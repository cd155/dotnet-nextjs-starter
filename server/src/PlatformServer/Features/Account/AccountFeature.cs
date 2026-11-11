using System.Security.Claims;
using System.Threading.RateLimiting;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PlatformServer.Configuration;
using SharedLibrary.Exceptions;

namespace PlatformServer.Features.Account;

/// <summary>
/// Registration, email confirmation, sign-in, password reset by email, and the signed-in user's profile and
/// password, for the web app's cookie session. The CMS's login form is <c>AuthFeature</c>.
/// </summary>
public static class AccountFeature
{
    private const string ResetLinksPerEmail = "reset-links-per-email";
    private const string DeletionCodesPerUser = "deletion-codes-per-user";

    public static IServiceCollection AddAccount(this IServiceCollection services)
    {
        services.AddOptions<WebAppOptions>()
            .BindConfiguration(WebAppOptions.SectionName);
        services.AddScoped<AccountEmails>();
        services.AddSingleton<ConfirmationEmailLimits>();
        services.AddOptions<TurnstileOptions>()
            .BindConfiguration(TurnstileOptions.SectionName);
        services.AddHttpClient<TurnstileVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<UnconfirmedAccountCleanup>();

        // At most three reset links an hour to one address, however many IPs ask, so nobody can flood an inbox.
        // The "email" policy can't do this: it partitions before the request body is read.
        services.AddKeyedSingleton(ResetLinksPerEmail, (_, _) => PartitionedRateLimiter.Create<string, string>(email =>
            RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            })));

        // At most three account deletion codes an hour to one user.
        services.AddKeyedSingleton(DeletionCodesPerUser, (_, _) => PartitionedRateLimiter.Create<string, string>(userId =>
            RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            })));

        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var account = api.MapGroup("account").WithTags("Account");

        // POST /api/v1/account/register. Sends the confirmation link; the user signs in once the email is confirmed.
        account.MapPost("register", async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> (
            RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            AccountEmails accountEmails,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(AccountFeature));

            // An unconfirmed account takes the new password and name, so whoever registered the address first
            // has no way in once its owner confirms it. Resetting the password checks it against the rules, saves
            // the name with it, and rotates the security stamp, which voids the earlier link. The address isn't
            // confirmed, so no password-changed email goes to it.
            var existing = await userManager.FindByEmailAsync(request.Email);
            if (existing is { EmailConfirmed: false })
            {
                existing.DisplayName = request.DisplayName;
                existing.UpdatedTime = DateTime.UtcNow;

                var resetToken = await userManager.GeneratePasswordResetTokenAsync(existing);
                var resetResult = await userManager.ResetPasswordAsync(existing, resetToken, request.Password);

                if (!resetResult.Succeeded)
                {
                    return TypedResults.ValidationProblem(Errors(resetResult));
                }

                await accountEmails.QueueConfirmationAsync(existing);

                logger.LogInformation("Unconfirmed user registered again: {Email}", request.Email);

                return TypedResults.NoContent();
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName,
                CreatedTime = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return TypedResults.ValidationProblem(Errors(result));
            }

            var roleResult = await userManager.AddToRoleAsync(user, RoleConstants.User);

            if (!roleResult.Succeeded)
            {
                // A 500 without an exception, so nothing else logs it, and Sentry wouldn't hear of it.
                logger.LogError(
                    "Registration failed to assign the User role: {Errors}", string.Join(" ", roleResult.Errors.Select(e => e.Description)));
                await userManager.DeleteAsync(user);
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Registration failed",
                    detail: "Failed to assign user role. Please try again.");
            }

            await accountEmails.QueueConfirmationAsync(user);

            logger.LogInformation("New user registered: {Email}", request.Email);

            return TypedResults.NoContent();
        })
            .AllowAnonymous()
            .RequireRateLimiting("email")
            .RequireTurnstile();

        // POST /api/v1/account/confirm-email. Doesn't sign in: the link works until it expires, so signing in would
        // make it a reusable login link.
        account.MapPost("confirm-email", async Task<Results<NoContent, ProblemHttpResult>> (
            ConfirmEmailRequest request,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(request.UserId);

            // A second click, or the page posting twice, still ends at "confirmed".
            if (user is { EmailConfirmed: true })
            {
                return TypedResults.NoContent();
            }

            var token = AccountEmails.DecodeCode(request.Code);
            if (user is null || token is null || !(await userManager.ConfirmEmailAsync(user, token)).Succeeded)
            {
                return InvalidLink();
            }

            return TypedResults.NoContent();
        })
            // Only the global limiter: the code can't be guessed, and sharing "login" would turn away a user who
            // follows the link right after a few failed sign-ins.
            .AllowAnonymous();

        // POST /api/v1/account/resend-confirmation. The same answer whether or not the email has an account.
        account.MapPost("resend-confirmation", async Task<NoContent> (
            ResendConfirmationRequest request,
            UserManager<ApplicationUser> userManager,
            AccountEmails accountEmails) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is { EmailConfirmed: false })
            {
                // A new link, so UnconfirmedAccountCleanup keeps the account until it expires.
                user.UpdatedTime = DateTime.UtcNow;
                await userManager.UpdateAsync(user);
                await accountEmails.QueueConfirmationAsync(user);
            }

            return TypedResults.NoContent();
        })
            .AllowAnonymous()
            .RequireRateLimiting("email")
            .RequireTurnstile();

        // POST /api/v1/account/forgot-password. The same answer whether or not the email has an account, and
        // whether or not it's past its own limit. A link goes only to a confirmed email, since a reset is only as
        // safe as the address it goes to.
        account.MapPost("forgot-password", async Task<NoContent> (
            ForgotPasswordRequest request,
            UserManager<ApplicationUser> userManager,
            AccountEmails accountEmails,
            [FromKeyedServices(ResetLinksPerEmail)] PartitionedRateLimiter<string> perEmailLimit) =>
        {
            // Counted before the lookup, so an address without an account is limited the same way.
            using var lease = perEmailLimit.AttemptAcquire(userManager.NormalizeEmail(request.Email) ?? string.Empty);
            if (!lease.IsAcquired)
            {
                return TypedResults.NoContent();
            }

            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is { EmailConfirmed: true })
            {
                await accountEmails.QueuePasswordResetAsync(user);
            }

            return TypedResults.NoContent();
        })
            .AllowAnonymous()
            .RequireRateLimiting("email");

        // POST /api/v1/account/reset-password. Doesn't sign in, like confirm-email: the user signs in with the new
        // password.
        account.MapPost("reset-password", async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> (
            ResetPasswordRequest request,
            UserManager<ApplicationUser> userManager,
            AccountEmails accountEmails) =>
        {
            var user = await userManager.FindByIdAsync(request.UserId);
            var token = AccountEmails.DecodeCode(request.Code);
            if (user is null || token is null)
            {
                return InvalidLink();
            }

            // Following the link proves the user owns the address, so it also lifts a lockout from wrong passwords.
            // The reset saves these with the new password, or nothing when it fails.
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;

            // The reset rotates the security stamp, which voids this link and ends every cookie session at its next
            // check. Identity checks the token before the password rules, so
            // an expired link with a weak password is still an invalid link.
            var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded)
            {
                return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
                    ? InvalidLink()
                    : TypedResults.ValidationProblem(Errors(result));
            }

            accountEmails.QueuePasswordChanged(user);

            return TypedResults.NoContent();
        })
            // Only the global limiter, as for confirm-email: the code can't be guessed.
            .AllowAnonymous();

        // POST /api/v1/account/login
        account.MapPost("login", async Task<IResult> (
            LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var result = await signInManager.PasswordSignInAsync(
                request.Email, request.Password, isPersistent: true, lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                return await SignInFailedAsync(result, request, userManager);
            }

            var user = await userManager.FindByEmailAsync(request.Email)
                ?? throw new UnauthorizedException();

            return TypedResults.Ok(await ProfileAsync(userManager, user));
        })
            .Produces<UserProfile>()
            .AllowAnonymous()
            .RequireRateLimiting("login");

        // POST /api/v1/account/logout
        account.MapPost("logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.NoContent();
        });

        // POST /api/v1/account/sign-out-everywhere
        // Ends every session, for a lost computer: a new security stamp, which
        // Identity's periodic stamp check ends cookie sessions.
        account.MapPost("sign-out-everywhere", async Task<Results<NoContent, ProblemHttpResult>> (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory loggerFactory) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            var result = await userManager.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
            {
                // A 500 without an exception, so nothing else logs it, and Sentry wouldn't hear of it.
                loggerFactory.CreateLogger(typeof(AccountFeature)).LogError(
                    "Sign out everywhere failed for user {UserId}: {Errors}", user.Id, string.Join(" ", result.Errors.Select(e => e.Description)));
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Sign out failed",
                    detail: "Your sessions could not be ended. Please try again.");
            }

            await signInManager.SignOutAsync();

            loggerFactory.CreateLogger(typeof(AccountFeature)).LogInformation("User {UserId} signed out everywhere", user.Id);
            return TypedResults.NoContent();
        });

        // GET /api/v1/account/profile
        account.MapGet("profile", async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            return await ProfileAsync(userManager, user);
        });

        // PUT /api/v1/account/profile
        account.MapPut("profile", async Task<IResult> (
            UpdateProfileRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            user.DisplayName = request.DisplayName;
            user.UpdatedTime = DateTime.UtcNow;

            var result = await userManager.UpdateAsync(user);

            return result.Succeeded
                ? TypedResults.Ok(await ProfileAsync(userManager, user))
                : TypedResults.ValidationProblem(Errors(result));
        })
            .Produces<UserProfile>();

        // POST /api/v1/account/change-password
        account.MapPost("change-password", async Task<Results<NoContent, ValidationProblem>> (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            AccountEmails accountEmails) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                return TypedResults.ValidationProblem(Errors(result));
            }

            // The change rotates the security stamp, as sign-out-everywhere does: every cookie session ends at its
            // next check. A new cookie keeps this one signed in.
            await signInManager.RefreshSignInAsync(user);

            accountEmails.QueuePasswordChanged(user);

            return TypedResults.NoContent();
        })
            // Lockout doesn't count wrong current passwords, so this limit is what stops guessing.
            .RequireRateLimiting("login");

        // POST /api/v1/account/deletion-code. Emails the code that, with the password, deletes the account.
        account.MapPost("deletion-code", async Task<Results<NoContent, ProblemHttpResult>> (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            AccountEmails accountEmails,
            [FromKeyedServices(DeletionCodesPerUser)] PartitionedRateLimiter<string> perUserLimit) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            if (await DeletionBlockAsync(userManager, user) is { } blocked)
            {
                return blocked;
            }

            using var lease = perUserLimit.AttemptAcquire(user.Id);
            if (!lease.IsAcquired)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many codes",
                    detail: "Please wait before asking for another code.");
            }

            await accountEmails.QueueDeletionCodeAsync(user);

            return TypedResults.NoContent();
        });

        // POST /api/v1/account/delete. Deletes the account and, through the database's cascades, everything the user
        // owns, such as their notes. An MCP refresh token fails without the user (AuthFeature's token endpoint).
        account.MapPost("delete", async Task<Results<NoContent, ProblemHttpResult>> (
            DeleteAccountRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            AccountEmails accountEmails) =>
        {
            var user = await userManager.GetUserAsync(principal)
                ?? throw new UnauthorizedException();

            if (await DeletionBlockAsync(userManager, user) is { } blocked)
            {
                return blocked;
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                return Locked();
            }

            // The code first: a right password resets the failed count, so checking it first would let whoever knows
            // the password guess codes without ever being locked out.
            if (!await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, AccountEmails.DeletionPurpose, request.Code))
            {
                await userManager.AccessFailedAsync(user);
                return await userManager.IsLockedOutAsync(user) ? Locked() : WrongPasswordOrCode();
            }

            var password = await signInManager.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
            if (password.IsLockedOut)
            {
                return Locked();
            }

            if (!password.Succeeded)
            {
                return WrongPasswordOrCode();
            }

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Couldn't delete user {user.Id}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
            }

            accountEmails.QueueAccountDeleted(user);

            // Other cookie sessions end at their next security stamp check, which no longer finds the user.
            await signInManager.SignOutAsync();

            return TypedResults.NoContent();
        })
            // Lockout counts wrong codes and passwords; this limit slows the guesses in between.
            .RequireRateLimiting("login");

        return api;
    }

    /// <summary>Why the account can't be deleted, or null when it can: staff give up their role first.</summary>
    private static async Task<ProblemHttpResult?> DeletionBlockAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.ContentManager)
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Staff account",
                detail: "Staff accounts can't be deleted. Ask an admin to remove your role first.")
            : null;
    }

    private static ProblemHttpResult Locked() => TypedResults.Problem(
        statusCode: StatusCodes.Status423Locked,
        title: "Account locked",
        detail: "Too many failed attempts. Please try again later.");

    /// <summary>The same answer for either, so a guesser doesn't learn which one was right.</summary>
    private static ProblemHttpResult WrongPasswordOrCode() => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Wrong password or code",
        detail: "The password or the code is wrong, or the code has expired.");

    private static async Task<UserProfile> ProfileAsync(UserManager<ApplicationUser> userManager, ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        DisplayName = user.DisplayName,
        Roles = [.. await userManager.GetRolesAsync(user)]
    };

    /// <summary>An emailed link that has expired, was used, or was never one of ours.</summary>
    private static ProblemHttpResult InvalidLink() => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid link",
        detail: "This link has expired or isn't valid.");

    // The same failures for cookie and token sign-in. Identity refuses an unconfirmed user before it checks the
    // lockout or the password, so this checks both: a wrong password counts towards the lockout as it does for
    // everyone, and only the account's owner learns that the email waits for confirmation.
    private static async Task<ProblemHttpResult> SignInFailedAsync(
        Microsoft.AspNetCore.Identity.SignInResult result,
        LoginRequest request,
        UserManager<ApplicationUser> userManager)
    {
        var unconfirmed = result.IsNotAllowed ? await userManager.FindByEmailAsync(request.Email) : null;

        if (result.IsLockedOut || (unconfirmed is not null && await userManager.IsLockedOutAsync(unconfirmed)))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account locked",
                detail: "Too many failed attempts. Please try again later.");
        }

        if (unconfirmed is { EmailConfirmed: false })
        {
            if (await userManager.CheckPasswordAsync(unconfirmed, request.Password))
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Email not confirmed",
                    detail: "Please confirm your email first.");
            }

            await userManager.AccessFailedAsync(unconfirmed);
        }

        return TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials",
            detail: "The email or password is incorrect.");
    }

    /// <summary>Identity's errors keyed by their codes, as the controller's ModelState had them.</summary>
    private static Dictionary<string, string[]> Errors(IdentityResult result)
        => result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
