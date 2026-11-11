namespace PlatformServer.Features.Account;

public sealed record DeleteAccountRequest
{
    public required string CurrentPassword { get; init; }

    /// <summary>The 6-digit code from the email that <c>deletion-code</c> sent.</summary>
    public required string Code { get; init; }
}
