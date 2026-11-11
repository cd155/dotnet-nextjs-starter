using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// Cookie or token sign-in. An explicit null is a 400 with the errors by field, as [ApiController] answered; an
// empty or missing field is a failed sign-in.
public sealed record LoginRequest
{
    [Required(AllowEmptyStrings = true)]
    public string Email { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string Password { get; init; } = string.Empty;
}
