using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// What the web app's reset-password page posts: the link's user ID and code, and the new password. An explicit
// null is a 400 with the errors by field; a wrong user ID or code is an invalid link.
public sealed record ResetPasswordRequest
{
    [Required(AllowEmptyStrings = true)]
    public string UserId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string Code { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string NewPassword { get; init; } = string.Empty;
}
