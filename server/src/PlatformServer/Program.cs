using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using PlatformServer.Components;
using PlatformServer.Configuration;
using PlatformServer.Features.Account;
using PlatformServer.Features.Auth;
using PlatformServer.Features.Email;
using PlatformServer.Features.Mcp;
using PlatformServer.Features.Notes;
using Scalar.AspNetCore;

namespace PlatformServer;

/// <summary>
/// Entry point for the Platform Server: the API, the Blazor CMS and the MCP endpoint.
/// Configures and runs the web server with all necessary services and middleware.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        ConfigureSentry(builder);

        // Configure services
        ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

        WebApplication app = builder.Build();

        LogStartupSettings(app);

        // Configure middleware pipeline
        ConfigureMiddleware(app);

        app.Run();
    }

    /// <summary>
    /// Initializes Sentry when a DSN is provided. UseSentry also adds Sentry's ILogger provider, so an event
    /// logged at Error reaches Sentry, and lower levels ride along as breadcrumbs.
    /// </summary>
    /// <param name="builder">The web application builder</param>
    private static void ConfigureSentry(WebApplicationBuilder builder)
    {
        var sentryDsn = builder.Configuration["SENTRY_SERVER_DSN"];

        if (string.IsNullOrWhiteSpace(sentryDsn))
        {
            return;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = sentryDsn;
            options.Environment = builder.Environment.EnvironmentName;
            options.AddExceptionFilter(new ClientErrorExceptionFilter());
            // The MCP SDK logs every McpException a tool or prompt throws at Error, but it is the answer the model
            // gets back, such as a note that isn't found. Other exceptions in a tool still report.
            options.AddExceptionFilterForType<McpException>();
#if DEBUG
            options.Debug = true;
#endif
        });

        // Sentry gives its logger provider a rule of its own, which takes the place of Logging:LogLevel, so its
        // caps are copied to that provider. Without them, ASP.NET Core's request lines would reach Sentry's
        // breadcrumbs.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            var caps = options.Rules.Where(rule => rule.ProviderName is null && rule.CategoryName is not null).ToList();
            foreach (var cap in caps)
            {
                options.Rules.Add(new LoggerFilterRule("Sentry", cap.CategoryName, cap.LogLevel, cap.Filter));
            }
        });
    }

    /// <summary>
    /// Logs the settings worth seeing at startup. No logger exists while the services are configured, so
    /// these wait until the app is built.
    /// </summary>
    /// <param name="app">The web application</param>
    private static void LogStartupSettings(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration["SENTRY_SERVER_DSN"]))
        {
            app.Logger.LogInformation("Sentry is not configured. Provide SENTRY_SERVER_DSN to enable monitoring.");
        }

        // Email isn't needed to start: without it, only the emails fail (EmailSender). Outside Development that
        // is an error, so it reaches Sentry.
        var emailOptions = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        if (emailOptions.LogsInsteadOfSending(app.Environment))
        {
            app.Logger.LogInformation("Email is not configured, so emails are logged instead of sent. Provide Email__Host, or run Mailpit (docker-compose.local.yml).");
        }
        else if (emailOptions.MissingSettings() is { Count: > 0 } missingEmailSettings)
        {
            app.Logger.LogError("Email is not configured ({MissingSettings} not set), so every email will fail to send", string.Join(", ", missingEmailSettings));
        }

        // Without Turnstile's secret key, sign-up and resending the confirmation refuse everyone (TurnstileVerifier).
        var turnstileOptions = app.Services.GetRequiredService<IOptions<TurnstileOptions>>().Value;
        if (turnstileOptions.SkipsCheck(app.Environment))
        {
            app.Logger.LogInformation("Turnstile is not configured, so sign-up skips the bot check. Provide Turnstile__SecretKey to check it.");
        }
        else if (string.IsNullOrWhiteSpace(turnstileOptions.SecretKey))
        {
            app.Logger.LogError("Turnstile is not configured (Turnstile:SecretKey not set), so every sign-up will be refused");
        }

        // Surface the resource indicator (see AddOpenIddictServer), so a mismatch with what the client sends (the
        // usual cause of invalid_target, ID2190) shows in the logs.
        var mcpResource = app.Configuration["MCP_RESOURCE"];
        if (!string.IsNullOrWhiteSpace(mcpResource))
        {
            app.Logger.LogInformation("OpenIddict authorization server allowlisted MCP resource indicator: {McpResource}", mcpResource);
        }

        // Sign-in to /mcp isn't needed to start: without it, the MCP endpoint refuses every request.
        if (app.Configuration.MissingMcpSettings() is { Count: > 0 } missingMcpSettings)
        {
            app.Logger.LogError("MCP sign-in is not configured ({MissingSettings} not set), so the MCP endpoint refuses every request", string.Join(", ", missingMcpSettings));
        }
    }

    /// <summary>
    /// Configures all application services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The application configuration</param>
    /// <param name="environment">The hosting environment</param>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // Database and data access
        services.AddDatabaseContext(configuration);

        // The features. Account uses Identity's services and has none of its own.
        services.AddNotes();

        // Authentication via ASP.NET Core Identity: the cookie backs the Blazor UI and the web app's API calls
        services.AddIdentityAuthentication(environment);

        // Transactional email over SMTP, Identity's sender (your provider in production, Mailpit locally)
        services.AddEmail();

        // The account's emails, whose links open the web app
        services.AddAccount();

        // OAuth 2.1 authorization server (OpenIddict) on the same Identity users, so MCP clients can get
        // bearer tokens for them
        services.AddOpenIddictServer(environment, configuration);

        // The MCP endpoint (/mcp) for AI chat apps, signed in by the bearer tokens issued above
        services.AddMcp(configuration);

        // Configure forwarded headers for reverse proxy (Caddy) to properly detect client IP
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Restrict trusted sources for forwarded headers to Docker internal networks
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // Docker's default address pool (172.16.0.0/12, i.e. 172.16-172.31). Compose hands each
            // network the next free subnet from this pool, so caddy-net lands on whatever is free on
            // the host. When the proxy sits outside the trusted set its X-Forwarded-For/-Proto
            // headers are dropped and every request looks like plain HTTP from the proxy's address.
            // Nothing outside Docker can reach this container anyway — it publishes no host ports
            // behind Caddy.
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
        });

        // Rate limiting for login endpoint to prevent brute force attacks
        services.AddRateLimiter(options =>
        {
            // Sign-in and change-password limiter - per client IP. Partitioned like the global limiter below:
            // a single shared window would let any five requests lock every user out for a minute.
            options.AddPolicy("login", context =>
            {
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,                    // 5 attempts
                    Window = TimeSpan.FromMinutes(1),   // per minute
                    QueueLimit = 0                      // No queuing, reject immediately
                });
            });

            // Endpoints that send email (registration, resending the confirmation, forgot-password) - per client IP.
            // Tighter than "login", so one caller can't spend the day's sending quota.
            options.AddPolicy("email", context =>
            {
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                });
            });

            // Every request to /mcp - per signed-in user, as the global limit below is per IP for /api. /mcp always
            // has a signed-in user; the "unknown" partition is only a fallback.
            options.AddPolicy(AuthSchemes.McpRateLimit, context =>
            {
                var userId = context.User.FindUserId() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 200,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });

            // Global API limiter - per client IP. An abuse guard sized for ordinary browsing.
            //
            // Partitioning only works if the caller's IP actually reaches here: a server-side web
            // app in front of this API must relay X-Forwarded-For, otherwise every user shares one
            // partition because all API traffic arrives from that one container.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Only apply to API routes
                if (!context.Request.Path.StartsWithSegments("/api"))
                    return RateLimitPartition.GetNoLimiter(string.Empty);

                // RemoteIpAddress is automatically populated from X-Forwarded-For by UseForwardedHeaders()
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 200,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });

            // Handle rate limit rejections
            options.OnRejected = async (context, cancellationToken) =>
            {
                var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                // For API and MCP routes, return JSON 429 response
                if (context.HttpContext.Request.Path.StartsWithSegments("/api")
                    || context.HttpContext.Request.Path.StartsWithSegments(McpFeature.Path))
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";

                    var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? (int)retryAfter.TotalSeconds
                        : 60;

                    context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

                    logger.LogWarning(
                        "API rate limit exceeded from {ClientIp} for path {Path}",
                        clientIp,
                        context.HttpContext.Request.Path);

                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "Too many requests. Please try again later.",
                        retryAfterSeconds = retryAfterSeconds
                    }, cancellationToken);
                    return;
                }

                // For login page, redirect with error
                logger.LogWarning(
                    "Rate limit exceeded for login from {ClientIp} - possible brute force attack",
                    clientIp);
                context.HttpContext.Response.Redirect("/login?error=ratelimit");
            };
        });

        // CORS for the web app. Origins come from Cors:AllowedOrigins so each
        // environment supplies its own (e.g. Cors__AllowedOrigins__0 in production).
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy("WebFrontend", policy =>
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });

        // Web services
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Minimal APIs and the OpenAPI document read the HTTP JSON options; without them the features
        // would send enums as numbers. Numbers are strict, as the clients send them as numbers; otherwise
        // every number is typed "integer or string".
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        // Errors go through the built-in exception handler, and AppExceptionHandler answers the app's own.
        services.AddExceptionHandling(environment);

        // A Minimal API argument that can't be bound (an unknown enum value, a word for a number) throws in
        // every environment, not only Development, so AppExceptionHandler answers it with a 400 and a problem
        // body. Otherwise production sends an empty 400: Minimal APIs don't write one, even with
        // IProblemDetailsService registered.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        // The request records' [Required] and other attributes, checked before a handler runs: a failure is a
        // 400 with the errors by field, as [ApiController] answered.
        services.AddValidation();

        // API versioning, for the features. AddMvc isn't called: there are no controllers.
#pragma warning disable AV0013
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        });
#pragma warning restore AV0013

        // OpenAPI document generation (Microsoft native, .NET 10+)
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new()
                {
                    Title = "Starter API",
                    Version = "v1",
                    Description = "The API behind the starter's web app."
                };
                return Task.CompletedTask;
            });
            options.AddSchemaTransformer<RequiredPropertiesSchemaTransformer>();
        });
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.NumberHandling = JsonNumberHandling.Strict;
    }

    /// <summary>
    /// Configures the middleware pipeline for handling HTTP requests.
    /// </summary>
    /// <param name="app">The web application</param>
    private static void ConfigureMiddleware(WebApplication app)
    {
        // Handle forwarded headers from reverse proxy (Caddy) - must be early in pipeline
        app.UseForwardedHeaders();

        app.UseCors("WebFrontend");

        app.UseExceptionHandler();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            // OpenAPI JSON: GET /openapi/v1.json
            app.MapOpenApi().AllowAnonymous();
            // Scalar UI: GET /scalar/v1
            app.MapScalarApiReference().AllowAnonymous();
        }
        else
        {
            // The default HSTS value is 30 days. You may want to change this for production scenarios.
            app.UseHsts();
        }

        // Note: HTTPS redirection is handled by Caddy reverse proxy
        // app.UseHttpsRedirection();

        app.UseStaticFiles();

        // Authentication first, so the per-user limit on /mcp sees the user
        app.UseAuthentication();

        // Rate limiting to prevent brute force attacks on login
        app.UseRateLimiter();

        app.UseAuthorization();

        // Must come after authentication/authorization so antiforgery tokens validate against the signed-in user
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapAuthEndpoints();

        var api = app.MapApiV1();
        api.MapAccountEndpoints();
        api.MapNotesEndpoints();

        app.MapMcpEndpoint();

        app.MapHealthChecks("/health").AllowAnonymous();
    }
}
