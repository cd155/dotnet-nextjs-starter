using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DataLibrary.DbContexts;

// Enables Add-Migration and allow SQL generation during the design time
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        var builder = new DbContextOptionsBuilder<AppDbContext>();

        // EF Core needs to understand the target database schema to generate correct SQL
        builder.UseNpgsql(connectionString);

        // Register OpenIddict's entity sets so generated migrations and the model snapshot include its tables.
        // Must match the runtime registration in every host.
        builder.UseOpenIddict();

        return new AppDbContext(builder.Options);
    }
}
