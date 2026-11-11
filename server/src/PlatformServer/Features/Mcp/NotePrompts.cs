using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using ModelContextProtocol.Server;
using PlatformServer.Configuration;
using PlatformServer.Features.Mcp.Prompts;
using PlatformServer.Features.Notes;

namespace PlatformServer.Features.Mcp;

/// <summary>
/// Prompts, the MCP "prompts" primitive: the user picks one in their chat app, and the server returns one
/// ready-to-run message, a Markdown template in <c>Prompts/</c> filled with the user's data. The chat app's own model
/// does the work; the server makes no LLM call.
/// </summary>
[McpServerPromptType]
public sealed class NotePrompts(NoteRepository notes, IHttpContextAccessor httpContextAccessor)
{
    [McpServerPrompt(Name = "summarize_my_notes")]
    [Description("Summarize all of the signed-in user's notes into a short digest, with the open to-dos they mention.")]
    public async Task<string> SummarizeMyNotes(CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var list = await notes.ListAsync(user.GetRequiredUserId(), cancellationToken);

        var text = new StringBuilder();
        foreach (var note in list)
        {
            text.AppendLine($"## {note.Title}{(note.IsPinned ? " (pinned)" : "")}");
            text.AppendLine(note.Body);
            text.AppendLine();
        }

        return McpPromptTemplate.Render("notes-summary", new Dictionary<string, string>
        {
            ["notes"] = list.Count == 0 ? "(The user has no notes yet.)" : text.ToString().TrimEnd()
        });
    }
}
