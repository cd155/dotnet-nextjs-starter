using DatabaseInitializer.Data;
using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using DataLibrary.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Unable to resolve the database connection string. " +
        "Provide ConnectionStrings__DefaultConnection via configuration or environment variables.");
}

// Determine whether to seed mock data:
//   - pass --seed as a CLI argument, or
//   - set the environment variable SEED_MOCK_DATA=true
bool seedMockData = args.Contains("--seed", StringComparer.OrdinalIgnoreCase)
    || string.Equals(configuration["SEED_MOCK_DATA"], "true", StringComparison.OrdinalIgnoreCase);

// Build a service provider with the Identity user/role managers for proper password hashing.
// AddIdentityCore (rather than AddIdentity) is all a seeder needs: no cookies, sign-in or
// token providers, so no ASP.NET Core web packages are required.
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSingleton<AuditTimestampInterceptor>();
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());

    // Match every other host's model so migrations apply and the OpenIddict managers work.
    options.UseOpenIddict();
});
services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// OpenIddict's managers, to seed the MCP scope and clients.
services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AppDbContext>());

using var serviceProvider = services.BuildServiceProvider();
using var scope = serviceProvider.CreateScope();

var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

try
{
    var pendingMigrations = context.Database.GetPendingMigrations().ToList();
    if (pendingMigrations.Count == 0)
    {
        Console.WriteLine("No pending migrations. Database is up to date.");
    }
    else
    {
        Console.WriteLine($"Pending migrations: {string.Join(", ", pendingMigrations)}");
    }

    Console.WriteLine("Applying migrations...");
    context.Database.Migrate();
    Console.WriteLine("Migrations applied. Database schema is up to date.");

    // Always seed Identity roles
    await SeedRolesAsync(roleManager);

    // Seed admin user from environment variables
    await SeedAdminUserAsync(userManager, configuration);

    // The MCP endpoint's OAuth scope and clients
    await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, configuration);

    if (seedMockData)
    {
        Console.WriteLine("Seeding mock data...");
        await Seeder.SeedDatabase(context, userManager);
        Console.WriteLine("Mock data seeded successfully.");
    }
    else
    {
        Console.WriteLine("Skipping mock data seeding. Pass --seed or set SEED_MOCK_DATA=true to seed mock data.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error during database initialization: {ex.Message}");
    Console.WriteLine(ex.ToString());
    Environment.Exit(1);
}

Console.WriteLine("Database initialization complete.");

static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
{
    string[] roles = [RoleConstants.User, RoleConstants.ContentManager, RoleConstants.Admin];

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
            Console.WriteLine($"Created role: {role}");
        }
    }
}

// Returns the admin user, or null when none is configured.
static async Task<ApplicationUser?> SeedAdminUserAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
{
    var adminEmail = configuration["ADMIN_EMAIL"];
    var adminPassword = configuration["ADMIN_PASSWORD"];

    if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
    {
        Console.WriteLine("ADMIN_EMAIL or ADMIN_PASSWORD not configured. Skipping admin user seeding.");
        return null;
    }

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser is null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            DisplayName = "Admin",
            EmailConfirmed = true,
            CreatedTime = DateTime.UtcNow
        };

        EnsureSucceeded(await userManager.CreateAsync(adminUser, adminPassword), $"create admin user {adminEmail}");
        Console.WriteLine($"Created admin user: {adminEmail}");
    }
    else
    {
        Console.WriteLine($"Admin user {adminEmail} already exists.");
    }

    // Checked on every run, so a retry recovers an admin created by a run that failed before
    // its roles were assigned.
    string[] adminRoles = [RoleConstants.Admin, RoleConstants.ContentManager];
    var missingRoles = adminRoles.Except(await userManager.GetRolesAsync(adminUser)).ToList();
    if (missingRoles.Count > 0)
    {
        EnsureSucceeded(await userManager.AddToRolesAsync(adminUser, missingRoles), $"assign roles to {adminEmail}");
        Console.WriteLine($"Assigned roles to {adminEmail}: {string.Join(", ", missingRoles)}");
    }

    return adminUser;
}

public partial class Program
{
    // Identity reports most failures (e.g. a password that breaks the policy) as a failed result
    // rather than an exception. Throw so the initializer exits non-zero instead of letting the
    // platform start without the roles or users it expects.
    internal static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}
