using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DatabaseInitializer.Data;

/// <summary>
/// Seeds what the MCP endpoint's sign-in needs: the <c>mcp</c> scope, bound to the MCP resource so access tokens
/// carry it as their audience, and one pre-registered client per AI app. There is no dynamic client registration.
/// Idempotent, so it runs on every start.
/// </summary>
public static class OpenIddictSeeder
{
    /// <summary>
    /// Claude's callback for its hosted apps (claude.ai, Desktop, mobile). Anthropic fixes it:
    /// https://claude.com/docs/connectors/building/authentication#callback-urls
    /// </summary>
    private const string ClaudeRedirectUri = "https://claude.ai/api/mcp/auth_callback";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var resource = configuration["MCP_RESOURCE"];

        await SeedScopeAsync(services.GetRequiredService<IOpenIddictScopeManager>(), resource);

        var applicationManager = services.GetRequiredService<IOpenIddictApplicationManager>();

        // Public: Claude sends a client secret only when the user types one into its connector dialog.
        await SeedClientAsync(applicationManager, "claude-connector", "Claude", ClaudeRedirectUri, resource);

        // MCP Inspector, for trying the tools locally. Its callback is http://localhost:6274/oauth/callback.
        var inspectorRedirectUri = configuration["MCP_INSPECTOR_REDIRECT_URI"];
        if (string.IsNullOrWhiteSpace(inspectorRedirectUri))
        {
            Console.WriteLine("MCP_INSPECTOR_REDIRECT_URI not configured. Skipping the MCP Inspector client.");
        }
        else
        {
            await SeedClientAsync(applicationManager, "mcp-inspector", "MCP Inspector", inspectorRedirectUri, resource);
        }
    }

    private static async Task SeedScopeAsync(IOpenIddictScopeManager scopeManager, string? resource)
    {
        var existing = await scopeManager.FindByNameAsync("mcp");
        if (existing is null)
        {
            var descriptor = new OpenIddictScopeDescriptor { Name = "mcp", DisplayName = "MCP tools" };

            // Binding the resource makes OpenIddict stamp aud = resource on access tokens, which /mcp checks.
            if (!string.IsNullOrWhiteSpace(resource))
            {
                descriptor.Resources.Add(resource);
            }

            await scopeManager.CreateAsync(descriptor);
            Console.WriteLine("Created OpenIddict scope: mcp");
            return;
        }

        if (!string.IsNullOrWhiteSpace(resource) && !(await scopeManager.GetResourcesAsync(existing)).Contains(resource))
        {
            // Created before MCP_RESOURCE was set: bind it now.
            var descriptor = new OpenIddictScopeDescriptor();
            await scopeManager.PopulateAsync(descriptor, existing);
            descriptor.Resources.Add(resource);
            await scopeManager.UpdateAsync(existing, descriptor);
            Console.WriteLine("Bound the OpenIddict scope 'mcp' to the configured resource.");
        }
    }

    private static async Task SeedClientAsync(
        IOpenIddictApplicationManager applicationManager, string clientId, string displayName, string redirectUri, string? resource)
    {
        // rsrc: permission for the resource indicator, or OpenIddict rejects the authorize request (ID2192).
        var resourcePermission = string.IsNullOrWhiteSpace(resource) ? null : Permissions.Prefixes.Resource + resource;

        var existing = await applicationManager.FindByClientIdAsync(clientId);
        if (existing is not null)
        {
            // Add what's missing, keeping URIs already registered so a change doesn't break a sign-in in flight.
            var current = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(current, existing);

            var updated = false;
            if (resourcePermission is not null && current.Permissions.Add(resourcePermission))
            {
                updated = true;
            }

            if (current.RedirectUris.Add(new Uri(redirectUri)))
            {
                updated = true;
            }

            if (updated)
            {
                await applicationManager.UpdateAsync(existing, current);
                Console.WriteLine($"Updated OpenIddict client '{clientId}'.");
            }

            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            // Implicit consent: a signed-in user goes straight back to the app, with no consent page to build.
            ConsentType = ConsentTypes.Implicit,
            // Public: PKCE stands in for a client secret.
            ClientType = ClientTypes.Public,
            RedirectUris = { new Uri(redirectUri) },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + "mcp"
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };

        if (resourcePermission is not null)
        {
            descriptor.Permissions.Add(resourcePermission);
        }

        await applicationManager.CreateAsync(descriptor);
        Console.WriteLine($"Created OpenIddict client: {clientId}");
    }
}
