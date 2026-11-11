using System.Net;
using System.Net.Http.Json;
using DataLibrary.DbContexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlatformServer.Features.Account;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// What keeps a script from using sign-up to send email: Turnstile, the confirmation email limits, and the
// cleanup of accounts nobody confirmed.
public class SignUpAbuseTests
{
    private const string Register = "/api/v1/account/register";
    private const string ResendConfirmation = "/api/v1/account/resend-confirmation";
    private const string Email = "new@example.com";
    private const string Password = "Password1";

    [Theory]
    [InlineData(Register)]
    [InlineData(ResendConfirmation)]
    public async Task WithoutAToken_NothingIsSent_AndCloudflareIsntAsked(string path)
    {
        var cloudflare = new StubCloudflare(success: true, hostname: "app.example.com");
        await using var factory = await TurnstileFactoryAsync(cloudflare);
        await factory.CreateUserAsync("waiting@example.com", Password, emailConfirmed: false);

        var response = await factory.CreateClient().PostAsJsonAsync(path, Body(path, token: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("turnstileToken", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Emails.Confirmations);
        Assert.Equal(0, cloudflare.Calls);
    }

    [Fact]
    public async Task ARefusedToken_DoesntRegister()
    {
        await using var factory = await TurnstileFactoryAsync(new StubCloudflare(success: false, hostname: null));

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Body(Register, "token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Emails.Confirmations);
        Assert.False(await UserExistsAsync(factory, Email));
    }

    // One Turnstile widget can serve several sites, so a token made for another one must not work here.
    [Fact]
    public async Task ATokenForAnotherHost_IsRefused()
    {
        await using var factory = await TurnstileFactoryAsync(new StubCloudflare(success: true, hostname: "other.example.com"));

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Body(Register, "token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await UserExistsAsync(factory, Email));
    }

    [Fact]
    public async Task APassingToken_Registers_AndSendsTheTokenAndSecretToCloudflare()
    {
        var cloudflare = new StubCloudflare(success: true, hostname: "app.example.com");
        await using var factory = await TurnstileFactoryAsync(cloudflare);

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Body(Register, "the-token"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(factory.Emails.Confirmations);
        Assert.Contains("secret=test-secret", cloudflare.LastBody);
        Assert.Contains("response=the-token", cloudflare.LastBody);
    }

    [Fact]
    public async Task OneAddress_GetsAtMostItsConfirmationEmailsForTheDay()
    {
        await using var factory = new AppApiFactory();
        await factory.PrepareRegistrationAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Register, Body(Register, null))).StatusCode);
        for (var i = 0; i < ConfirmationEmailLimits.PerAddressPerDay + 2; i++)
        {
            // Registering again answers the same, so nobody learns the address is past its limit.
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Register, Body(Register, null))).StatusCode);
        }

        Assert.Equal(ConfirmationEmailLimits.PerAddressPerDay, factory.Emails.Confirmations.Count);
    }

    [Fact]
    public void TheLimits_StopAnAddress_ThenTheDay()
    {
        using var limits = new ConfirmationEmailLimits();

        for (var i = 0; i < ConfirmationEmailLimits.PerAddressPerDay; i++)
        {
            Assert.Null(limits.TryTake("A@EXAMPLE.COM"));
        }

        Assert.Equal(ConfirmationEmailLimits.Refusal.Address, limits.TryTake("A@EXAMPLE.COM"));

        for (var i = ConfirmationEmailLimits.PerAddressPerDay; i < ConfirmationEmailLimits.PerDay; i++)
        {
            Assert.Null(limits.TryTake($"{i}@EXAMPLE.COM"));
        }

        Assert.Equal(ConfirmationEmailLimits.Refusal.Daily, limits.TryTake("LAST@EXAMPLE.COM"));
    }

    // Refused by the day's cap, an address keeps its own sends for when the cap resets.
    [Fact]
    public void TheDailyCap_DoesntCountTowardAnAddress()
    {
        using var limits = new ConfirmationEmailLimits();
        for (var i = 0; i < ConfirmationEmailLimits.PerDay; i++)
        {
            Assert.Null(limits.TryTake($"{i}@EXAMPLE.COM"));
        }

        for (var i = 0; i < ConfirmationEmailLimits.PerAddressPerDay + 1; i++)
        {
            Assert.Equal(ConfirmationEmailLimits.Refusal.Daily, limits.TryTake("WAITING@EXAMPLE.COM"));
        }
    }

    [Fact]
    public async Task WithoutTheWebAppsUrl_NoTokenPasses()
    {
        await using var factory = new AppApiFactory(builder =>
        {
            builder.UseSetting("Turnstile:SecretKey", "test-secret");
            builder.UseSetting("WebApp:BaseUrl", "");
            builder.ConfigureTestServices(services => services.AddHttpClient<TurnstileVerifier>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubCloudflare(success: true, hostname: "other.example.com")));
        });
        await factory.PrepareRegistrationAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Body(Register, "token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheCleanup_DeletesOnlyOldUnconfirmedAccounts()
    {
        await using var factory = new AppApiFactory();
        var old = DateTime.UtcNow - UnconfirmedAccountCleanup.KeptFor - TimeSpan.FromHours(1);
        await factory.CreateUserAsync("old-unconfirmed@example.com", Password, emailConfirmed: false, createdTime: old);
        await factory.CreateUserAsync("new-unconfirmed@example.com", Password, emailConfirmed: false);
        await factory.CreateUserAsync("old-confirmed@example.com", Password, createdTime: old);
        // An old account that asked for a new link counts from that link.
        await factory.CreateUserAsync("resent@example.com", Password, emailConfirmed: false, createdTime: old);
        await factory.CreateClient().PostAsJsonAsync(ResendConfirmation, new ResendConfirmationRequest { Email = "resent@example.com" });

        var cleanup = ActivatorUtilities.CreateInstance<UnconfirmedAccountCleanup>(factory.Services);
        var deleted = await cleanup.DeleteExpiredAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.False(await UserExistsAsync(factory, "old-unconfirmed@example.com"));
        Assert.True(await UserExistsAsync(factory, "new-unconfirmed@example.com"));
        Assert.True(await UserExistsAsync(factory, "old-confirmed@example.com"));
        Assert.True(await UserExistsAsync(factory, "resent@example.com"));
    }

    [Fact]
    public async Task WhenCloudflareCantBeReached_NoTokenPasses()
    {
        await using var factory = await TurnstileFactoryAsync(new StubCloudflare(success: true, hostname: "app.example.com", unreachable: true));

        var response = await factory.CreateClient().PostAsJsonAsync(Register, Body(Register, "token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Emails.Confirmations);
        Assert.False(await UserExistsAsync(factory, Email));
    }

    [Fact]
    public async Task WithoutASecretKeyOutsideDevelopment_NoTokenPasses()
    {
        var cloudflare = new StubCloudflare(success: true, hostname: "app.example.com");
        var verifier = new TurnstileVerifier(
            new HttpClient(cloudflare),
            Options.Create(new TurnstileOptions()),
            Options.Create(new WebAppOptions { BaseUrl = "https://app.example.com" }),
            new HostingEnvironment { EnvironmentName = Environments.Production },
            NullLogger<TurnstileVerifier>.Instance);

        Assert.False(await verifier.VerifyAsync("token", remoteIp: null, CancellationToken.None));
        Assert.Equal(0, cloudflare.Calls);
    }

    // A secret key turns the check on, and Cloudflare is the stub.
    private static async Task<AppApiFactory> TurnstileFactoryAsync(StubCloudflare cloudflare)
    {
        var factory = new AppApiFactory(builder =>
        {
            builder.UseSetting("Turnstile:SecretKey", "test-secret");
            builder.UseSetting("WebApp:BaseUrl", "https://app.example.com");
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<TurnstileVerifier>().ConfigurePrimaryHttpMessageHandler(() => cloudflare));
        });
        await factory.PrepareRegistrationAsync();
        return factory;
    }

    private static object Body(string path, string? token) => path == Register
        ? new RegisterRequest { Email = Email, Password = Password, TurnstileToken = token }
        : new ResendConfirmationRequest { Email = "waiting@example.com", TurnstileToken = token };

    private static async Task<bool> UserExistsAsync(AppApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AnyAsync(u => u.Email == email);
    }

    private sealed class StubCloudflare(bool success, string? hostname, bool unreachable = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (unreachable)
            {
                throw new HttpRequestException("Cloudflare can't be reached");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Dictionary<string, object?>
                {
                    ["success"] = success,
                    ["hostname"] = hostname,
                    ["error-codes"] = success ? Array.Empty<string>() : new[] { "invalid-input-response" }
                })
            };
        }
    }
}
