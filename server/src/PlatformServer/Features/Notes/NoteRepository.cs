using DataLibrary.DbContexts;
using DataLibrary.Entities.Notes;
using Microsoft.EntityFrameworkCore;
using SharedLibrary;
using SharedLibrary.Exceptions;

namespace PlatformServer.Features.Notes;

/// <summary>
/// A user's notes, for the API and the MCP tools. Every method takes the owner's id, so one user can never read or
/// change another's note: someone else's note is "not found", the same as a missing one.
/// </summary>
public sealed class NoteRepository(AppDbContext db)
{
    /// <summary>The user's notes, pinned first, then the most recently changed.</summary>
    public async Task<IReadOnlyList<Note>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entities = await db.Notes.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedTime ?? n.CreatedTime)
            .ToListAsync(cancellationToken);

        return [.. entities.Select(Note.From)];
    }

    public async Task<Note> GetAsync(string userId, int id, CancellationToken cancellationToken = default) =>
        Note.From(await FindAsync(userId, id, tracked: false, cancellationToken));

    public async Task<Note> CreateAsync(string userId, SaveNoteRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var entity = new NoteEntity
        {
            UserId = userId,
            Title = request.Title.Trim(),
            Body = request.Body,
            IsPinned = request.IsPinned
        };
        db.Notes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Note.From(entity);
    }

    public async Task<Note> UpdateAsync(string userId, int id, SaveNoteRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var entity = await FindAsync(userId, id, tracked: true, cancellationToken);
        entity.Title = request.Title.Trim();
        entity.Body = request.Body;
        entity.IsPinned = request.IsPinned;
        await db.SaveChangesAsync(cancellationToken);

        return Note.From(entity);
    }

    public async Task DeleteAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var deleted = await db.Notes.Where(n => n.UserId == userId && n.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw NotFound(id);
        }
    }

    private async Task<NoteEntity> FindAsync(string userId, int id, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.Notes : db.Notes.AsNoTracking();
        return await query.FirstOrDefaultAsync(n => n.UserId == userId && n.Id == id, cancellationToken)
            ?? throw NotFound(id);
    }

    private static EntityNotFoundException NotFound(int id) => new("Note", id);

    /// <summary>
    /// The request's rules, checked here as well as by the API's validation, because the MCP tools build requests
    /// of their own.
    /// </summary>
    private static void Validate(SaveNoteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(SaveNoteRequest.Title)] = ["A note needs a title."];
        }
        else if (request.Title.Trim().Length > NoteLimits.Title)
        {
            errors[nameof(SaveNoteRequest.Title)] = [$"A title is at most {NoteLimits.Title} characters."];
        }

        if (request.Body.Length > NoteLimits.Body)
        {
            errors[nameof(SaveNoteRequest.Body)] = [$"A note is at most {NoteLimits.Body} characters."];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors: errors);
        }
    }
}
