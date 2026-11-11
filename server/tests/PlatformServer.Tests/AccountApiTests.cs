using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PlatformServer.Features.Account;
using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.ApiAuth;

namespace PlatformServer.Tests;

// Changing the password through HTTP. One factory per test, since the endpoint shares the login rate limit,
// five a minute per factory.
public class AccountApiTests(SharedApiFactory shared) : IClassFixture<SharedApiFactory>
{
    private const string ChangePassword = "/api/v1/account/change-password";
    private const string Login = "/api/v1/account/login";
    private const string Profile = "/api/v1/account/profile";
    private const string Email = "user@example.com";
    private const string Password = "Password1";
    private const string NewPassword = "NewPassword2";

    [Fact]
    public async Task TheRightCurrentPassword_ChangesIt()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password });

        var response = await client.PostAsJsonAsync(ChangePassword, Change(Password, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, Password));
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, NewPassword));
        Assert.Equal(Email, Assert.Single(factory.Emails.PasswordChanges));
    }

    [Theory]
    [InlineData("WrongPassword1", NewPassword, "PasswordMismatch")]
    [InlineData(Password, "short1A", "PasswordTooShort")]
    [InlineData(Password, "nodigitsHere", "PasswordRequiresDigit")]
    public async Task AWrongCurrentPasswordOrABadNewOne_ChangesNothing(string current, string next, string code)
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password });

        var response = await client.PostAsJsonAsync(ChangePassword, Change(current, next));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
        Assert.Contains(code, problem.Errors.Keys);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, Password));
        Assert.Empty(factory.Emails.PasswordChanges);
    }

    // With the stamp checked on every request, the other session is out at once; this one got a new cookie.
    [Fact]
    public async Task ChangingIt_SignsOutTheOtherSessions_ButNotThisOne()
    {
        await using var factory = new AppApiFactory(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        await factory.CreateUserAsync(Email, Password);
        var thisSession = factory.CreateClient();
        var otherSession = factory.CreateClient();
        await thisSession.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password });
        await otherSession.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password });

        var response = await thisSession.PostAsJsonAsync(ChangePassword, Change(Password, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await thisSession.GetAsync(Profile)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherSession.GetAsync(Profile)).StatusCode);
    }

    [Fact]
    public async Task SignedOut_Returns401()
    {
        var factory = await shared.ResetAsync();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(ChangePassword, Change(Password, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static ChangePasswordRequest Change(string current, string next)
        => new() { CurrentPassword = current, NewPassword = next };

    private static async Task<HttpStatusCode> SignInStatusAsync(AppApiFactory factory, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = password });
        return response.StatusCode;
    }
}
