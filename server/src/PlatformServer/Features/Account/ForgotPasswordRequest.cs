using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// An explicit null is a 400 with the errors by field; any other address gets the same answer.
public sealed record ForgotPasswordRequest
{
    [Required(AllowEmptyStrings = true)]
    public string Email { get; init; } = string.Empty;
}
