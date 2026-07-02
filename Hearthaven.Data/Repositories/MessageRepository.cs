using Hearthaven.Core.Data;
using Hearthaven.Data.Database;
using Microsoft.EntityFrameworkCore;

namespace Hearthaven.Data.Repositories;

/// <summary>
/// 消息仓储实现
/// </summary>
public class MessageRepository : IMessageRepository
{
    private readonly IDbContextFactory<HearthavenDbContext> _dbFactory;

    public MessageRepository(IDbContextFactory<HearthavenDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<MessageEntity> AddAsync(MessageEntity message)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    public async Task AddRangeAsync(List<MessageEntity> messages)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Messages.AddRange(messages);
        await db.SaveChangesAsync();
    }

    public async Task<List<MessageEntity>> GetBySessionIdAsync(string sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<MessageEntity>> GetPagedBeforeAsync(string sessionId, long beforeId, int size)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId && m.Id < beforeId)
            .OrderByDescending(m => m.Id)
            .Take(size)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<MessageEntity>> GetLatestAsync(string sessionId, int count)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Take(count)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 获取最新 N 轮（Group）的所有消息。
    /// 按组内最大 Id 降序取最新的 N 个 Group，返回这些 Group 的全部消息（按 Id 升序）。
    /// </summary>
    public async Task<List<MessageEntity>> GetLatestGroupsAsync(string sessionId, int groupCount)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Step 1: 查出最新的 N 个 GroupId（按组内最大 Id 降序）
        var latestGroupIds = await db.Messages
            .Where(m => m.SessionId == sessionId
                && m.GroupId != null && m.GroupId != "" && m.GroupId != "_null_")
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, MaxId = g.Max(m => m.Id) })
            .OrderByDescending(x => x.MaxId)
            .Take(groupCount)
            .Select(x => x.GroupId)
            .ToListAsync();

        if (latestGroupIds.Count == 0)
            return [];

        // Step 2: 取这些 Group 的全部消息
        return await db.Messages
            .Where(m => m.SessionId == sessionId && latestGroupIds.Contains(m.GroupId))
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 获取某个最大 Id 之前的 N 轮（Group）的所有消息（滚动加载更早历史）。
    /// 找出所有 MAX(Id) &lt; beforeMaxId 的 Group，取最新的 N 个，返回全部消息（按 Id 升序）。
    /// </summary>
    public async Task<List<MessageEntity>> GetGroupsBeforeMaxIdAsync(string sessionId, long beforeMaxId, int groupCount)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Step 1: 查出 MAX(Id) < beforeMaxId 的最新 N 个 GroupId
        var groupIds = await db.Messages
            .Where(m => m.SessionId == sessionId
                && m.GroupId != null && m.GroupId != "" && m.GroupId != "_null_")
            .GroupBy(m => m.GroupId)
            .Select(g => new { GroupId = g.Key, MaxId = g.Max(m => m.Id) })
            .Where(x => x.MaxId < beforeMaxId)
            .OrderByDescending(x => x.MaxId)
            .Take(groupCount)
            .Select(x => x.GroupId)
            .ToListAsync();

        if (groupIds.Count == 0)
            return [];

        // Step 2: 取这些 Group 的全部消息
        return await db.Messages
            .Where(m => m.SessionId == sessionId && groupIds.Contains(m.GroupId))
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    public async Task DeleteByGroupIdAsync(string sessionId, string groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var toDelete = await db.Messages
            .Where(m => m.SessionId == sessionId && m.GroupId == groupId)
            .ToListAsync();
        db.Messages.RemoveRange(toDelete);
        await db.SaveChangesAsync();
    }

    public async Task ClearSessionAsync(string sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var messages = await db.Messages
            .Where(m => m.SessionId == sessionId)
            .ToListAsync();
        db.Messages.RemoveRange(messages);
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetLastContentAsync(string sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Content)
            .FirstOrDefaultAsync();
    }

    public async Task<long> SumTokenCountAsync(string sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId && m.TokenCount != null)
            .SumAsync(m => (long)m.TokenCount!);
    }

    public async Task<long?> GetMinIdByGroupIdAsync(string sessionId, string groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages
            .Where(m => m.SessionId == sessionId && m.GroupId == groupId)
            .MinAsync(m => (long?)m.Id);
    }

    public async Task<MessageEntity?> GetByIdAsync(long id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Messages.FindAsync(id);
    }

    public async Task DeleteAssistantByGroupIdAsync(string sessionId, string groupId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var toDelete = await db.Messages
            .Where(m => m.SessionId == sessionId && m.GroupId == groupId
                && (m.Role == "assistant" || m.Role == "tool"))
            .ToListAsync();
        db.Messages.RemoveRange(toDelete);
        await db.SaveChangesAsync();
    }

    public async Task UpdateContentAsync(long messageId, string newContent)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var msg = await db.Messages.FindAsync(messageId);
        if (msg != null)
        {
            msg.Content = newContent;
            // 编辑不修改 Timestamp，避免影响消息排序（按 Id 排序不会受 Timestamp 影响，此处为双重保险）
            await db.SaveChangesAsync();
        }
    }
}
