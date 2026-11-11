using System.ComponentModel.DataAnnotations;
using SharedLibrary;

namespace PlatformServer.Features.Notes;

/// <summary>A new note, or a note's new values: the whole note, so creating and editing take the same body.</summary>
public sealed record SaveNoteRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(NoteLimits.Title)]
    public required string Title { get; init; }

    [StringLength(NoteLimits.Body)]
    public string Body { get; init; } = "";

    public bool IsPinned { get; init; }
}
