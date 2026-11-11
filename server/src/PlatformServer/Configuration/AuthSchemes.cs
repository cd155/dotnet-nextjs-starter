namespace PlatformServer.Configuration;

/// <summary>
/// The API takes two credentials. The Identity cookie serves the web app's server and the Blazor CMS. An OAuth
/// access token, issued by this app's OpenIddict server, serves the MCP endpoint (<c>McpFeature</c>), and only it.
/// </summary>
public static class AuthSchemes
{
    /// <summary>The rate-limit policy for <c>/mcp</c>: per signed-in user, as the global limit is per IP for <c>/api</c>.</summary>
    public const string McpRateLimit = "mcp";
}
