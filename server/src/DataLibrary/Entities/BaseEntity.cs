namespace DataLibrary.Entities;

/// <summary>
/// A row the server creates. There's no soft-delete flag: an entity that needs a state like
/// "published" declares it under its own name.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime? UpdatedTime { get; set; }
}
