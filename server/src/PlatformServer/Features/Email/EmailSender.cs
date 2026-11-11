using System.Text.Encodings.Web;
using DataLibrary.Entities.Identity;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace PlatformServer.Features.Email;

/// <summary>
/// The SMTP server email goes through, from the <c>Email</c> section: your provider in production
/// (<c>docker-compose.yml</c>), Mailpit locally (<c>docker-compose.local.yml</c>).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The SMTP host. Left blank in Development, emails are logged instead of sent.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>
    /// TLS on the connection. STARTTLS by default, so the connection is never sent in the clear. Mailpit
    /// speaks plain SMTP, so the local stack sets <c>None</c>.
    /// </summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>The SMTP login. Left blank, the sender doesn't sign in, as Mailpit needs no login.</summary>
    public string? Username { get; set; }

    /// <summary>The SMTP password; for most providers, an API key.</summary>
    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    /// <summary>The sender's name, which the emails also use as the app's name.</summary>
    public string FromName { get; set; } = "Starter";

    /// <summary>
    /// The settings sending needs but doesn't have, as configuration keys. A password is needed only with a
    /// login.
    /// </summary>
    public IReadOnlyList<string> MissingSettings()
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(Host))
        {
            missing.Add("Email:Host");
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            missing.Add("Email:FromAddress");
        }

        if (!string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password))
        {
            missing.Add("Email:Password");
        }

        return missing;
    }

    /// <summary>Whether emails are logged instead of sent: only in Development, with no host.</summary>
    public bool LogsInsteadOfSending(IHostEnvironment environment) =>
        environment.IsDevelopment() && string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// Sends Identity's emails, and any other, over SMTP. The same code reaches Resend and Mailpit; only
/// <see cref="EmailOptions"/> differ. Missing settings don't stop the app, which runs without email: each send
/// fails instead, and the app logs an error at startup (<c>Program.LogStartupSettings</c>).
/// </summary>
public sealed class EmailSender(IOptions<EmailOptions> options, IHostEnvironment environment, ILogger<EmailSender> logger)
    : IAccountEmailSender
{
    // MailKit waits 2 minutes by default, which would hold the request that sends. The timeout applies to each
    // step (connect, sign-in, send), not to the whole send.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string SmallPrint = "font-size: 13px; color: #6b7280;";

    /// <summary>The app's name in the emails' text: the sender's name, HTML-encoded.</summary>
    private string AppName => HtmlEncoder.Default.Encode(options.Value.FromName);

    // The links and the code come HTML-encoded from the caller.
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(email, $"Confirm your email for {options.Value.FromName}", Message(user, $"""
            <p>Thanks for signing up for {AppName}. Confirm your email address to start using your account.</p>
            {LinkButton(confirmationLink, "Confirm email")}
            <p style="{SmallPrint}">The link expires in 48 hours. If you didn't sign up for {AppName}, you can ignore this email.</p>
            """));

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, $"Reset your {options.Value.FromName} password", Message(user, $"""
            <p>We got a request to reset the password for your {AppName} account.</p>
            {LinkButton(resetLink, "Reset password")}
            <p style="{SmallPrint}">The link expires in 1 hour. If you didn't ask to reset your password, you can ignore this email; your password stays the same.</p>
            """));

    // Identity's own default (DefaultMessageEmailSender). Nothing sends a reset code.
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(email, "Reset your password", $"Please reset your password using the following code: {resetCode}");

    public Task SendPasswordChangedAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(email, $"Your {options.Value.FromName} password was changed", Message(user, $"""
            <p>The password for your {AppName} account was just changed. If you changed it, there's nothing more to do.</p>
            <p>If you didn't, <a href="{resetLink}" style="color: #007BFF;">reset your password</a> now to keep your account safe.</p>
            """));

    // The code is digits only, so it needs no encoding.
    public Task SendDeletionCodeAsync(ApplicationUser user, string email, string code) =>
        SendAsync(email, $"Your {options.Value.FromName} account deletion code", Message(user, $"""
            <p>Use this code to confirm that you want to delete your {AppName} account:</p>
            <p style="margin: 24px 0; font-size: 28px; font-weight: 700; letter-spacing: 6px;">{code}</p>
            <p>Deleting your account removes everything in it, such as your notes. It can't be undone.</p>
            <p style="{SmallPrint}">The code expires in a few minutes. If you didn't ask to delete your account, don't share this code, and change your password: someone may know it.</p>
            """));

    public Task SendAccountDeletedAsync(ApplicationUser user, string email) =>
        SendAsync(email, $"Your {options.Value.FromName} account was deleted", Message(user, $"""
            <p>Your {AppName} account and its data have been deleted, as you asked. Thanks for trying {AppName}.</p>
            """));

    /// <summary>A message's frame: the greeting, by the user's name when they have one, then the body.</summary>
    private static string Message(ApplicationUser user, string body)
    {
        var greeting = string.IsNullOrWhiteSpace(user.DisplayName)
            ? "Hi,"
            : $"Hi {HtmlEncoder.Default.Encode(user.DisplayName)},";

        return $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; font-size: 15px; line-height: 1.5; color: #1f2937; max-width: 480px;">
              <p>{greeting}</p>
              {body}
            </div>
            """;
    }

    /// <summary>The link as a button, and as plain text for mail apps that don't show the button.</summary>
    private static string LinkButton(string link, string label) => $"""
        <p style="margin: 24px 0;">
          <a href="{link}" style="display: inline-block; padding: 12px 24px; border-radius: 12px; background: #007BFF; color: #ffffff; font-weight: 600; text-decoration: none;">{label}</a>
        </p>
        <p style="{SmallPrint}">If the button doesn't work, open this link:<br><a href="{link}" style="color: #007BFF; word-break: break-all;">{link}</a></p>
        """;

    /// <summary>
    /// Sends one HTML message. A failure, missing settings included, throws, so it reaches the logs and Sentry,
    /// and the caller decides what the user sees. Only Development, with no host, logs the message instead.
    /// </summary>
    public async Task SendAsync(string email, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (settings.LogsInsteadOfSending(environment))
        {
            // The body too, so a confirmation or reset link can still be followed locally.
            logger.LogInformation("Email isn't configured, so \"{Subject}\" to {Email} wasn't sent: {Body}", subject, email, htmlBody);
            return;
        }

        var missing = settings.MissingSettings();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Email isn't configured: {string.Join(", ", missing)} not set.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress!));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(settings.Host!, settings.Port, settings.Security, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(new SendRequest(message), cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
