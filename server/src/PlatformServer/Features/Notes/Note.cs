using DataLibrary.Entities.Notes;

namespace PlatformServer.Features.Notes;

/// <summary>A note as the API and the MCP tools return it.</summary>
public sealed record Note
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required bool IsPinned { get; init; }
    public required DateTime CreatedTime { get; init; }
    public DateTime? UpdatedTime { get; init; }

    public static Note From(NoteEntity entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Body = entity.Body,
        IsPinned = entity.IsPinned,
        CreatedTime = entity.CreatedTime,
        UpdatedTime = entity.UpdatedTime
    };
}
