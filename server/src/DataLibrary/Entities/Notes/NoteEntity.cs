using DataLibrary.Entities.Identity;

namespace DataLibrary.Entities.Notes;

/// <summary>
/// A user's note: the starter's example feature. Copy its path through the layers (this entity, the
/// DbContext, <c>Features/Notes</c>, the MCP <c>NoteTools</c>, the CMS page and the web's <c>/notes</c>) when you
/// add a feature of your own.
/// </summary>
public class NoteEntity : BaseEntity
{
    /// <summary>The owner. Deleting the user deletes their notes.</summary>
    public required string UserId { get; set; }

    public ApplicationUser? User { get; set; }

    public required string Title { get; set; }

    public string Body { get; set; } = "";

    /// <summary>Pinned notes are listed first.</summary>
    public bool IsPinned { get; set; }
}
