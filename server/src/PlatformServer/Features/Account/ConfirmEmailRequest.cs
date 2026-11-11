using System.ComponentModel.DataAnnotations;

namespace PlatformServer.Features.Account;

// What the web app's confirm-email page posts: the link's user ID and code. An explicit null is a 400 with the
// errors by field; an empty or wrong one is an invalid link.
public sealed record ConfirmEmailRequest
{
    [Required(AllowEmptyStrings = true)]
    public string UserId { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string Code { get; init; } = string.Empty;
}
