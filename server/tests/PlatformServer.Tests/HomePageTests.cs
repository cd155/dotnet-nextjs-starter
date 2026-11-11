using System.Net;
using System.Net.Http.Json;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using PlatformServer.Features.Account;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// The CMS's home page through HTTP: the counts are for admins only; anyone else signed in gets a welcome.
// One factory per test, since the login rate limit is per factory.
public class HomePageTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Email = "user@example.com";
    private const string Password = "Password1";

    [Fact]
    public async Task ForAnAdmin_ShowsTheCountsAndHealth()
    {
        var factory = await shared.ResetAsync();
        await factory.AddToRoleAsync(await factory.CreateUserAsync(Email, Password), RoleConstants.Admin);
        var client = await SignedInClientAsync(factory);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1 users", html);
        Assert.Contains("0 notes", html);
        Assert.Contains("Healthy", html);
    }

    [Fact]
    public async Task ForAContentManager_ShowsAWelcomeWithoutTheCounts()
    {
        var factory = await shared.ResetAsync();
        await factory.AddToRoleAsync(await factory.CreateUserAsync(Email, Password), RoleConstants.ContentManager);
        var client = await SignedInClientAsync(factory);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Welcome to the CMS", html);
        Assert.DoesNotContain(" notes", html);
        Assert.DoesNotContain("Healthy", html);
    }

    [Fact]
    public async Task SignedOut_RedirectsToLogin()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.AbsolutePath);
    }

    // Signed in with the cookie, as the CMS is.
    private static async Task<HttpClient> SignedInClientAsync(AppApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/v1/account/login", new LoginRequest { Email = Email, Password = Password });
        login.EnsureSuccessStatusCode();
        return client;
    }
}
