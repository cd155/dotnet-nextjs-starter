using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataLibrary.DbContexts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// Real MCP access tokens, from the app's own OAuth flow as a chatbot runs it: sign in with the cookie, authorize with
/// PKCE and the MCP resource, and exchange the code. The factory needs <c>MCP_AUTHORITY</c> and <c>MCP_RESOURCE</c>
/// set, as <see cref="McpTokens.CreateFactory"/> does.
/// </summary>
public static class McpTokens
{
    /// <summary>The test server's base address, and so the tokens' issuer.</summary>
    public const string Authority = "http://localhost";

    /// <summary>The tokens' audience.</summary>
    public const string Resource = "http://localhost/mcp";

    private const string ClientId = "test-connector";
    private const string RedirectUri = "http://localhost/callback";

    /// <summary>A factory with MCP sign-in configured, and whatever else <paramref name="configure"/> sets.</summary>
    public static AppApiFactory CreateFactory(Action<IWebHostBuilder>? configure = null) => new(builder =>
    {
        builder.UseSetting("MCP_AUTHORITY", Authority).UseSetting("MCP_RESOURCE", Resource);
        configure?.Invoke(builder);
    });

    /// <summary>An access token for the user, who must exist. Registers the test client and the scope on first use.</summary>
    public static async Task<string> GetAccessTokenAsync(AppApiFactory factory, string email, string password)
    {
        await RegisterClientAsync(factory, ClientId, RedirectUri);
        return await GetAccessTokenAsync(factory, email, password, ClientId, RedirectUri);
    }

    /// <summary>
    /// An access token for the user, who must exist, signed in through a client <see cref="RegisterClientAsync"/>
    /// registered and sent to <paramref name="redirectUri"/>.
    /// </summary>
    public static async Task<string> GetAccessTokenAsync(
        AppApiFactory factory, string email, string password, string clientId, string redirectUri)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await ApiAuth.SignInAsync(client, email, password);

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var authorize = await AuthorizeAsync(client, clientId, redirectUri, verifier);
        if (authorize.StatusCode != HttpStatusCode.Redirect || authorize.Headers.Location is not { } location
            || !location.ToString().StartsWith(redirectUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Authorize answered {(int)authorize.StatusCode}: {authorize.Headers.Location} {await authorize.Content.ReadAsStringAsync()}");
        }

        var code = QueryHelpers.ParseQuery(location.Query)[Parameters.Code].ToString();
        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.AuthorizationCode,
            [Parameters.Code] = code,
            [Parameters.RedirectUri] = redirectUri,
            [Parameters.ClientId] = clientId,
            [Parameters.CodeVerifier] = verifier,
        }));
        var body = await token.Content.ReadAsStringAsync();
        if (!token.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token answered {(int)token.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
    }

    /// <summary>
    /// The authorize request a chatbot's sign-in makes, from a signed-in browser that doesn't follow redirects. The
    /// answer is the redirect to the callback with the code, or the error. Without a
    /// <paramref name="codeVerifier"/> it sends the challenge of a random one.
    /// </summary>
    public static Task<HttpResponseMessage> AuthorizeAsync(
        HttpClient client, string clientId, string redirectUri, string? codeVerifier = null)
    {
        codeVerifier ??= Base64Url(RandomNumberGenerator.GetBytes(32));
        return client.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            [Parameters.ResponseType] = ResponseTypes.Code,
            [Parameters.ClientId] = clientId,
            [Parameters.RedirectUri] = redirectUri,
            [Parameters.Scope] = "mcp",
            [Parameters.CodeChallenge] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))),
            [Parameters.CodeChallengeMethod] = CodeChallengeMethods.Sha256,
            [Parameters.Resource] = Resource,
            [Parameters.State] = "state",
        }));
    }

    /// <summary>
    /// Registers a client as DatabaseInitializer's OpenIddictSeeder registers the chatbot connectors, unless it
    /// exists: a public PKCE client with its callbacks, and the mcp scope bound to the resource, so the token's
    /// audience is /mcp.
    /// </summary>
    public static async Task RegisterClientAsync(AppApiFactory factory, string clientId, params string[] redirectUris)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync("mcp") is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = "mcp", Resources = { Resource } });
        }

        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(clientId) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + "mcp",
                    Permissions.Prefixes.Resource + Resource,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            };
            descriptor.RedirectUris.UnionWith(redirectUris.Select(uri => new Uri(uri)));

            await applications.CreateAsync(descriptor);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
