using System.Security.Claims;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PlatformServer.Features.Auth;

/// <summary>
/// The CMS login form's sign-in and sign-out (Components/Pages/Login.razor), and the OAuth 2.1 authorize, token and
/// userinfo endpoints of the OpenIddict authorization server. OAuth reuses the Identity cookie login: the authorize
/// endpoint sends the user through the /login page and never creates accounts, so only registered users can get a
/// token. The routes sit at the site root and are left out of the OpenAPI document.
/// </summary>
public static class AuthFeature
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /Auth/login. Anonymous so the Blazor login page can post credentials without an authenticated
        // user. The form fields are nullable so a missing one reaches the "empty" redirect instead of a 400.
        app.MapPost("Auth/login", async (
            [FromForm] string? email,
            [FromForm] string? password,
            [FromForm] string? returnUrl,
            HttpContext httpContext,
            SignInManager<ApplicationUser> signInManager,
            IAntiforgery antiforgery,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(AuthFeature));
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            // Reject posts without the form's antiforgery token, so another site cannot sign a visitor into an
            // attacker's account (login CSRF). Checked here, with the automatic check turned off below, because
            // that one answers a failure with a bare 400 instead of sending the user back to the login page.
            if (!await antiforgery.IsRequestValidAsync(httpContext))
            {
                logger.LogWarning(
                    "Login attempt with a missing or invalid antiforgery token from {ClientIp}, UserAgent: {UserAgent}",
                    clientIp, userAgent);
                return Results.Redirect(BuildLoginErrorUrl("expired", returnUrl));
            }

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                logger.LogWarning(
                    "Login attempt with empty credentials from {ClientIp}, UserAgent: {UserAgent}",
                    clientIp, userAgent);
                return Results.Redirect(BuildLoginErrorUrl("empty", returnUrl));
            }

            var result = await signInManager.PasswordSignInAsync(
                email, password, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Successful login for {Email} from {ClientIp}, UserAgent: {UserAgent}",
                    email, clientIp, userAgent);
                // Honor a local return URL (the page the cookie challenge redirected away from, such as the
                // /connect/authorize round-trip);
                // fall back to the home page otherwise.
                return Results.Redirect(GetSafeReturnUrl(returnUrl));
            }

            if (result.IsLockedOut)
            {
                logger.LogWarning(
                    "Locked out login attempt for {Email} from {ClientIp}",
                    email, clientIp);
                return Results.Redirect(BuildLoginErrorUrl("locked", returnUrl));
            }

            logger.LogWarning(
                "Failed login attempt for {Email} from {ClientIp}, UserAgent: {UserAgent}",
                email, clientIp, userAgent);

            return Results.Redirect(BuildLoginErrorUrl("invalid", returnUrl));
        })
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .DisableAntiforgery()
            .ExcludeFromDescription();

        // GET|POST /Auth/logout. Deliberately not anonymous: it relies on the fallback policy.
        app.MapMethods("Auth/logout", [HttpMethods.Get, HttpMethods.Post], async (
            HttpContext httpContext,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory loggerFactory) =>
        {
            var userName = httpContext.User.Identity?.Name ?? "unknown";
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            loggerFactory.CreateLogger(typeof(AuthFeature)).LogInformation(
                "User {UserName} logged out from {ClientIp}",
                userName, clientIp);

            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        })
            .ExcludeFromDescription();

        // GET|POST /connect/authorize. Requires an interactive Identity sign-in; if the user has no auth cookie
        // yet, challenges the cookie scheme which redirects to /login. On success, issues the authorization code
        // carrying the user's claims and requested scopes.
        app.MapMethods("connect/authorize", [HttpMethods.Get, HttpMethods.Post], async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            IOpenIddictScopeManager scopeManager) =>
        {
            var request = httpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // Reuse the existing Identity cookie. If the user isn't signed in, send them through the
            // platform's normal /login page and return here afterwards — no account is created here,
            // so only already-registered users can complete the flow.
            var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (result?.Succeeded is not true)
            {
                var httpRequest = httpContext.Request;
                return Results.Challenge(
                    properties: new AuthenticationProperties
                    {
                        RedirectUri = httpRequest.PathBase + httpRequest.Path + QueryString.Create(
                            httpRequest.HasFormContentType ? httpRequest.Form : httpRequest.Query)
                    },
                    authenticationSchemes: [IdentityConstants.ApplicationScheme]);
            }

            var user = await userManager.GetUserAsync(result.Principal)
                ?? throw new InvalidOperationException("The signed-in user could not be loaded.");

            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role);

            var roles = await userManager.GetRolesAsync(user);

            identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Name, await userManager.GetUserNameAsync(user))
                .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
                .SetClaims(Claims.Role, [.. roles]);

            // Grant the requested scopes (the coarse "mcp" scope plus the standard identity scopes)
            // and attach the resources mapped to those scopes, so the access token carries the
            // matching aud claim, which /mcp checks (McpFeature).
            identity.SetScopes(request.GetScopes());

            var resources = new List<string>();
            await foreach (var resource in scopeManager.ListResourcesAsync(identity.GetScopes()))
            {
                resources.Add(resource);
            }
            identity.SetResources(resources);

            identity.SetDestinations(GetDestinations);

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // POST /connect/token. Exchanges an authorization code (or refresh token) for an access token,
        // re-using the principal OpenIddict stored when the code was issued.
        app.MapPost("connect/token", async (
            HttpContext httpContext,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var request = httpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            {
                throw new InvalidOperationException("The specified grant type is not supported.");
            }

            var result = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (result?.Succeeded is not true || result.Principal is null)
            {
                return InvalidGrant("The token is no longer valid.");
            }

            // Resolve the user from the "sub" claim carried by the token principal. UserManager's
            // GetUserAsync looks up the NameIdentifier claim, which this OpenIddict principal does not
            // carry, so use the subject directly. Then confirm the user can still sign in.
            var subject = result.Principal.GetClaim(Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null || !await signInManager.CanSignInAsync(user))
            {
                return InvalidGrant("The user is no longer allowed to sign in.");
            }

            return Results.SignIn(
                result.Principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // GET|POST /connect/userinfo. Returns standard claims for the bearer access token so OIDC-aware
        // clients can fetch the signed-in user's profile.
        app.MapMethods("connect/userinfo", [HttpMethods.Get, HttpMethods.Post], async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager) =>
        {
            // The token principal carries "sub", not the NameIdentifier claim GetUserAsync reads, so look the
            // user up by the subject, as the token endpoint does.
            var subject = principal.GetClaim(Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null)
            {
                return Results.Challenge(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            // Only return claims whose scope was granted, so a client can't obtain extra personal data
            // (name/email/roles) without requesting the matching scope.
            var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [Claims.Subject] = await userManager.GetUserIdAsync(user)
            };

            if (principal.HasScope(Scopes.Profile))
            {
                claims[Claims.Name] = await userManager.GetUserNameAsync(user);
            }

            if (principal.HasScope(Scopes.Email))
            {
                claims[Claims.Email] = await userManager.GetEmailAsync(user);
            }

            if (principal.HasScope(Scopes.Roles))
            {
                claims[Claims.Role] = await userManager.GetRolesAsync(user);
            }

            return Results.Ok(claims);
        })
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .ExcludeFromDescription();

        return app;
    }

    // Only redirect to local URLs after login to avoid open-redirect attacks. The cookie challenge sets this so
    // the user lands back on the page they originally requested. The built-in check is Url.IsLocalUrl's rule.
    private static string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl : "/";

    // Re-render the login page with an error code while preserving a (validated) return URL so a failed attempt
    // can be retried without losing the originally requested page.
    private static string BuildLoginErrorUrl(string error, string? returnUrl)
    {
        var url = $"/login?error={error}";

        if (!string.IsNullOrEmpty(returnUrl) && RedirectHttpResult.IsLocalUrl(returnUrl))
        {
            url += $"&ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        return url;
    }

    private static IResult InvalidGrant(string description) =>
        Results.Forbid(
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    /// <summary>
    /// Controls which token each claim is written to: every claim is in the access token (so the
    /// resource server sees the user), and name/email/roles are additionally copied to the identity
    /// token only when the corresponding OIDC scope (profile/email/roles) was granted.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Every claim goes to the access token (the MCP resource server reads the user from there).
        // A claim is additionally copied to the identity token only when its OIDC scope was granted,
        // so name/email/roles are never exposed to a client that didn't request the matching scope.
        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Email))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Roles))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
