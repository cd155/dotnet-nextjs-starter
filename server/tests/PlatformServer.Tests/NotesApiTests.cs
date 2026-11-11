using System.Net;
using System.Net.Http.Json;
using PlatformServer.Features.Notes;
using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.ApiAuth;

namespace PlatformServer.Tests;

// The notes API through HTTP, signed in with the cookie as the web app's server is.
public class NotesApiTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Notes = "/api/v1/me/notes";
    private const string Password = "Password1";

    [Fact]
    public async Task CreateReadUpdateDelete()
    {
        var client = await ClientAsync("ann@example.com");

        var create = await client.PostAsJsonAsync(Notes, new { title = "  First  ", body = "Hello" });
        var created = (await create.Content.ReadFromJsonAsync<Note>(Json))!;
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal($"{Notes}/{created.Id}", create.Headers.Location?.OriginalString);
        Assert.Equal("First", created.Title);

        var update = await client.PutAsJsonAsync($"{Notes}/{created.Id}", new { title = "Renamed", body = "Hi", isPinned = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var read = (await client.GetFromJsonAsync<Note>($"{Notes}/{created.Id}", Json))!;
        Assert.Equal(("Renamed", "Hi", true), (read.Title, read.Body, read.IsPinned));
        Assert.NotNull(read.UpdatedTime);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Notes}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Notes}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task TheList_IsPinnedFirst_ThenTheLatest()
    {
        var client = await ClientAsync("ann@example.com");
        await client.PostAsJsonAsync(Notes, new { title = "Old" });
        await client.PostAsJsonAsync(Notes, new { title = "Pinned", isPinned = true });
        await client.PostAsJsonAsync(Notes, new { title = "New" });

        var list = (await client.GetFromJsonAsync<List<Note>>(Notes, Json))!;

        Assert.Equal(["Pinned", "New", "Old"], list.Select(n => n.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTitle_Is400WithTheField(string title)
    {
        var client = await ClientAsync("ann@example.com");

        var response = await client.PostAsJsonAsync(Notes, new { title });
        var problem = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", problem, StringComparison.OrdinalIgnoreCase);
    }

    // Someone else's note is "not found", the same as a missing one, so its id tells nothing.
    [Fact]
    public async Task AnotherUsersNote_Is404()
    {
        var factory = await shared.ResetAsync();
        var ann = await ClientAsync(factory, "ann@example.com");
        var bob = await ClientAsync(factory, "bob@example.com");
        var note = (await (await ann.PostAsJsonAsync(Notes, new { title = "Ann's" })).Content.ReadFromJsonAsync<Note>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"{Notes}/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"{Notes}/{note.Id}", new { title = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"{Notes}/{note.Id}")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<List<Note>>(Notes, Json))!);
    }

    [Fact]
    public async Task SignedOut_Is401()
    {
        var factory = await shared.ResetAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Notes)).StatusCode);
    }

    private async Task<HttpClient> ClientAsync(string email) => await ClientAsync(await shared.ResetAsync(), email);

    private static async Task<HttpClient> ClientAsync(AppApiFactory factory, string email)
    {
        await factory.CreateUserAsync(email, Password);
        var client = factory.CreateClient();
        await SignInAsync(client, email, Password);
        return client;
    }
}
