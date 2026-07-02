using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Hearthaven.Core.Data;

namespace Hearthaven.Data.Database;

/// <summary>
/// 设计时 DbContext 工厂（供 dotnet ef 迁移命令使用）
/// </summary>
public class HearthavenDbContextFactory : IDesignTimeDbContextFactory<HearthavenDbContext>
{
    public HearthavenDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HearthavenDbContext>()
            .UseSqlite("Data Source=Hearthaven.db")
            .Options;

        return new HearthavenDbContext(options);
    }
}
