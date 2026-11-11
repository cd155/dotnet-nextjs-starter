using System.Net.Http.Json;
using System.Text.Json;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// The OpenAPI document is what the web app generates its API types from, so it has to match what the API sends.
public class OpenApiDocumentTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    [Theory]
    [InlineData("/api/v{version}/me/notes")]
    [InlineData("/api/v{version}/me/notes/{id}")]
    [InlineData("/api/v{version}/account/profile")]
    public async Task ThePaths_AreListed(string path)
    {
        var document = await GetDocumentAsync();

        Assert.True(document.GetProperty("paths").TryGetProperty(path, out _), $"{path} is missing.");
    }

    // RequiredPropertiesSchemaTransformer marks a property the API always sends as required, so the generated type
    // doesn't make it optional.
    [Fact]
    public async Task ANote_RequiresEveryPropertyItAlwaysHas()
    {
        var document = await GetDocumentAsync();

        var required = document.GetProperty("components").GetProperty("schemas").GetProperty("Note").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).ToList();
        Assert.Superset(new HashSet<string?> { "id", "title", "body", "isPinned", "createdTime" }, required.ToHashSet());
    }

    // The OAuth and MCP endpoints aren't part of the API the web app calls.
    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/token")]
    [InlineData("/mcp")]
    public async Task TheSignInAndMcpEndpoints_AreLeftOut(string path)
    {
        var document = await GetDocumentAsync();

        Assert.False(document.GetProperty("paths").TryGetProperty(path, out _), $"{path} is listed.");
    }

    private async Task<JsonElement> GetDocumentAsync()
    {
        var factory = await shared.ResetAsync();
        return await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
    }
}
