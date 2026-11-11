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

// Registering, confirming the email and resending the link through HTTP, for the web app's cookie sign-in. One
// factory per test, since sign-in has the login rate limit, and registering and resending share the email one.
public class EmailConfirmationApiTests
{
    private const string Register = "/api/v1/account/register";
    private const string Login = "/api/v1/account/login";
    private const string ConfirmEmail = "/api/v1/account/confirm-email";
    private const string ResendConfirmation = "/api/v1/account/resend-confirmation";
    private const string Email = "new@example.com";
    private const string Password = "Password1";
    private const string OtherPassword = "OtherPassword2";

    [Fact]
    public async Task Registering_SendsTheLink_AndDoesntSignIn()
    {
        await using var factory = await RegistrationFactoryAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Registration(Password));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var (email, link) = Assert.Single(factory.Emails.Confirmations);
        Assert.Equal(Email, email);
        Assert.Contains("/confirm-email?userId=", link);
        Assert.Equal(HttpStatusCode.Forbidden, await SignInStatusAsync(factory, Password));
    }

    // Only the account's owner learns that it waits for confirmation.
    [Fact]
    public async Task AnUnconfirmedUser_WithTheWrongPassword_GetsInvalidCredentials()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password, emailConfirmed: false);

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, OtherPassword));
    }

    // Wrong passwords count towards the lockout, as they do for a confirmed account.
    [Fact]
    public async Task AnUnconfirmedUser_IsLockedOut_AfterWrongPasswords()
    {
        await using var factory = new AppApiFactory(builder => builder.ConfigureTestServices(services =>
            services.Configure<IdentityOptions>(options => options.Lockout.MaxFailedAccessAttempts = 2)));
        await factory.CreateUserAsync(Email, Password, emailConfirmed: false);

        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, OtherPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, OtherPassword));
        Assert.Equal(HttpStatusCode.Locked, await SignInStatusAsync(factory, Password));
    }

    [Fact]
    public async Task TheLink_ConfirmsTheEmail_ThenSignInWorks()
    {
        await using var factory = await RegistrationFactoryAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Register, Registration(Password));

        var response = await client.PostAsJsonAsync(ConfirmEmail, LinkRequest(factory.Emails.Confirmations[0].Link));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, Password));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ATamperedCode_OrAnUnknownUser_IsRefused(bool tamperCode, bool unknownUser)
    {
        await using var factory = await RegistrationFactoryAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Register, Registration(Password));
        var request = LinkRequest(factory.Emails.Confirmations[0].Link);
        request = request with
        {
            Code = tamperCode ? "x" + request.Code[1..] : request.Code,
            UserId = unknownUser ? Guid.NewGuid().ToString() : request.UserId
        };

        var response = await client.PostAsJsonAsync(ConfirmEmail, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await SignInStatusAsync(factory, Password));
    }

    [Fact]
    public async Task AnExpiredLink_IsRefused()
    {
        await using var factory = await RegistrationFactoryAsync(builder => builder.ConfigureTestServices(services =>
            services.Configure<EmailConfirmationTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.Zero)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Register, Registration(Password));

        var response = await client.PostAsJsonAsync(ConfirmEmail, LinkRequest(factory.Emails.Confirmations[0].Link));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json))!;
        Assert.Equal("Invalid link", problem.Title);
    }

    // A second click, or the web page posting twice, still ends at "confirmed".
    [Fact]
    public async Task AConfirmedUser_GetsNoContent_WhateverTheCode()
    {
        await using var factory = new AppApiFactory();
        var userId = await factory.CreateUserAsync(Email, Password);

        var response = await factory.CreateClient().PostAsJsonAsync(ConfirmEmail, new ConfirmEmailRequest { UserId = userId, Code = "used" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Whoever registered the address first keeps no way in once its owner confirms it.
    [Fact]
    public async Task RegisteringAgain_Unconfirmed_TakesTheNewPassword_AndVoidsTheOldLink()
    {
        await using var factory = await RegistrationFactoryAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Register, Registration(Password));

        var response = await client.PostAsJsonAsync(Register, Registration(OtherPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, factory.Emails.Confirmations.Count);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(ConfirmEmail, LinkRequest(factory.Emails.Confirmations[0].Link))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync(ConfirmEmail, LinkRequest(factory.Emails.Confirmations[1].Link))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(factory, Password));
        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(factory, OtherPassword));
        // The new password reset the unconfirmed account's, but its address isn't one to send to.
        Assert.Empty(factory.Emails.PasswordChanges);
    }

    [Fact]
    public async Task RegisteringAgain_Unconfirmed_WithABadPassword_ChangesNothing()
    {
        await using var factory = await RegistrationFactoryAsync();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Register, Registration(Password));

        var response = await client.PostAsJsonAsync(Register, Registration("short1A"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(factory.Emails.Confirmations);
        Assert.Equal(HttpStatusCode.Forbidden, await SignInStatusAsync(factory, Password));
    }

    [Fact]
    public async Task RegisteringAgain_Confirmed_IsTaken()
    {
        await using var factory = await RegistrationFactoryAsync();
        await factory.CreateUserAsync(Email, Password);

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Registration(OtherPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
        Assert.Contains("DuplicateUserName", problem.Errors.Keys);
        Assert.Empty(factory.Emails.Confirmations);
    }

    [Fact]
    public async Task Resend_AnswersTheSame_ButSendsOnlyToAnUnconfirmedEmail()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync("confirmed@example.com", Password);
        await factory.CreateUserAsync("unconfirmed@example.com", Password, emailConfirmed: false);
        var client = factory.CreateClient();

        foreach (var email in new[] { "nobody@example.com", "confirmed@example.com", "unconfirmed@example.com" })
        {
            var response = await client.PostAsJsonAsync(ResendConfirmation, new ResendConfirmationRequest { Email = email });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var (sentTo, _) = Assert.Single(factory.Emails.Confirmations);
        Assert.Equal("unconfirmed@example.com", sentTo);
    }

    [Fact]
    public async Task Resend_PastTheEmailLimit_IsRateLimited()
    {
        await using var factory = new AppApiFactory();
        await factory.CreateUserAsync(Email, Password, emailConfirmed: false);
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(ResendConfirmation, Resend())).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(ResendConfirmation, Resend())).StatusCode);
        // The address's own limit stopped the emails well before the IP's.
        Assert.Equal(ConfirmationEmailLimits.PerAddressPerDay, factory.Emails.Confirmations.Count);
    }

    // The account stays; the user asks for another link.
    [Fact]
    public async Task AFailedSend_StillRegisters()
    {
        await using var factory = await RegistrationFactoryAsync();
        factory.Emails.Fails = true;

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Registration(Password));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await SignInStatusAsync(factory, Password));
    }

    private static async Task<AppApiFactory> RegistrationFactoryAsync(Action<IWebHostBuilder>? configure = null)
    {
        var factory = new AppApiFactory(configure);
        await factory.PrepareRegistrationAsync();
        return factory;
    }

    private static RegisterRequest Registration(string password)
        => new() { Email = Email, Password = password, DisplayName = "New User" };

    private static ResendConfirmationRequest Resend() => new() { Email = Email };

    /// <summary>What the web's confirm-email page posts: the link's user ID and code.</summary>
    private static ConfirmEmailRequest LinkRequest(string link)
    {
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        return new ConfirmEmailRequest { UserId = query["userId"]!, Code = query["code"]! };
    }

    private static async Task<HttpStatusCode> SignInStatusAsync(AppApiFactory factory, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Login, new LoginRequest { Email = Email, Password = password });
        return response.StatusCode;
    }
}
