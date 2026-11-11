namespace PlatformServer.Features.Account;

public sealed record UserProfile
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}
