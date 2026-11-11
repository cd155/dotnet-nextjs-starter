using System.Text;
using System.Text.Encodings.Web;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using PlatformServer.Features.Email;

namespace PlatformServer.Features.Account;

/// <summary>The web app, from the <c>WebApp</c> section; emailed links open its pages.</summary>
public sealed class WebAppOptions
{
    public const string SectionName = "WebApp";

    /// <summary>The web app's public URL, without a trailing slash: https://app.example.com in production.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// The emails the account sends: the links that confirm an address and reset a password, and the notice that a
/// password changed. A link opens a web app page (<c>confirm-email</c>, <c>reset-password</c>), which posts the
/// user ID and code back to the API.
/// </summary>
/// <remarks>
/// The link is made here, during the request, and the email is sent later from the <see cref="IEmailQueue"/>, so a
/// slow SMTP server doesn't hold the request. A failed send is logged: what the user asked for still happens,
/// and they can ask for another link.
/// </remarks>
public sealed class AccountEmails(
    UserManager<ApplicationUser> userManager,
    IAccountEmailSender emailSender,
    IEmailQueue emailQueue,
    IOptions<WebAppOptions> webApp,
    ConfirmationEmailLimits confirmationLimits,
    ILogger<AccountEmails> logger)
{
    /// <summary>
    /// Queues the link that confirms the user's address, unless <see cref="ConfirmationEmailLimits"/> is used up for
    /// the address or the day. Then nothing is sent, and the caller answers as if it were, as for any address.
    /// </summary>
    public async Task QueueConfirmationAsync(ApplicationUser user)
    {
        switch (confirmationLimits.TryTake(user.NormalizedEmail ?? userManager.NormalizeEmail(user.Email!)))
        {
            case ConfirmationEmailLimits.Refusal.Address:
                logger.LogWarning("Not sending another confirmation email to {Email} today", user.Email);
                return;
            case ConfirmationEmailLimits.Refusal.Daily:
                // An error, so Sentry hears of it: either sign-ups took off or a script got past Turnstile.
                logger.LogError("Not sending the confirmation email to {Email}: today's {Limit} are sent", user.Email, ConfirmationEmailLimits.PerDay);
                return;
        }

        var link = Link("confirm-email", user, await userManager.GenerateEmailConfirmationTokenAsync(user));
        Queue(user, "confirmation", email => emailSender.SendConfirmationLinkAsync(user, email, link));
    }

    public async Task QueuePasswordResetAsync(ApplicationUser user)
    {
        var link = Link("reset-password", user, await userManager.GeneratePasswordResetTokenAsync(user));
        Queue(user, "password reset", email => emailSender.SendPasswordResetLinkAsync(user, email, link));
    }

    public void QueuePasswordChanged(ApplicationUser user)
    {
        var link = HtmlEncoder.Default.Encode(PageUrl("forgot-password"));
        Queue(user, "password changed", email => emailSender.SendPasswordChangedAsync(user, email, link));
    }

    /// <summary>The purpose of the code that deletes an account, so a code made for anything else doesn't.</summary>
    public const string DeletionPurpose = "DeleteAccount";

    /// <summary>
    /// Queues the 6-digit code that, with the password, deletes the account. Identity's email provider makes it; it
    /// lasts a few minutes, and a password change voids it.
    /// </summary>
    public async Task QueueDeletionCodeAsync(ApplicationUser user)
    {
        var code = await userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, DeletionPurpose);
        Queue(user, "account deletion code", email => emailSender.SendDeletionCodeAsync(user, email, code));
    }

    /// <summary>Confirms the account is gone. Called after the delete: the user object still holds the address.</summary>
    public void QueueAccountDeleted(ApplicationUser user)
        => Queue(user, "account deleted", email => emailSender.SendAccountDeletedAsync(user, email));

    /// <summary>The token a link carries, from its <c>code</c>; null when the code isn't one this class made.</summary>
    public static string? DecodeCode(string code)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The web app page with the user ID and token, HTML-encoded for the email's body.</summary>
    private string Link(string page, ApplicationUser user, string token)
    {
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = QueryHelpers.AddQueryString(
            PageUrl(page),
            new Dictionary<string, string?> { ["userId"] = user.Id, ["code"] = code });

        return HtmlEncoder.Default.Encode(link);
    }

    private string PageUrl(string page) => $"{webApp.Value.BaseUrl.TrimEnd('/')}/{page}";

    // The address is read now, while the request's user is current.
    private void Queue(ApplicationUser user, string kind, Func<string, Task> send)
    {
        var email = user.Email!;
        emailQueue.Enqueue(async () =>
        {
            try
            {
                await send(email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't send the {Kind} email to {Email}", kind, email);
            }
        });
    }
}
