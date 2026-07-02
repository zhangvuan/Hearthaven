using Hearthaven.Core.Data;

namespace Hearthaven.Core.Agent;

/// <summary>
/// 消息持久化服务接口 — 负责消息的增量保存。
/// </summary>
public interface IMessagePersistenceService
{
    /// <summary>重置增量保存计数（新对话开始时调用）</summary>
    /// <param name="startCount">已有 persistedUserMsg 时为 1，否则 0</param>
    void Reset(int startCount = 0);

    /// <summary>增量保存尚未写入 DB 的新消息</summary>
    Task FlushIncrementalAsync(List<MessageEntity> contextMessages, string sessionId);
}
