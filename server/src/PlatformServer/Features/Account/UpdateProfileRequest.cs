namespace PlatformServer.Features.Account;

public sealed record UpdateProfileRequest
{
    public string? DisplayName { get; init; }
}
