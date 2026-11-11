namespace SharedLibrary;

/// <summary>The longest a note's fields may be, shared by the column sizes and the request validation.</summary>
public static class NoteLimits
{
    public const int Title = 200;
    public const int Body = 10_000;
}
