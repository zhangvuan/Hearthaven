using Microsoft.EntityFrameworkCore;

using Hearthaven.Core.Data;

namespace Hearthaven.Data.Database;

/// <summary>
/// DbContext 工厂 — 每次调用创建新实例（EF Core SQLite 不维护物理连接池，无需额外池化）
/// </summary>
public class HearthavenDbFactory : IDbContextFactory<HearthavenDbContext>
{
    private readonly DbContextOptions<HearthavenDbContext> _options;

    public HearthavenDbFactory(DbContextOptions<HearthavenDbContext> options)
    {
        _options = options;
    }

    public HearthavenDbContext CreateDbContext()
    {
        return new HearthavenDbContext(_options);
    }

    public ValueTask<HearthavenDbContext> CreateDbContextAsync()
    {
        return ValueTask.FromResult(new HearthavenDbContext(_options));
    }
}
