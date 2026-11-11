using DataLibrary.Entities.Identity;
using DataLibrary.Entities.Notes;
using DataLibrary.Interceptors;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

public class AuditTimestampInterceptorTests
{
    [Fact]
    public async Task SaveChangesAsync_WhenEntityAdded_SetsCreatedTime()
    {
        using var sqlite = TestUtilities.CreateSqliteContext(new AuditTimestampInterceptor());
        var userId = await AddUserAsync(sqlite);
        var before = DateTime.UtcNow;

        var note = new NoteEntity { UserId = userId, Title = "New" };
        sqlite.Context.Notes.Add(note);
        await sqlite.Context.SaveChangesAsync();

        Assert.InRange(note.CreatedTime, before, DateTime.UtcNow);
        Assert.Null(note.UpdatedTime);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCreatedTimeProvided_KeepsIt()
    {
        using var sqlite = TestUtilities.CreateSqliteContext(new AuditTimestampInterceptor());
        var userId = await AddUserAsync(sqlite);
        var createdTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var note = new NoteEntity { UserId = userId, Title = "Imported", CreatedTime = createdTime };
        sqlite.Context.Notes.Add(note);
        await sqlite.Context.SaveChangesAsync();

        Assert.Equal(createdTime, note.CreatedTime);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityModified_SetsUpdatedTimeAndKeepsCreatedTime()
    {
        using var sqlite = TestUtilities.CreateSqliteContext(new AuditTimestampInterceptor());
        var userId = await AddUserAsync(sqlite);
        var note = new NoteEntity { UserId = userId, Title = "Old" };
        sqlite.Context.Notes.Add(note);
        await sqlite.Context.SaveChangesAsync();
        var createdTime = note.CreatedTime;

        note.Title = "Renamed";
        await sqlite.Context.SaveChangesAsync();

        Assert.Equal(createdTime, note.CreatedTime);
        Assert.NotNull(note.UpdatedTime);
        Assert.True(note.UpdatedTime >= createdTime);
    }

    private static async Task<string> AddUserAsync(SqliteTestContext sqlite)
    {
        var user = new ApplicationUser { UserName = "owner@example.com", Email = "owner@example.com" };
        sqlite.Context.Users.Add(user);
        await sqlite.Context.SaveChangesAsync();
        return user.Id;
    }
}
