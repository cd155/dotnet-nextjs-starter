using DataLibrary.Entities.Identity;
using DataLibrary.Entities.Notes;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharedLibrary;

namespace DataLibrary.DbContexts;

/// <summary>
/// The app's one DbContext: Identity's tables, OpenIddict's (registered by <c>options.UseOpenIddict()</c> in every
/// host and the design-time factory), and the features' own.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<NoteEntity> Notes { get; set; }

    public AppDbContext() { }
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NoteEntity>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(NoteLimits.Title);
            b.Property(x => x.Body).HasMaxLength(NoteLimits.Body);
            b.HasIndex(x => x.UserId);

            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Ensure all DateTime values read from the database are tagged as UTC.
        // Some database providers (including Npgsql) can materialize DateTime values
        // with DateTimeKind.Unspecified; without this converter they may serialize
        // without a 'Z' suffix in JSON responses.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(utcNullableConverter);
                }
            }
        }
    }
}
