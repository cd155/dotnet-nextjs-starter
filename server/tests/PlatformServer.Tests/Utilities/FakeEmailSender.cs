using System.Net;
using DataLibrary.Entities.Identity;
using PlatformServer.Features.Email;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// Stands in for the SMTP sender in <see cref="AppApiFactory"/>: records each email instead of sending it, or
/// throws when <see cref="Fails"/> is set.
/// </summary>
public sealed class FakeEmailSender : IAccountEmailSender
{
    private readonly List<(string Email, string Link)> _confirmations = [];
    private readonly List<(string Email, string Link)> _passwordResets = [];
    private readonly List<string> _passwordChanges = [];
    private readonly List<(string Email, string Code)> _deletionCodes = [];
    private readonly List<string> _accountDeletions = [];

    public bool Fails { get; set; }

    /// <summary>The confirmation emails, oldest first, with the link HTML-decoded as a browser would read it.</summary>
    public IReadOnlyList<(string Email, string Link)> Confirmations => Snapshot(_confirmations);

    /// <summary>The password reset emails, oldest first, with the link HTML-decoded.</summary>
    public IReadOnlyList<(string Email, string Link)> PasswordResets => Snapshot(_passwordResets);

    /// <summary>Where each password-changed email went, oldest first.</summary>
    public IReadOnlyList<string> PasswordChanges => Snapshot(_passwordChanges);

    /// <summary>The account deletion codes, oldest first.</summary>
    public IReadOnlyList<(string Email, string Code)> DeletionCodes => Snapshot(_deletionCodes);

    /// <summary>Where each account-deleted email went, oldest first.</summary>
    public IReadOnlyList<string> AccountDeletions => Snapshot(_accountDeletions);

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        => Record(_confirmations, (email, WebUtility.HtmlDecode(confirmationLink)));

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        => Record(_passwordResets, (email, WebUtility.HtmlDecode(resetLink)));

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;

    public Task SendPasswordChangedAsync(ApplicationUser user, string email, string resetLink)
        => Record(_passwordChanges, email);

    public Task SendDeletionCodeAsync(ApplicationUser user, string email, string code)
        => Record(_deletionCodes, (email, code));

    public Task SendAccountDeletedAsync(ApplicationUser user, string email)
        => Record(_accountDeletions, email);

    /// <summary>Forgets every email and stops failing, as a new sender would be.</summary>
    public void Reset()
    {
        Fails = false;
        foreach (var sent in new System.Collections.IList[] { _confirmations, _passwordResets, _passwordChanges, _deletionCodes, _accountDeletions })
        {
            lock (sent)
            {
                sent.Clear();
            }
        }
    }

    private Task Record<T>(List<T> sent, T email)
    {
        if (Fails)
        {
            throw new InvalidOperationException("The fake sender was told to fail.");
        }

        lock (sent)
        {
            sent.Add(email);
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<T> Snapshot<T>(List<T> sent)
    {
        lock (sent)
        {
            return [.. sent];
        }
    }
}
