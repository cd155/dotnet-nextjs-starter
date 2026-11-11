using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace PlatformServer.Features.Email;

/// <summary>
/// Identity's emails, and the account's own that Identity has no method for. Its second implementation is the tests'
/// <c>FakeEmailSender</c>, which records the emails instead of sending them.
/// </summary>
public interface IAccountEmailSender : IEmailSender<ApplicationUser>
{
    /// <summary>
    /// Tells the user their password changed, with a link to reset it in case it wasn't them. The link is the
    /// web app's forgot-password page, HTML-encoded like Identity's links.
    /// </summary>
    Task SendPasswordChangedAsync(ApplicationUser user, string email, string resetLink);

    /// <summary>Sends the 6-digit code that, with the password, deletes the account.</summary>
    Task SendDeletionCodeAsync(ApplicationUser user, string email, string code);

    /// <summary>Tells the user their account and its data were deleted.</summary>
    Task SendAccountDeletedAsync(ApplicationUser user, string email);
}
