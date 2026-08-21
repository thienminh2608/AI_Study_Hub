using AIStudyHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AIStudyHub.UnitTests;

/// <summary>
/// A test-specific DbContext that inherits from the real StudyHubDbContext
/// but patches SQL Server-specific defaults (like getdate()) so they work
/// with the SQLite provider used in unit tests.
/// </summary>
public class TestStudyHubDbContext : StudyHubDbContext
{
    public TestStudyHubDbContext(DbContextOptions<StudyHubDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Replace SQL Server getdate() with SQLite datetime('now')
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var defaultSql = property.GetDefaultValueSql();
                if (defaultSql != null && (defaultSql.Contains("getdate", StringComparison.OrdinalIgnoreCase) || defaultSql.Contains("getutcdate", StringComparison.OrdinalIgnoreCase) || defaultSql.Contains("sysutcdatetime", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetDefaultValueSql("datetime('now')");
                }
            }
        }
    }
}

/// <summary>
/// Creates a fresh SQLite in-memory database for each test.
/// Implements IDisposable to clean up the connection after the test finishes.
/// </summary>
public class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public TestStudyHubDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new TestStudyHubDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
