using System;
using DataLibrary.DbContexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// Provides common utilities for test classes to reduce code duplication
/// and ensure consistent test setup across the test suite.
/// </summary>
public static class TestUtilities
{
    /// <summary>
    /// Creates an in-memory SQLite-backed context that fully supports relational
    /// behaviour including transactions, foreign keys, and unique constraints.
    /// The returned wrapper keeps the SQLite connection alive; dispose it when done.
    /// </summary>
    /// <param name="interceptors">Optional EF Core interceptors to register on the context</param>
    public static SqliteTestContext CreateSqliteContext(params IInterceptor[] interceptors) => new(interceptors);
}

/// <summary>
/// Wraps a SQLite in-memory connection and an EF Core context with foreign
/// key enforcement enabled. Disposing this object tears down both the context
/// and the connection, destroying the in-memory database.
/// </summary>
public sealed class SqliteTestContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Context { get; }
    public DbContextOptions<AppDbContext> Options { get; }

    public SqliteTestContext(params IInterceptor[] interceptors)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaCmd.ExecuteNonQuery();

        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .UseOpenIddict()
            .Options;
        Context = new AppDbContext(Options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
