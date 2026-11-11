using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using OpenIddict.Validation.AspNetCore;
using PlatformServer.Configuration;
using SharedLibrary.Exceptions;

namespace PlatformServer.Features.Mcp;

/// <summary>
/// The MCP (Model Context Protocol) endpoint: the app's tools and prompts over streamable HTTP at
/// <c>/mcp</c>, for chatbots (Claude connectors, MCP Inspector).
/// </summary>
/// <remarks>
/// <para>
/// Sign-in is OAuth 2.1: this app's own OpenIddict server issues the access tokens, and requests to
/// <c>/mcp</c> are signed in by their bearer token, never by the CMS cookie, so a browser can't call a
/// tool with the CMS session. Everything else keeps the Identity cookie.
/// </para>
/// <para>
/// Every request needs a signed-in user. A request without a token, or with one that doesn't sign in, gets a 401
/// that points at the protected-resource metadata, so the client signs in. A tool that needs a role carries
/// <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/> with it, which the SDK checks itself
/// (<c>AddAuthorizationFilters</c>) and which hides the tool from a user without the role.
/// </para>
/// </remarks>
public static class McpFeature
{
    /// <summary>The endpoint's path. Clients connect to <c>https://&lt;host&gt;/mcp</c>.</summary>
    public const string Path = "/mcp";

    /// <summary>Picks the bearer token for <c>/mcp</c> and the Identity cookie for everything else.</summary>
    private const string SchemeSelector = "McpOrCookie";

    /// <summary>
    /// What the server tells a chatbot about itself when it connects. It names areas, not tools, so it doesn't go
    /// stale when a tool changes; <c>list_capabilities</c> gives the exact list. Not every client passes it on.
    /// </summary>
    private const string Instructions = """
        This server gives a signed-in user access to their notes in the app: list, read, create and update them. A
        prompt summarizes them into a digest. Admins can also count every user's notes.

        Which tools the user has depends on their role. When the user asks what this server can do, or which
        commands, tools or prompts are available, call list_capabilities and show them the whole list.
        """;

    /// <summary>Signs no one in: what <c>/mcp</c> authenticates with when its sign-in isn't configured.</summary>
    private const string NoSignInScheme = "McpNoSignIn";

    /// <summary>
    /// Registers the MCP server and, when <c>MCP_AUTHORITY</c> and <c>MCP_RESOURCE</c> are set, the
    /// bearer-token sign-in for <c>/mcp</c>. Call after <c>AddIdentityAuthentication</c>, whose default
    /// schemes this replaces.
    /// </summary>
    public static IServiceCollection AddMcp(this IServiceCollection services, IConfiguration configuration)
    {
        // Stateless = true treats every tool call as an independent request: right for request/response
        // tools. Switch to false if a tool needs per-session state or server-initiated messages
        // (sampling/elicitation). Tool classes are listed explicitly so the exposed surface is in one place.
        services.AddMcpServer(options => options.ServerInstructions = Instructions)
            .WithHttpTransport(options => options.Stateless = true)
            // Required for the tools with [Authorize(Roles = ...)]; without it the SDK returns -32603 on list.
            .AddAuthorizationFilters()
            .WithTools<ServerTools>()
            .WithTools<NoteTools>()
            .WithTools<AdminNoteTools>()
            .WithPrompts<NotePrompts>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                // The SDK hands the model only an McpException's message, and a generic one for anything else. The
                // feature repositories throw SharedLibrary's exceptions, whose messages are the answer: pass them on.
                try
                {
                    return await next(context, cancellationToken);
                }
                catch (AppException exception)
                {
                    throw new McpException(MessageOf(exception), exception);
                }
            }));

        // Every path but /mcp keeps the Identity cookie. /mcp never falls back to it, whether or not its own
        // sign-in is configured.
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = SchemeSelector;
            options.DefaultChallengeScheme = SchemeSelector;
            options.DefaultForbidScheme = SchemeSelector;
        });

        // Sign-in isn't needed to start: without it nobody can sign in to /mcp, so it refuses every request
        // (this scheme's challenge is a plain 401). Program logs the missing settings.
        if (configuration.MissingMcpSettings().Count > 0)
        {
            authentication
                .AddScheme<AuthenticationSchemeOptions, NoSignInHandler>(NoSignInScheme, displayName: null, configureOptions: null)
                .AddPolicyScheme(SchemeSelector, displayName: null, options =>
                    options.ForwardDefaultSelector = context => context.Request.Path.StartsWithSegments(Path)
                        ? NoSignInScheme
                        : IdentityConstants.ApplicationScheme);

            return services;
        }

        var authority = configuration["MCP_AUTHORITY"]!;
        var resource = configuration["MCP_RESOURCE"]!;

        // Validates the access tokens this app's OpenIddict server issues, in-process: no discovery
        // document or key download. A token must name /mcp as its audience (the "mcp" scope is bound to
        // that resource by DatabaseInitializer's OpenIddictSeeder).
        services.AddOpenIddict()
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.AddAudiences(resource);
            });

        authentication
            // The MCP scheme owns the 401 challenge (it advertises the protected-resource metadata URL) and
            // serves /.well-known/oauth-protected-resource, which points clients at the authorization server.
            // It hands authenticating the request to OpenIddict's validation.
            .AddMcp(options =>
            {
                options.ForwardAuthenticate = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = resource,
                    AuthorizationServers = { authority },
                    ScopesSupported = { "mcp" },
                };
            })
            .AddPolicyScheme(SchemeSelector, displayName: null, options =>
                options.ForwardDefaultSelector = context => context.Request.Path.StartsWithSegments(Path)
                    ? McpAuthenticationDefaults.AuthenticationScheme
                    : IdentityConstants.ApplicationScheme);

        return services;
    }

    // The message, then each field's errors, e.g. "… Title: A note needs a title." The default
    // validation message says nothing the errors don't, so it's left out.
    private static string MessageOf(AppException exception)
    {
        if (exception is not AppValidationException { Errors.Count: > 0 } validation)
        {
            return exception.Message;
        }

        var errors = validation.Errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}");
        return string.Join(" ", exception.Message == ExceptionConstants.DefaultValidationMessage ? errors : [exception.Message, .. errors]);
    }

    /// <summary>
    /// The MCP sign-in settings that are not set: <c>MCP_AUTHORITY</c> (this app's public base URL, the
    /// tokens' issuer) and <c>MCP_RESOURCE</c> (the public <c>/mcp</c> URL, the tokens' audience).
    /// </summary>
    public static IReadOnlyList<string> MissingMcpSettings(this IConfiguration configuration) =>
        new[] { "MCP_AUTHORITY", "MCP_RESOURCE" }
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();

    /// <summary>
    /// Signs no one in. ASP.NET Core has no built-in scheme that does nothing, and without one /mcp would fall back to
    /// the CMS cookie. (The MCP scheme can't stand in: it needs its resource metadata, which needs the settings.)
    /// </summary>
    private sealed class NoSignInHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <summary>
    /// Maps the MCP endpoint. It needs a signed-in user; a signed-out request is challenged by the MCP scheme, whose
    /// 401 points at the protected-resource metadata. Tools that need a role say so themselves. Every request counts
    /// against the user's <see cref="AuthSchemes.McpRateLimit"/>.
    /// </summary>
    public static void MapMcpEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMcp(Path).RequireAuthorization().RequireRateLimiting(AuthSchemes.McpRateLimit);
    }
}
