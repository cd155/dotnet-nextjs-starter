using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// An explicit null email or password is a 400 with the errors by field; Identity's rules answer the rest.
public sealed record RegisterRequest : ITurnstileProtected
{
    [Required(AllowEmptyStrings = true)]
    public string Email { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string Password { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    /// <summary>
    /// The token the web app's Cloudflare Turnstile widget gave the user, which shows a person filled in the form.
    /// Required unless the API skips the check (Development without a secret key); a token works once.
    /// </summary>
    public string? TurnstileToken { get; init; }
}
