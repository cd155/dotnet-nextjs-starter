using DataLibrary.Entities.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformServer.Features.Email;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// The email settings. Missing ones never stop the app: outside Development each send fails, and in Development,
// which the tests run in, no host means the email is logged instead.
public class EmailSenderTests
{
    private static readonly Dictionary<string, string?> ResendSettings = new()
    {
        ["Email:Host"] = "smtp.example.com",
        ["Email:Username"] = "apikey",
        ["Email:Password"] = "re_test",
        ["Email:FromAddress"] = "noreply@example.com",
    };

    [Fact]
    public async Task MissingSettings_OutsideDevelopment_FailTheSend()
    {
        using var provider = BuildProvider(Environments.Production, []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<EmailSender>().SendAsync("user@example.com", "Hello", "<p>Hi</p>"));
        Assert.Contains("Email:Host", ex.Message);
        Assert.Contains("Email:FromAddress", ex.Message);
    }

    [Fact]
    public async Task NoHost_InDevelopment_LogsTheEmail_InsteadOfSending()
    {
        var logs = new LogCapture();
        using var provider = BuildProvider(Environments.Development, [], logs);

        await provider.GetRequiredService<EmailSender>().SendAsync("user@example.com", "Hello", "<p>Hi</p>");

        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Information && entry.Mentions("wasn't sent") && entry.Mentions("<p>Hi</p>"));
    }

    [Fact]
    public async Task AHost_WithoutASender_FailsTheSend_InDevelopmentToo()
    {
        using var provider = BuildProvider(Environments.Development, new() { ["Email:Host"] = "localhost" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<EmailSender>().SendAsync("user@example.com", "Hello", "<p>Hi</p>"));
        Assert.Contains("Email:FromAddress", ex.Message);
    }

    [Fact]
    public void ALogin_WithoutAPassword_IsMissingThePassword()
    {
        var options = new EmailOptions { Host = "smtp.example.com", Username = "apikey", FromAddress = "noreply@example.com" };

        Assert.Equal(["Email:Password"], options.MissingSettings());
    }

    [Fact]
    public void ResendSettings_AreComplete_WithStartTlsOn587()
    {
        using var provider = BuildProvider(Environments.Production, ResendSettings);

        var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.Empty(options.MissingSettings());
        Assert.Equal(587, options.Port);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, options.Security);
    }

    // The display name is the user's own text, so it can't add markup to the email.
    [Fact]
    public async Task TheConfirmationEmail_CarriesTheLink_AndEncodesTheName()
    {
        var logs = new LogCapture();
        using var provider = BuildProvider(Environments.Development, [], logs);
        var user = new ApplicationUser { Email = "user@example.com", DisplayName = "<b>Ann</b>" };

        await provider.GetRequiredService<EmailSender>().SendConfirmationLinkAsync(user, "user@example.com", "https://app.example.com/confirm-email?userId=1&amp;code=abc");

        Assert.Contains(logs.Entries, entry => entry.Mentions("href=\"https://app.example.com/confirm-email?userId=1&amp;code=abc\""));
        Assert.Contains(logs.Entries, entry => entry.Mentions("Hi &lt;b&gt;Ann&lt;/b&gt;,"));
    }

    [Fact]
    public async Task TheDeletionCodeEmail_CarriesTheCode_AndSaysWhatGoes()
    {
        var logs = new LogCapture();
        using var provider = BuildProvider(Environments.Development, [], logs);
        var user = new ApplicationUser { Email = "user@example.com" };

        await provider.GetRequiredService<EmailSender>().SendDeletionCodeAsync(user, "user@example.com", "123456");

        Assert.Contains(logs.Entries, entry => entry.Mentions(">123456</p>") && entry.Mentions("can't be undone"));
    }

    [Fact]
    public async Task TheAccountDeletedEmail_NamesTheApp()
    {
        var logs = new LogCapture();
        using var provider = BuildProvider(Environments.Development, [], logs);
        var user = new ApplicationUser { Email = "user@example.com" };

        await provider.GetRequiredService<EmailSender>().SendAccountDeletedAsync(user, "user@example.com");

        Assert.Contains(logs.Entries, entry => entry.Mentions("Your Starter account and its data have been deleted"));
    }

    private static ServiceProvider BuildProvider(string environmentName, Dictionary<string, string?> settings, LogCapture? logs = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environmentName });
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddEmail();
        return services.BuildServiceProvider();
    }
}
