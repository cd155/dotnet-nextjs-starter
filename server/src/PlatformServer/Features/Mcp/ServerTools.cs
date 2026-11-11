using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PlatformServer.Features.Mcp;

/// <summary>
/// The smallest possible MCP tool: no database, no injected services.
/// Connect a client (MCP Inspector, Claude) and call "ping" to prove the transport,
/// routing, and tool discovery work end-to-end before debugging anything data-related.
/// </summary>
/// <remarks>
/// Anatomy of a tool class:
///   - [McpServerToolType] marks the class as a tool container; it must also be registered in
///     McpFeature.AddMcp via WithTools&lt;ServerTools&gt;().
///   - [McpServerTool(Name = ...)] exposes a method. Pin the name explicitly so renaming the C#
///     method never silently changes the public tool name clients already rely on.
///   - [Description] text is what the AI reads to decide WHEN to call the tool — write it for
///     the model (trigger conditions, expected input), not for human API docs.
/// </remarks>
[McpServerToolType]
public sealed class ServerTools
{
    [McpServerTool(Name = "ping")]
    [Description("Connectivity check for this MCP server. Call this to verify the connection works; it returns 'pong' with the server's current UTC time.")]
    public static string Ping() => $"pong — the MCP server is reachable. UTC time: {DateTime.UtcNow:O}";

    /// <summary>
    /// The tools and prompts the user can use, read from the server's registered ones and filtered by their
    /// <see cref="AuthorizeAttribute"/> the way <c>tools/list</c> and <c>prompts/list</c> filter them. Chatbots often
    /// show the model only some of a server's tools, so asking it what the server offers gave a partial answer.
    /// </summary>
    [McpServerTool(Name = "list_capabilities", ReadOnly = true)]
    [Description("List every tool and prompt this server offers the signed-in user, with what each one does. Call this whenever the user asks what this server can do, or which commands, tools or prompts are available, and show them the whole list.")]
    public static async Task<Capabilities> ListCapabilities(
        RequestContext<CallToolRequestParams> context,
        IAuthorizationPolicyProvider policies,
        IAuthorizationService authorization)
    {
        var options = context.Server.ServerOptions;
        var user = context.User ?? new ClaimsPrincipal();

        var tools = new List<Capability>();
        foreach (var tool in options.ToolCollection ?? [])
        {
            if (await IsAllowedAsync(tool.Metadata))
            {
                tools.Add(new Capability(tool.ProtocolTool.Name, tool.ProtocolTool.Description));
            }
        }

        var prompts = new List<Capability>();
        foreach (var prompt in options.PromptCollection ?? [])
        {
            if (await IsAllowedAsync(prompt.Metadata))
            {
                prompts.Add(new Capability(prompt.ProtocolPrompt.Name, prompt.ProtocolPrompt.Description));
            }
        }

        return new Capabilities(tools, prompts);

        async Task<bool> IsAllowedAsync(IReadOnlyList<object> metadata)
        {
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return true;
            }

            var policy = await AuthorizationPolicy.CombineAsync(policies, metadata.OfType<IAuthorizeData>());
            return policy is null || (await authorization.AuthorizeAsync(user, policy)).Succeeded;
        }
    }

    /// <summary>What <c>list_capabilities</c> returns: the tools, then the prompts, in the order they're registered.</summary>
    public sealed record Capabilities(IReadOnlyList<Capability> Tools, IReadOnlyList<Capability> Prompts);

    /// <summary>One tool or prompt: its name and its description, as the client's own list shows them.</summary>
    public sealed record Capability(string Name, string? Description);
}
