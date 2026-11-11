using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// The CMS login form through HTTP, as Login.razor posts it: where each outcome redirects, and that a return URL
// is followed only when it stays on this site. One factory per test, since the login rate limit is per factory.
public partial class AuthFeatureTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string Email = "staff@example.com";
    private const string Password = "Password1";

    [Fact]
    public async Task TheRightPassword_RedirectsHome_SignedIn()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);

        var response = await PostLoginAsync(client, Email, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
    }

    [Fact]
    public async Task ALocalReturnUrl_IsFollowed()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);

        var response = await PostLoginAsync(client, Email, Password, returnUrl: "/notes?page=2");

        Assert.Equal("/notes?page=2", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com/")]
    public async Task AReturnUrlToAnotherSite_RedirectsHomeInstead(string returnUrl)
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);

        var response = await PostLoginAsync(client, Email, Password, returnUrl);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    // Login CSRF: a post without the form's token is sent back to the page, not answered with a bare 400.
    [Fact]
    public async Task NoAntiforgeryToken_RedirectsToExpired()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = Email,
            ["password"] = Password
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=expired", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AMissingEmail_RedirectsToEmpty()
    {
        var factory = await shared.ResetAsync();
        var client = CreateClient(factory);

        var response = await PostLoginAsync(client, email: null, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=empty", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AWrongPassword_RedirectsToInvalid_KeepingTheReturnUrl()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);

        var response = await PostLoginAsync(client, Email, "WrongPassword1", returnUrl: "/notes");

        Assert.Equal("/login?error=invalid&ReturnUrl=%2Fnotes", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_SignsOut_AndRedirectsToLogin()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = CreateClient(factory);
        await PostLoginAsync(client, Email, Password);

        var response = await client.GetAsync("/auth/logout");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
    }

    private static HttpClient CreateClient(AppApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Posts the form as the login page renders it: its antiforgery token, and the cookie that goes with it.
    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string? email, string password, string? returnUrl = null)
    {
        var token = TokenField().Match(await client.GetStringAsync("/login"));
        Assert.True(token.Success, "The login page has no antiforgery token.");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token.Groups[1].Value,
            ["password"] = password
        };
        if (email is not null)
        {
            form["email"] = email;
        }

        if (returnUrl is not null)
        {
            form["returnUrl"] = returnUrl;
        }

        return await client.PostAsync("/auth/login", new FormUrlEncodedContent(form));
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""")]
    private static partial Regex TokenField();
}
