using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// An explicit null is a 400 with the errors by field; Identity answers a wrong current password and its rules.
public sealed record ChangePasswordRequest
{
    [Required(AllowEmptyStrings = true)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string NewPassword { get; init; } = string.Empty;
}
