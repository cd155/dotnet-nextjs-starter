using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.ApiAuth;
using static PlatformServer.Tests.Utilities.McpCalls;

namespace PlatformServer.Tests;

// The MCP endpoint through HTTP, with its sign-in configured: every request needs a bearer token, the CMS cookie never
// signs in /mcp, and the cookie still signs in the API and the CMS.
public class McpEndpointTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Email = "user@example.com";
    private const string Password = "Password1";

    private static readonly string[] UserTools = ["create_note", "get_note", "list_capabilities", "list_my_notes", "ping", "update_note"];

    // The token the OAuth flow issues signs in /mcp.
    [Fact]
    public async Task AValidToken_SignsInMcp()
    {
        var factory = await ResetFactoryAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient().WithToken(await McpTokens.GetAccessTokenAsync(factory, Email, Password));

        Assert.Equal(UserTools, await ListToolsAsync(client));
        Assert.NotEmpty((await CallAsync(client, "prompts/list", new { })).GetProperty("prompts").EnumerateArray());
    }

    // An admin also gets the tools marked [Authorize(Roles = Admin)].
    [Fact]
    public async Task AnAdmin_AlsoSeesTheAdminTools()
    {
        var factory = await ResetFactoryAsync();
        await factory.AddToRoleAsync(await factory.CreateUserAsync(Email, Password), RoleConstants.Admin);
        var client = factory.CreateClient().WithToken(await McpTokens.GetAccessTokenAsync(factory, Email, Password));

        var tools = await ListToolsAsync(client);

        Assert.Equal(UserTools.Append("count_notes").Order(StringComparer.Ordinal).ToArray(), tools);
    }

    // A user without the role can't call the admin tool by name either.
    [Fact]
    public async Task AUser_CannotCallAnAdminTool()
    {
        var factory = await ResetFactoryAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient().WithToken(await McpTokens.GetAccessTokenAsync(factory, Email, Password));

        var answer = await SendAsync(client, "tools/call", new { name = "count_notes", arguments = new { } });

        Assert.True(answer.TryGetProperty("error", out _) || IsError(answer.GetProperty("result")), answer.ToString());
    }

    // A signed-out client can call nothing, and the 401 tells it where to sign in.
    [Theory]
    [InlineData("tools/list", null)]
    [InlineData("prompts/list", null)]
    [InlineData("tools/call", "ping")]
    [InlineData("tools/call", "list_my_notes")]
    public async Task SignedOut_GetsA401ThatPointsAtTheMetadata(string method, string? tool)
    {
        var factory = await ResetFactoryAsync();

        await AssertChallengedAsync(factory.CreateClient(), method, tool);
    }

    // The CMS cookie would let any page in the admin's browser call admin tools, so /mcp ignores it.
    [Fact]
    public async Task AnAdminsCookie_DoesNotSignInMcp()
    {
        var factory = await ResetFactoryAsync();
        await factory.AddToRoleAsync(await factory.CreateUserAsync(Email, Password), RoleConstants.Admin);
        var client = factory.CreateClient();
        await SignInAsync(client, Email, Password);

        await AssertChallengedAsync(client, "tools/list");
    }

    // Without the sign-in settings nobody can sign in to /mcp, and it still ignores the cookie rather than falling back
    // to it.
    [Fact]
    public async Task WithoutMcpSignIn_TheCookieDoesNotSignInMcp_ButStillSignsInTheApi()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client, Email, Password);

        using var mcp = await PostAsync(client, "tools/list", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
    }

    [Fact]
    public async Task AnInvalidBearerToken_GetsA401ThatPointsAtTheMetadata()
    {
        var factory = await ResetFactoryAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        await AssertChallengedAsync(client, "tools/list");
    }

    // Where a chat app learns which authorization server to sign in with.
    [Fact]
    public async Task TheProtectedResourceMetadata_NamesTheResourceAndTheAuthority()
    {
        var factory = await ResetFactoryAsync();

        var metadata = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");

        Assert.Equal(McpTokens.Resource, metadata.GetProperty("resource").GetString());
        Assert.Equal(McpTokens.Authority, metadata.GetProperty("authorization_servers")[0].GetString()?.TrimEnd('/'));
    }

    // The bearer token is for /mcp only: the API and the CMS keep the cookie.
    [Fact]
    public async Task SignedOut_TheApiReturns401_AndTheCmsRedirectsToLogin()
    {
        var factory = await ResetFactoryAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var api = await client.GetAsync("/api/v1/me/notes");
        var cms = await client.GetAsync("/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, cms.StatusCode);
        Assert.Equal("/login", cms.Headers.Location?.AbsolutePath);
    }

    private static async Task AssertChallengedAsync(HttpClient client, string method, string? tool = null)
    {
        using var response = await PostAsync(client, method, tool is null ? new { } : new { name = tool, arguments = new { } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=", challenge.Parameter);
    }

    private Task<AppApiFactory> ResetFactoryAsync() => shared.ResetAsync(() => McpTokens.CreateFactory());
}
