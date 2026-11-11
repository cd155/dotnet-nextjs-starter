using System.ComponentModel;
using System.Security.Claims;
using DataLibrary.DbContexts;
using DataLibrary.Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using PlatformServer.Configuration;
using PlatformServer.Features.Notes;

namespace PlatformServer.Features.Mcp;

/// <summary>
/// The signed-in user's notes, through the same <see cref="NoteRepository"/> the API uses, so a chat can read and
/// write them. The [Description] texts are written for the model: when to call the tool, and what it returns.
/// </summary>
[McpServerToolType]
public sealed class NoteTools(NoteRepository notes, IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "list_my_notes", ReadOnly = true)]
    [Description("List the signed-in user's notes, pinned first, then the most recently changed. Each has its id, title, body and whether it's pinned.")]
    public Task<IReadOnlyList<Note>> ListMyNotes(CancellationToken cancellationToken) =>
        notes.ListAsync(UserId(), cancellationToken);

    [McpServerTool(Name = "get_note", ReadOnly = true)]
    [Description("Get one of the signed-in user's notes by its id, from list_my_notes.")]
    public Task<Note> GetNote([Description("The note's id.")] int id, CancellationToken cancellationToken) =>
        notes.GetAsync(UserId(), id, cancellationToken);

    [McpServerTool(Name = "create_note")]
    [Description("Create a note for the signed-in user. Returns the new note with its id.")]
    public Task<Note> CreateNote(
        [Description("A short title, at most 200 characters.")] string title,
        [Description("The note's text, plain or Markdown, at most 10,000 characters.")] string body,
        [Description("Whether to pin the note to the top of the list.")] bool isPinned,
        CancellationToken cancellationToken) =>
        notes.CreateAsync(UserId(), new SaveNoteRequest { Title = title, Body = body, IsPinned = isPinned }, cancellationToken);

    [McpServerTool(Name = "update_note", Idempotent = true)]
    [Description("Replace a note's title, body and pin with the values given. Call get_note first and keep what the user didn't ask to change.")]
    public Task<Note> UpdateNote(
        [Description("The note's id.")] int id,
        [Description("The new title, at most 200 characters.")] string title,
        [Description("The new text, at most 10,000 characters.")] string body,
        [Description("Whether the note is pinned.")] bool isPinned,
        CancellationToken cancellationToken) =>
        notes.UpdateAsync(UserId(), id, new SaveNoteRequest { Title = title, Body = body, IsPinned = isPinned }, cancellationToken);

    /// <summary>The user who made the MCP request: MCP is stateless here, so each call is its own HTTP request.</summary>
    private string UserId() =>
        (httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal()).GetRequiredUserId();
}

/// <summary>
/// An admin-only tool: the [Authorize] attribute hides it from everyone else's tool list, and the SDK refuses a call
/// from a user without the role (<c>AddAuthorizationFilters</c> in <see cref="McpFeature"/>).
/// </summary>
[McpServerToolType]
[Authorize(Roles = RoleConstants.Admin)]
public sealed class AdminNoteTools(AppDbContext db)
{
    [McpServerTool(Name = "count_notes", ReadOnly = true)]
    [Description("Admins only. Count every user's notes, and the users who have at least one.")]
    public async Task<NoteCounts> CountNotes(CancellationToken cancellationToken) => new(
        await db.Notes.CountAsync(cancellationToken),
        await db.Notes.Select(n => n.UserId).Distinct().CountAsync(cancellationToken));

    public sealed record NoteCounts(int Notes, int Users);
}
