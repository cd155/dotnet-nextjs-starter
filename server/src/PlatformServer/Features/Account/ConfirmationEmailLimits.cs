using System.Threading.RateLimiting;

namespace PlatformServer.Features.Account;

/// <summary>
/// How many confirmation emails go out, whoever asks and from wherever. A confirmation goes to an address nobody has
/// proved they own, so a script can aim it at anyone's inbox, and every send spends the email provider's
/// quota. Turnstile keeps scripts out; these are the backstop when one gets through.
/// </summary>
/// <remarks>The counts are kept in memory, so a restart starts them over.</remarks>
public sealed class ConfirmationEmailLimits : IDisposable
{
    /// <summary>Confirmation emails to one address a day: sign-up and a resend or two.</summary>
    public const int PerAddressPerDay = 3;

    /// <summary>Confirmation emails a day across the site, so password resets keep the rest of the quota.</summary>
    public const int PerDay = 50;

    private readonly PartitionedRateLimiter<string> _perAddress = PartitionedRateLimiter.Create<string, string>(email =>
        RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PerAddressPerDay,
            Window = TimeSpan.FromDays(1),
            QueueLimit = 0
        }));

    private readonly FixedWindowRateLimiter _daily = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = PerDay,
        Window = TimeSpan.FromDays(1),
        QueueLimit = 0
    });

    /// <summary>Takes a send from both limits; null when it may go, or the limit that's used up.</summary>
    /// <param name="normalizedEmail">The address as Identity normalizes it, so case doesn't make a new one.</param>
    public Refusal? TryTake(string normalizedEmail)
    {
        // Checked first, so a day past the cap doesn't spend the address's own sends, which would hold it back after
        // the cap resets. Taking from it comes last, so a refused address doesn't spend the day's.
        if (_daily.GetStatistics()?.CurrentAvailablePermits == 0)
        {
            return Refusal.Daily;
        }

        using var perAddress = _perAddress.AttemptAcquire(normalizedEmail);
        if (!perAddress.IsAcquired)
        {
            return Refusal.Address;
        }

        using var daily = _daily.AttemptAcquire();
        return daily.IsAcquired ? null : Refusal.Daily;
    }

    public void Dispose()
    {
        _perAddress.Dispose();
        _daily.Dispose();
    }

    public enum Refusal
    {
        /// <summary>The address has had its emails for the day.</summary>
        Address,

        /// <summary>The site has sent its confirmation emails for the day.</summary>
        Daily
    }
}
