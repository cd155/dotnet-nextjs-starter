using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.McpCalls;

namespace PlatformServer.Tests;

// The note tools and prompt through /mcp, each signed in as its own user.
public class McpNoteToolsTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Password = "Password1";

    [Fact]
    public async Task CreateThenList_ReturnsTheNote_PinnedFirst()
    {
        var client = await ClientAsync("ann@example.com");

        await CallToolOkAsync(client, "create_note", new { title = "Later", body = "", isPinned = false });
        var pinned = await CallToolOkAsync(client, "create_note", new { title = "Now", body = "Do it", isPinned = true });
        var list = await CallToolOkAsync(client, "list_my_notes", new { });

        Assert.Equal(["Now", "Later"], list.EnumerateArray().Select(n => n.GetProperty("title").GetString()));
        Assert.Equal(pinned.GetProperty("id").GetInt32(), list[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UpdateNote_ReplacesTheValues()
    {
        var client = await ClientAsync("ann@example.com");
        var id = (await CallToolOkAsync(client, "create_note", new { title = "Draft", body = "v1", isPinned = false })).GetProperty("id").GetInt32();

        await CallToolOkAsync(client, "update_note", new { id, title = "Final", body = "v2", isPinned = true });
        var note = await CallToolOkAsync(client, "get_note", new { id });

        Assert.Equal("Final", note.GetProperty("title").GetString());
        Assert.Equal("v2", note.GetProperty("body").GetString());
        Assert.True(note.GetProperty("isPinned").GetBoolean());
    }

    // The repository's errors reach the model as the tool's message.
    [Fact]
    public async Task ABlankTitle_FailsWithTheReason()
    {
        var client = await ClientAsync("ann@example.com");

        var result = await CallToolAsync(client, "create_note", new { title = " ", body = "", isPinned = false });

        Assert.True(IsError(result));
        Assert.Contains("A note needs a title.", TextOf(result));
    }

    // Another user's note is "not found", the same as a missing one.
    [Fact]
    public async Task AnotherUsersNote_IsNotFound()
    {
        var factory = await shared.ResetAsync(() => McpTokens.CreateFactory());
        var ann = await ClientAsync(factory, "ann@example.com");
        var bob = await ClientAsync(factory, "bob@example.com");
        var id = (await CallToolOkAsync(ann, "create_note", new { title = "Ann's", body = "", isPinned = false })).GetProperty("id").GetInt32();

        var result = await CallToolAsync(bob, "get_note", new { id });

        Assert.True(IsError(result));
        Assert.Contains("was not found", TextOf(result));
        Assert.Empty((await CallToolOkAsync(bob, "list_my_notes", new { })).EnumerateArray());
    }

    [Fact]
    public async Task ThePrompt_HoldsTheUsersNotes()
    {
        var client = await ClientAsync("ann@example.com");
        await CallToolOkAsync(client, "create_note", new { title = "Groceries", body = "- [ ] milk", isPinned = true });

        var prompt = await CallAsync(client, "prompts/get", new { name = "summarize_my_notes", arguments = new { } });
        var text = prompt.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString();

        Assert.Contains("## Groceries (pinned)", text);
        Assert.Contains("- [ ] milk", text);
    }

    private async Task<HttpClient> ClientAsync(string email) =>
        await ClientAsync(await shared.ResetAsync(() => McpTokens.CreateFactory()), email);

    private static async Task<HttpClient> ClientAsync(AppApiFactory factory, string email)
    {
        await factory.CreateUserAsync(email, Password);
        return factory.CreateClient().WithToken(await McpTokens.GetAccessTokenAsync(factory, email, Password));
    }
}
