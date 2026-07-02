using System.Text.Json;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;

namespace Hearthaven.Core.Agent;

/// <summary>
/// 消息持久化服务 — 负责增量保存消息到 DB。
/// 从 AgentService 中抽取，遵循单一职责原则。
/// </summary>
public class MessagePersistenceService : IMessagePersistenceService
{
    private readonly IMessageWriter _messageRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly ContextManager _contextManager;

    /// <summary>[B11] 已持久化的消息计数，用于增量保存</summary>
    private int _lastSavedCount;

    public MessagePersistenceService(
        IMessageWriter messageRepo,
        ISessionRepository sessionRepo,
        ContextManager contextManager)
    {
        _messageRepo = messageRepo;
        _sessionRepo = sessionRepo;
        _contextManager = contextManager;
    }

    public void Reset(int startCount = 0)
    {
        _lastSavedCount = startCount;
    }

    /// <summary>
    /// [B11] 增量保存 — 只持久化 contextMessages 中尚未写入 DB 的新消息。
    /// </summary>
    public async Task FlushIncrementalAsync(List<MessageEntity> contextMessages, string sessionId)
    {
        if (contextMessages.Count <= _lastSavedCount) return;

        var newMessages = contextMessages[_lastSavedCount..];

        foreach (var entity in newMessages)
            entity.TokenCount = _contextManager.CountMessageTokens(entity);

        await _messageRepo.AddRangeAsync(newMessages).ConfigureAwait(false);
        await _sessionRepo.TouchAsync(sessionId).ConfigureAwait(false);

        _lastSavedCount = contextMessages.Count;
    }
}
