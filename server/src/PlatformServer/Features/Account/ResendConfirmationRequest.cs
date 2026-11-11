using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// An explicit null is a 400 with the errors by field; any other address gets the same answer.
public sealed record ResendConfirmationRequest : ITurnstileProtected
{
    [Required(AllowEmptyStrings = true)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The token the web app's Cloudflare Turnstile widget gave the user, which shows a person filled in the form.
    /// Required unless the API skips the check (Development without a secret key); a token works once.
    /// </summary>
    public string? TurnstileToken { get; init; }
}
