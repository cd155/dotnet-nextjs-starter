using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using PlatformServer.Features.Account;
using PlatformServer.Tests.Utilities;
using Xunit;
using static PlatformServer.Tests.Utilities.ApiAuth;

namespace PlatformServer.Tests;

// Asking for a reset link and resetting the password through HTTP. One factory per test, since sign-in has
// the login rate limit, and asking for a link shares the email one with registering.
public class PasswordResetApiTests
{
    private const string ForgotPassword = "/api/v1/account/forgot-password";
    private const string ResetPassword = "/api/v1/account/reset-password";
    private const string Login = "/api/v1/account/login";
    private const string Profile = "/api/v1/account/profile";
    private const string Email = "user@example.com";
    private const string Password = "Password1";
    private const string NewPassword = "NewPassword2";

    [Fact]
    public async Task ForgotPassword_AnswersTheSame_ButSendsOnlyToAConfirmedEmail()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync("confirmed@example.com", Password);
        await factory.CreateUserAsync("unconfirmed@example.com", Password, emailConfirmed: false);
        var client = factory.CreateClient();

        foreach (var email in new[] { "nobody@example.com", "confirmed@example.com", "unconfirmed@example.com" })
        {
            var response = await client.PostAsJsonAsync(ForgotPassword, new ForgotPasswordRequest { Email = email });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var (sentTo, link) = Assert.Single(factory.Emails.PasswordResets);
        Assert.Equal("confirmed@example.com", sentTo);
        Assert.Contains("/reset-password?userId=", link);
    }

    // Past three links an hour, an address gets no more, and the answer doesn't change. Another address still does.
    [Fact]
    public async Task ForgotPassword_PastTheLimitForOneEmail_SendsNothing()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        await factory.CreateUserAsync("other@example.com", Password);
        var client = factory.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(ForgotPassword, Forgot())).StatusCode);
        }

        await client.PostAsJsonAsync(ForgotPassword, new ForgotPasswordRequest { Email = "other@example.com" });

        Assert.Equal(3, factory.Emails.PasswordResets.Count(sent => sent.Email == Email));
        Assert.Single(factory.Emails.PasswordResets, sent => sent.Email == "other@example.com");
    }

    [Fact]
    public async Task ForgotPassword_PastTheEmailLimit_IsRateLimited()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(ForgotPassword, new ForgotPasswordRequest { Email = $"user{i}@example.com" });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(ForgotPassword, Forgot())).StatusCode);
    }

    [Fact]
    public async Task TheLink_SetsTheNewPassword_AndSaysSo()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var link = await RequestLinkAsync(factory, client);

        var response = await client.PostAsJsonAsync(ResetPassword, Reset(link, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, Password));
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, NewPassword));
        Assert.Equal(Email, Assert.Single(factory.Emails.PasswordChanges));
    }

    [Fact]
    public async Task TheLink_WorksOnce()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var link = await RequestLinkAsync(factory, client);
        await client.PostAsJsonAsync(ResetPassword, Reset(link, NewPassword));

        var response = await client.PostAsJsonAsync(ResetPassword, Reset(link, "ThirdPassword3"));

        await AssertInvalidLinkAsync(response);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, NewPassword));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ATamperedCode_OrAnUnknownUser_IsRefused(bool tamperCode, bool unknownUser)
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var request = Reset(await RequestLinkAsync(factory, client), NewPassword);
        request = request with
        {
            Code = tamperCode ? request.Code[..^4] + "AAAA" : request.Code,
            UserId = unknownUser ? Guid.NewGuid().ToString() : request.UserId
        };

        var response = await client.PostAsJsonAsync(ResetPassword, request);

        await AssertInvalidLinkAsync(response);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, Password));
    }

    // Identity checks the token before the password, so a weak password doesn't hide that the link expired.
    [Fact]
    public async Task AnExpiredLink_IsRefused_WhateverThePassword()
    {
        await using var factory = new AppApiFactory(builder => builder.ConfigureTestServices(services =>
            services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.Zero)));
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var link = await RequestLinkAsync(factory, client);

        await AssertInvalidLinkAsync(await client.PostAsJsonAsync(ResetPassword, Reset(link, NewPassword)));
        await AssertInvalidLinkAsync(await client.PostAsJsonAsync(ResetPassword, Reset(link, "weak")));
    }

    // A refused password leaves the link as it was, so the user tries again with a better one.
    [Fact]
    public async Task AWeakPassword_ChangesNothing_AndTheLinkStillWorks()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var link = await RequestLinkAsync(factory, client);

        var response = await client.PostAsJsonAsync(ResetPassword, Reset(link, "nodigitsHere"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
        Assert.Contains("PasswordRequiresDigit", problem.Errors.Keys);
        Assert.Empty(factory.Emails.PasswordChanges);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(ResetPassword, Reset(link, NewPassword))).StatusCode);
    }

    // With the stamp checked on every request, a session signed in before the reset is out at once.
    [Fact]
    public async Task AReset_SignsOutOtherSessions()
    {
        await using var factory = new AppApiFactory(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        await factory.CreateUserAsync(Email, Password);
        var session = factory.CreateClient();
        await session.PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = Password });
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(ResetPassword, Reset(await RequestLinkAsync(factory, client), NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync(Profile)).StatusCode);
    }

    [Fact]
    public async Task ALockedOutUser_CanSignIn_RightAfterAReset()
    {
        await using var factory = new AppApiFactory(builder => builder.ConfigureTestServices(services =>
            services.Configure<IdentityOptions>(options => options.Lockout.MaxFailedAccessAttempts = 2)));
        await factory.CreateUserAsync(Email, Password);
        await SignInStatusAsync(factory, "WrongPassword1");
        await SignInStatusAsync(factory, "WrongPassword1");
        Assert.Equal(HttpStatusCode.Locked, await SignInStatusAsync(factory, Password));
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(ResetPassword, Reset(await RequestLinkAsync(factory, client), NewPassword));

        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, NewPassword));
    }

    // The new password stands; only the notice is lost.
    [Fact]
    public async Task AFailedNotice_StillResets()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password);
        var client = factory.CreateClient();
        var link = await RequestLinkAsync(factory, client);
        factory.Emails.Fails = true;

        var response = await client.PostAsJsonAsync(ResetPassword, Reset(link, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, NewPassword));
    }

    private static ForgotPasswordRequest Forgot() => new() { Email = Email };

    /// <summary>Asks for a link for <see cref="Email"/> and returns it.</summary>
    private static async Task<string> RequestLinkAsync(AppApiFactory factory, HttpClient client)
    {
        await client.PostAsJsonAsync(ForgotPassword, Forgot());
        return factory.Emails.PasswordResets[^1].Link;
    }

    /// <summary>What the web's reset-password page posts: the link's user ID and code, and the new password.</summary>
    private static ResetPasswordRequest Reset(string link, string newPassword)
    {
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        return new ResetPasswordRequest { UserId = query["userId"]!, Code = query["code"]!, NewPassword = newPassword };
    }

    private static async Task AssertInvalidLinkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json))!;
        Assert.Equal("Invalid link", problem.Title);
    }

    private static async Task<HttpStatusCode> SignInStatusAsync(AppApiFactory factory, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = password });
        return response.StatusCode;
    }
}
