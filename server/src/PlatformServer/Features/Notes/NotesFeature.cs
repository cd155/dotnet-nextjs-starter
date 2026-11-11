using System.Security.Claims;
using PlatformServer.Configuration;

namespace PlatformServer.Features.Notes;

/// <summary>
/// The signed-in user's notes: the starter's example of a feature. The routes are under <c>/me</c>, so they find the
/// user from the session, not from an id the client sends.
/// </summary>
public static class NotesFeature
{
    public static IServiceCollection AddNotes(this IServiceCollection services)
    {
        services.AddScoped<NoteRepository>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotesEndpoints(this IEndpointRouteBuilder api)
    {
        var notes = api.MapGroup("me/notes").WithTags("Notes");

        // GET /api/v1/me/notes
        notes.MapGet("", (ClaimsPrincipal user, NoteRepository repository, CancellationToken cancellationToken) =>
            repository.ListAsync(user.GetRequiredUserId(), cancellationToken));

        // GET /api/v1/me/notes/{id}
        notes.MapGet("{id:int}", (int id, ClaimsPrincipal user, NoteRepository repository, CancellationToken cancellationToken) =>
            repository.GetAsync(user.GetRequiredUserId(), id, cancellationToken));

        // POST /api/v1/me/notes
        notes.MapPost("", async (SaveNoteRequest request, ClaimsPrincipal user, NoteRepository repository, CancellationToken cancellationToken) =>
        {
            var note = await repository.CreateAsync(user.GetRequiredUserId(), request, cancellationToken);
            return TypedResults.Created($"/api/v1/me/notes/{note.Id}", note);
        });

        // PUT /api/v1/me/notes/{id}
        notes.MapPut("{id:int}", (int id, SaveNoteRequest request, ClaimsPrincipal user, NoteRepository repository, CancellationToken cancellationToken) =>
            repository.UpdateAsync(user.GetRequiredUserId(), id, request, cancellationToken));

        // DELETE /api/v1/me/notes/{id}
        notes.MapDelete("{id:int}", async (int id, ClaimsPrincipal user, NoteRepository repository, CancellationToken cancellationToken) =>
        {
            await repository.DeleteAsync(user.GetRequiredUserId(), id, cancellationToken);
            return TypedResults.NoContent();
        });

        return api;
    }
}
