using DataLibrary.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace PlatformServer.Features.Account;

/// <summary>
/// Deletes accounts whose email was never confirmed, once their last link has long expired: at startup, then daily.
/// Registering again and resending the link set <c>UpdatedTime</c>, so an account counts from its latest link.
/// Most are addresses a script signed up; a person who still wants the account registers again.
/// </summary>
/// <remarks>
/// An unconfirmed account can't sign in, so it has no library, no share links and no token to outlive it. The
/// database deletes what hangs off the user (roles, tokens) with it.
/// </remarks>
public sealed class UnconfirmedAccountCleanup(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UnconfirmedAccountCleanup> logger) : BackgroundService
{
    /// <summary>How long an unconfirmed account is kept: longer than the 48 hours a confirmation link lasts.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), timeProvider);
        do
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Couldn't delete the unconfirmed accounts");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes the unconfirmed accounts older than <see cref="KeptFor"/>, and returns how many.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - KeptFor;

        var deleted = await context.Users
            .Where(u => !u.EmailConfirmed && (u.UpdatedTime ?? u.CreatedTime) < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} accounts left unconfirmed for {Days} days", deleted, KeptFor.TotalDays);
        }

        return deleted;
    }
}
