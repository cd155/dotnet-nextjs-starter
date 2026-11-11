using DataLibrary.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DataLibrary.Interceptors;

/// <summary>
/// Automatically sets <see cref="BaseEntity.CreatedTime"/> on insert and
/// <see cref="BaseEntity.UpdatedTime"/> on update so callers do not need to set
/// these fields manually.
/// </summary>
public sealed class AuditTimestampInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        SetTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void SetTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedTime == default)
            {
                entry.Entity.CreatedTime = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedTime = now;
            }
        }
    }
}
