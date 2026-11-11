using System.Security.Cryptography.X509Certificates;
using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using DataLibrary.Interceptors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using PlatformServer.Features.Account;

namespace PlatformServer.Configuration;

/// <summary>
/// Provides service registration extensions to organize and configure dependency injection.
/// This class improves the maintainability and readability of the Program.cs file.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in exception handling: <see cref="AppExceptionHandler"/> answers the app's exceptions,
    /// and every problem body, including the endpoints' own, is written through <see cref="IProblemDetailsService"/>.
    /// </summary>
    /// <param name="services">The service collection to add exception handling to</param>
    /// <param name="environment">Hosting environment; a 500 shows the exception message only in Development</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddExceptionHandling(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;

            // AppExceptionHandler always sets a detail, so this reaches only the built-in handler's 500.
            if (environment.IsDevelopment() && context.Exception is not null)
            {
                context.ProblemDetails.Detail ??= context.Exception.Message;
            }
        });

        return services;
    }

    /// <summary>
    /// Configures the database context with the appropriate connection string.
    /// Handles both environment variable and configuration file sources.
    /// </summary>
    /// <param name="services">The service collection to add the DbContext to</param>
    /// <param name="configuration">The application configuration</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddDatabaseContext(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }

        services.AddSingleton<AuditTimestampInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());

            // OpenIddict's entity sets on the shared context. Every host that builds this context (and the
            // design-time factory) registers them, so the model matches the migrations.
            options.UseOpenIddict();
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>();

        return services;
    }

    /// <summary>
    /// Configures ASP.NET Core Identity with cookie authentication.
    /// </summary>
    /// <param name="services">The service collection to add authentication to</param>
    /// <param name="environment">Hosting environment; relaxes the cookie's Secure flag for local HTTP development</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;

                // A new user confirms their email before signing in.
                options.SignIn.RequireConfirmedEmail = true;
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.ProviderName);

        // The default provider makes only password reset tokens, so this is how long a reset link lasts.
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/forbidden";
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
            options.Cookie.HttpOnly = true;
            // Dev: Secure only over HTTPS so login works on local HTTP non-localhost hosts. Prod: always Secure.
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;

            // Return 401/403 for API requests instead of redirecting
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        // How often a cookie's security stamp is checked against the user's. A password change signs other
        // sessions out at their next check; the default of 30 minutes is long for that.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

        // Secure-by-default: every endpoint requires an authenticated user unless it
        // explicitly opts out with AllowAnonymous. This ensures a newly added endpoint
        // or page that forgets to say who may use it fails closed (protected) rather than open (public).
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        services.AddCascadingAuthenticationState();

        return services;
    }

    /// <summary>
    /// Registers the OpenIddict OAuth 2.1 Authorization Server on top of the existing ASP.NET
    /// Identity users. This is additive — cookie authentication (see
    /// <see cref="AddIdentityAuthentication"/>) still backs the Blazor UI and the API. OpenIddict
    /// issues the bearer tokens chatbots (Claude, MCP Inspector) send to <c>/mcp</c>, which this app checks itself
    /// (<c>McpFeature</c>); the authorize/token/userinfo requests are handled by <c>AuthFeature</c>.
    /// </summary>
    /// <param name="services">The service collection to add the authorization server to</param>
    /// <param name="environment">Hosting environment, used to pick dev vs production signing keys</param>
    /// <param name="configuration">Application configuration (production certificate material)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddOpenIddictServer(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        // RFC 8707 resource indicator (the MCP server's public URL) that MCP clients send as the
        // resource= parameter on the authorize request. It must be allowlisted on the server
        // (RegisterResources below) or OpenIddict rejects the request with invalid_target (ID2190).
        // Read once here so the same value is both registered and logged at startup, and must match
        // the URL /mcp advertises in its protected-resource metadata (McpFeature).
        var mcpResource = configuration["MCP_RESOURCE"];

        services.AddOpenIddict()
            .AddCore(options =>
            {
                // OpenIddict stores its applications, authorizations, scopes and tokens in the shared
                // AppDbContext (entity sets registered by options.UseOpenIddict()).
                options.UseEntityFrameworkCore()
                    .UseDbContext<AppDbContext>();
            })
            .AddServer(options =>
            {
                // Without a set issuer OpenIddict takes it from each request's host. Tokens are issued on this
                // app's own host (MCP_AUTHORITY) but checked on requests to the MCP host (McpFeature), so
                // both must name the same issuer. Unset (tests, a stack without MCP sign-in): per request.
                var authority = configuration["MCP_AUTHORITY"];
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.SetIssuer(new Uri(authority));
                }

                options.SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo");

                // Authorization code + PKCE is the flow hosted connectors use; refresh
                // tokens keep them connected without re-prompting. PKCE is mandatory.
                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange();

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Roles,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    "mcp");

                // Allowlist the MCP resource indicator (read above) so OpenIddict accepts the clients'
                // resource= parameter instead of rejecting it with invalid_target (ID2190). This is
                // independent of the scope/resource binding the DatabaseInitializer sets — that only
                // governs the aud claim once a token is actually issued.
                if (!string.IsNullOrWhiteSpace(mcpResource))
                {
                    options.RegisterResources(mcpResource);
                }

                // Emit plain signed JWT access tokens (not the default encrypted JWE). /mcp validates
                // them in-process (McpFeature).
                options.DisableAccessTokenEncryption();

                if (environment.IsDevelopment())
                {
                    // Ephemeral, auto-managed certificates — local development only.
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // Production needs persistent keys, or a restart invalidates every live token.
                    // Both are required even with token encryption disabled: OpenIddict still
                    // encrypts authorization codes and refresh tokens with the encryption key.
                    options.AddSigningCertificate(LoadCertificate(configuration, "OpenIddict:SigningCertificate"));
                    options.AddEncryptionCertificate(LoadCertificate(configuration, "OpenIddict:EncryptionCertificate"));
                }

                // Hand authorize/token/userinfo to AuthFeature's endpoints; OpenIddict still serves
                // discovery (/.well-known/openid-configuration) and JWKS itself.
                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();

                // In development the host is hit directly over plain HTTP. In production Caddy
                // terminates TLS and forwards X-Forwarded-Proto=https (UseForwardedHeaders), so
                // OpenIddict sees a secure request and this requirement stays enforced.
                if (environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });

        return services;
    }

    /// <summary>
    /// Loads a base64-encoded PKCS#12 certificate from configuration for OpenIddict's production
    /// signing/encryption keys. Throws if absent so misconfiguration fails fast at startup.
    /// </summary>
    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string configKey)
    {
        var encoded = configuration[configKey];

        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new InvalidOperationException(
                $"{configKey} is not configured. Provide a base64-encoded PFX so OpenIddict has " +
                "persistent keys in production (otherwise every restart invalidates live tokens).");
        }

        // EphemeralKeySet keeps the private key in memory only (no OS key store / filesystem),
        // which is the right choice in Linux containers — the PFX in configuration is the stable
        // source of truth and is reloaded on every start.
        return X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(encoded),
            configuration["OpenIddict:CertificatePassword"],
            X509KeyStorageFlags.EphemeralKeySet);
    }
}
