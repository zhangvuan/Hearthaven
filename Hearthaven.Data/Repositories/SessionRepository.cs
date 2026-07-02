using Hearthaven.Core.Data;
using Hearthaven.Data.Database;
using Microsoft.EntityFrameworkCore;

namespace Hearthaven.Data.Repositories;

/// <summary>
/// 会话仓储实现
/// </summary>
public class SessionRepository : ISessionRepository
{
    private readonly IDbContextFactory<HearthavenDbContext> _dbFactory;

    public SessionRepository(IDbContextFactory<HearthavenDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<SessionEntity?> GetByIdAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions.FindAsync(id);
    }

    public async Task<List<SessionEntity>> GetAllOrderByCreatedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SessionWithPreview>> GetAllWithPreviewAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SessionWithPreview(
                s.Id,
                s.Title,
                s.UpdatedAt,
                s.Messages.OrderByDescending(m => m.Id).Select(m => m.Content).FirstOrDefault()))
            .ToListAsync();
    }

    public async Task<SessionEntity?> GetLatestAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<string> CreateAsync(string title = "新对话")
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = new SessionEntity { Id = Guid.NewGuid().ToString("N"), Title = title };
        db.Sessions.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateTitleAsync(string id, string title)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(id);
        if (session != null)
        {
            session.Title = title;
            await db.SaveChangesAsync();
        }
    }

    public async Task TouchAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(id);
        if (session != null)
        {
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdatePropertiesAsync(string id, string? workingDirectory = null, string? modelName = null, string? mode = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(id);
        if (session == null) return;

        if (workingDirectory != null)
            session.WorkingDirectory = workingDirectory;
        if (modelName != null)
            session.ModelName = modelName;
        if (mode != null)
            session.Mode = mode;

        session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(id);
        if (session != null)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync();
        }
    }
}
