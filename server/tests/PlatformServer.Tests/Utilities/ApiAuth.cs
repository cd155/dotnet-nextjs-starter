using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlatformServer.Features.Account;

namespace PlatformServer.Tests.Utilities;

/// <summary>Signing in through HTTP, for tests that go through <see cref="AppApiFactory"/>.</summary>
public static class ApiAuth
{
    /// <summary>The API's JSON: camelCase, with enums by name.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Signs the client in with the cookie, as the web app's server does.</summary>
    public static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/account/login", new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
    }
}
