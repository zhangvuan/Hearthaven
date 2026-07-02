using Hearthaven.Core.Data;

namespace Hearthaven.Core.Data;

/// <summary>
/// 消息读取接口 — 查询方法
/// </summary>
public interface IMessageReader
{
    /// <summary>获取指定会话的所有消息（按 ID 正序）</summary>
    Task<List<MessageEntity>> GetBySessionIdAsync(string sessionId);

    /// <summary>分页获取早于 beforeId 的消息（用于历史加载）</summary>
    Task<List<MessageEntity>> GetPagedBeforeAsync(string sessionId, long beforeId, int size);

    /// <summary>获取最新 N 轮（Group）的所有消息</summary>
    Task<List<MessageEntity>> GetLatestGroupsAsync(string sessionId, int groupCount);

    /// <summary>获取某个最大 Id 之前的 N 轮（Group）的所有消息（用于滚动加载更早历史）</summary>
    Task<List<MessageEntity>> GetGroupsBeforeMaxIdAsync(string sessionId, long beforeMaxId, int groupCount);

    /// <summary>获取指定会话的最新 N 条消息</summary>
    Task<List<MessageEntity>> GetLatestAsync(string sessionId, int count);

    /// <summary>获取最新一条消息的内容（用于侧边栏预览）</summary>
    Task<string?> GetLastContentAsync(string sessionId);

    /// <summary>SUM 聚合指定会话的消息 Token 数（利用预存的 TokenCount 字段）</summary>
    Task<long> SumTokenCountAsync(string sessionId);

    /// <summary>获取指定会话中某个 GroupId 的最小（最旧）消息 ID，用于分页边界对齐</summary>
    Task<long?> GetMinIdByGroupIdAsync(string sessionId, string groupId);

    /// <summary>按主键 ID 查询单条消息</summary>
    Task<MessageEntity?> GetByIdAsync(long id);
}

/// <summary>
/// 消息写入接口 — 写入方法
/// </summary>
public interface IMessageWriter
{
    /// <summary>添加单条消息，返回包含自增 ID 的完整实体</summary>
    Task<MessageEntity> AddAsync(MessageEntity message);

    /// <summary>批量添加消息</summary>
    Task AddRangeAsync(List<MessageEntity> messages);

    /// <summary>删除指定会话中某个轮次的所有消息</summary>
    Task DeleteByGroupIdAsync(string sessionId, string groupId);

    /// <summary>清空指定会话的所有消息</summary>
    Task ClearSessionAsync(string sessionId);

    /// <summary>
    /// 删除指定会话中某个轮次的助手消息和工具结果消息（保留用户消息）。
    /// 用于 A5 重新生成：只删除 AI 回复部分，保留用户输入。
    /// </summary>
    Task DeleteAssistantByGroupIdAsync(string sessionId, string groupId);

    /// <summary>更新指定消息的内容（用于 A6 编辑已发送消息）</summary>
    Task UpdateContentAsync(long messageId, string newContent);
}

/// <summary>
/// 消息仓储接口 — 继承读写子接口，保持完整接口约定。
/// 消费者可根据需要依赖 IMessageReader 或 IMessageWriter 以缩小接口。
/// </summary>
public interface IMessageRepository : IMessageReader, IMessageWriter
{
}
