using DataLibrary.Entities.Identity;
using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.McpCalls;

namespace PlatformServer.Tests;

// list_capabilities through /mcp: for each role it lists exactly what tools/list and prompts/list show that user.
public class McpCapabilitiesTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Password = "Password1";

    [Theory]
    [InlineData(null)]
    [InlineData(RoleConstants.ContentManager)]
    [InlineData(RoleConstants.Admin)]
    public async Task ListCapabilities_MatchesWhatTheUserIsListed(string? role)
    {
        var factory = await shared.ResetAsync(() => McpTokens.CreateFactory());
        var client = await ClientAsync(factory, role);

        var capabilities = await CallToolOkAsync(client, "list_capabilities", new { });

        Assert.Equal(await ListToolsAsync(client), NamesOf(capabilities, "tools"));
        var prompts = await CallAsync(client, "prompts/list", new { });
        Assert.Equal(NamesOf(prompts, "prompts"), NamesOf(capabilities, "prompts"));
        Assert.All(
            capabilities.GetProperty("tools").EnumerateArray(),
            tool => Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task ASignedInUser_DoesNotSeeTheAdminTools()
    {
        var factory = await shared.ResetAsync(() => McpTokens.CreateFactory());
        var client = await ClientAsync(factory, role: null);

        var tools = NamesOf(await CallToolOkAsync(client, "list_capabilities", new { }), "tools");

        Assert.Contains("list_my_notes", tools);
        Assert.DoesNotContain("count_notes", tools);
    }

    private static string[] NamesOf(System.Text.Json.JsonElement result, string property) =>
        result.GetProperty(property).EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task<HttpClient> ClientAsync(AppApiFactory factory, string? role)
    {
        var email = $"{role ?? "user"}@example.com".ToLowerInvariant();
        var userId = await factory.CreateUserAsync(email, Password);
        if (role is not null)
        {
            await factory.AddToRoleAsync(userId, role);
        }

        return factory.CreateClient().WithToken(await McpTokens.GetAccessTokenAsync(factory, email, Password));
    }
}
