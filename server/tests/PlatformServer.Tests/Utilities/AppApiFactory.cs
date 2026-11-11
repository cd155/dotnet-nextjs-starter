using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using DataLibrary.Interceptors;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlatformServer.Features.Account;
using PlatformServer.Features.Email;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// The whole API in memory, over an in-memory SQLite database, for tests that go through HTTP:
/// authentication schemes, rate limits, model binding.
/// </summary>
/// <remarks>
/// Starting the app takes about a second, so a test class shares one factory through <see cref="SharedApiFactory"/>,
/// which calls <see cref="ResetAsync"/> before each test. A test that needs its own host settings creates its own, and
/// so does a class whose tests count on state a reset can't clear, such as the per-address email limits.
/// Every request comes from <see cref="ClientIp"/>, which the rate limiters partition by: the <c>login</c> policy
/// allows five sign-ins a minute from it, and a reset moves to a new address.
/// </remarks>
public sealed class AppApiFactory : WebApplicationFactory<Program>
{
    private static int s_lastClientIp;

    private SqliteConnection _connection = OpenConnection();
    private readonly Action<IWebHostBuilder>? _configure;

    /// <summary>The client address of every request, new for each factory and after each reset.</summary>
    public IPAddress ClientIp { get; private set; } = NextClientIp();

    /// <summary>Takes the emails the API sends, in place of SMTP.</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <param name="configure">Applied last, for a test's own settings or services.</param>
    public AppApiFactory(Action<IWebHostBuilder>? configure = null)
    {
        _configure = configure;
    }

    /// <summary>
    /// Brings the running app back to how a new factory starts: an empty database (with the schema), no emails, an
    /// empty memory cache and a new <see cref="ClientIp"/>, so the rate limits start over.
    /// </summary>
    public async Task ResetAsync()
    {
        // AddDbContext's options are scoped by default, so each new scope runs the options lambda again and its
        // context uses the new connection. Closing the old connection drops the old database.
        var old = _connection;
        _connection = OpenConnection();
        await old.DisposeAsync();

        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        }

        Emails.Reset();
        (Services.GetService<IMemoryCache>() as MemoryCache)?.Clear();
        ClientIp = NextClientIp();
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    // 198.18.0.0/15 is set aside for benchmark tests, so no address here is a known proxy or a real client.
    private static IPAddress NextClientIp()
    {
        var n = Interlocked.Increment(ref s_lastClientIp);
        return new IPAddress([198, (byte)(18 + (n >> 16 & 1)), (byte)(n >> 8), (byte)n]);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, so the cookie isn't Secure-only over the test server's plain HTTP.
        builder.UseEnvironment("Development");

        // Program reads the connection string while it registers services, before
        // ConfigureTestServices runs. Any value does: the context is replaced below.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused");

        builder.ConfigureTestServices(services =>
        {
            // AddDbContext registers its provider in both of these; leaving either keeps Npgsql.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
                options.UseOpenIddict();
            });

            services.RemoveAll<IEmailSender<ApplicationUser>>();
            services.RemoveAll<IAccountEmailSender>();
            services.AddSingleton<IEmailSender<ApplicationUser>>(Emails);
            services.AddSingleton<IAccountEmailSender>(Emails);
            services.RemoveAll<IEmailQueue>();
            services.AddSingleton<IEmailQueue, InlineEmailQueue>();
            services.AddSingleton<IStartupFilter>(new ClientIpStartupFilter(this));

            // It would run at startup, before a test creates the schema; UnconfirmedAccountCleanupTests call it.
            services.Remove(services.Single(d => d.ImplementationType == typeof(UnconfirmedAccountCleanup)));
        });

        _configure?.Invoke(builder);
    }

    /// <summary>
    /// Creates the schema if needed, then a user who can sign in, or one whose email still waits for confirmation.
    /// Returns the user's ID.
    /// </summary>
    public async Task<string> CreateUserAsync(
        string email, string password, string? displayName = null, bool emailConfirmed = true, DateTime? createdTime = null)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            DisplayName = displayName,
            CreatedTime = createdTime ?? DateTime.UtcNow
        };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }

    /// <summary>Creates the schema and the role every new user gets, so the register endpoint can run.</summary>
    public async Task PrepareRegistrationAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        EnsureSucceeded(await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>()
            .CreateAsync(new IdentityRole(RoleConstants.User)));
    }

    /// <summary>Gives a user a role, making the role first if it's new.</summary>
    public async Task AddToRoleAsync(string userId, string role)
    {
        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(role))
        {
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"No user {userId}.");
        EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
    }

    // Runs before the app's own middleware, so forwarded headers and the rate limiters see the address.
    private sealed class ClientIpStartupFilter(AppApiFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = factory.ClientIp;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
