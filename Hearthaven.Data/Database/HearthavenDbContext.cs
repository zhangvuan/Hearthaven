using Microsoft.EntityFrameworkCore;

using Hearthaven.Core.Data;

namespace Hearthaven.Data.Database;

/// <summary>
/// 炉心数据库上下文
/// </summary>
public class HearthavenDbContext : DbContext
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    public HearthavenDbContext(DbContextOptions<HearthavenDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title)
                  .IsRequired()
                  .HasMaxLength(256);
            entity.Property(e => e.WorkingDirectory)
                  .HasMaxLength(1024);
            entity.Property(e => e.ModelName)
                  .HasMaxLength(128);
            entity.Property(e => e.Mode)
                  .IsRequired()
                  .HasMaxLength(32)
                  .HasDefaultValue("normal");
            entity.HasIndex(e => e.UpdatedAt);
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired();
            entity.Property(e => e.Role)
                  .IsRequired()
                  .HasMaxLength(32);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.ToolCallId).HasMaxLength(64);
            entity.Property(e => e.GroupId).HasMaxLength(64);

            // 按会话 + 自增主键的联合索引 — 所有消息查询都基于 SessionId + Id 排序/分页
            entity.HasIndex(e => new { e.SessionId, e.Id });

            // 外键关系
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Messages)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
