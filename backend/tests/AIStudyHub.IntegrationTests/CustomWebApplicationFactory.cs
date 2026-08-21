using System;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using AIStudyHub.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace AIStudyHub.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "mem_" + Guid.NewGuid().ToString("N");
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("Testing:UseInMemoryDb", "true"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Key", "m9uS6yBuZvrkIS8LcHlCvnJY7sbj9QEximY0oPcvKNM"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Issuer", "AIStudyHub.Api"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Jwt:Audience", "AIStudyHub.Client"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Auth:OtpPepper", "TestOtpPepperSecretForIntegrationTests2026!"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Ledger:SecretKey", "TestLedgerSecretKey_IntegrationTests_2026"),
                new System.Collections.Generic.KeyValuePair<string, string?>("PayOS:ChecksumKey", "d87a9b76e2c34f19b28a9c3d4e5f6071"),
                new System.Collections.Generic.KeyValuePair<string, string?>("PayOS:ClientId", "mock-client-id"),
                new System.Collections.Generic.KeyValuePair<string, string?>("PayOS:ApiKey", "mock-api-key"),
                new System.Collections.Generic.KeyValuePair<string, string?>("Frontend:BaseUrl", "http://localhost:5173")
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StudyHubDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            var dbConnectionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbConnection));
            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            // Remove BackgroundServices that may interfere with tests
            var schedulerDescriptors = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in schedulerDescriptors)
            {
                services.Remove(descriptor);
            }

            // Create open SQLite in-memory connection with unique shared cache for thread safety
            _connection = new SqliteConnection($"Data Source=file:{_dbName}?mode=memory&cache=shared");
            _connection.Open();

            services.AddDbContext<TestStudyHubDbContext>(options =>
            {
                options.UseSqlite($"Data Source=file:{_dbName}?mode=memory&cache=shared");
            });
            services.AddScoped<StudyHubDbContext>(provider => provider.GetRequiredService<TestStudyHubDbContext>());
            services.AddScoped<IStudyHubDbContext>(provider => provider.GetRequiredService<TestStudyHubDbContext>());
        });
    }

    public string GenerateJwtToken(int userId, string username, string role)
    {
        const string jwtKey = "m9uS6yBuZvrkIS8LcHlCvnJY7sbj9QEximY0oPcvKNM";
        const string issuer = "AIStudyHub.Api";
        const string audience = "AIStudyHub.Client";

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(jwtKey);
        var now = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            }),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(2),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static readonly System.Threading.SemaphoreSlim _seedLock = new System.Threading.SemaphoreSlim(1, 1);

    public async Task SeedDatabaseAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            await db.Database.EnsureCreatedAsync();

            if (!await db.Subscriptions.AnyAsync())
            {
                db.Subscriptions.AddRange(
                    new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, AiPromptLimitPerDay = 5, TotalStorageMb = 50 },
                    new Subscription { TierId = 2, TierName = "Basic", Price = 0, MaxStorageMb = 200, AiPromptLimitPerDay = 20, TotalStorageMb = 200 },
                    new Subscription { TierId = 3, TierName = "Premium", Price = 100000, MaxStorageMb = 500, AiPromptLimitPerDay = 100, TotalStorageMb = 500 }
                );
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _seedLock.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
    }
}

public class TestStudyHubDbContext : StudyHubDbContext
{
    public TestStudyHubDbContext(DbContextOptions<TestStudyHubDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
