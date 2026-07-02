using Hearthaven.Core.Data;
using Hearthaven.Diagnostics;
using Hearthaven.Models;
using Hearthaven.Utilities;

namespace Hearthaven.Services;

/// <summary>
/// 会话生命周期服务 — 负责会话的创建、切换、清除和缓存管理。
/// 不持有对 <see cref="ChatViewModel"/> 的任何引用，通过委托和参数解耦。
/// </summary>
public class SessionService
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly SessionCache _cache;

    /// <summary>当前会话 ID</summary>
    public string CurrentSessionId { get; set; } = "";

    /// <summary>当前会话的运行时状态（集中管理所有与会话相关的可变状态）</summary>
    public SessionState? CurrentState { get; set; }

    /// <summary>当前会话标题</summary>
    public string CurrentTitle { get; set; } = "新对话";

    public SessionService(ISessionRepository sessionRepo, IMessageRepository messageRepo, SessionCache cache)
    {
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _cache = cache;
    }

    /// <summary>当前会话切换事件（通知侧边栏更新选中状态）</summary>
    public event Action<string>? SessionChanged;

    /// <summary>供外部触发 SessionChanged 事件（事件只能由声明类内部触发）</summary>
    public void RaiseSessionChanged(string sessionId) => SessionChanged?.Invoke(sessionId);

    // ──────────────── 清除 / 新建会话 ────────────────

    /// <summary>清空当前会话的所有消息（保留会话本身）</summary>
    public async Task ClearChatAsync(SmartObservableCollection<MessageDisplayModel> messages,
        Action onSessionCleared)
    {
        if (string.IsNullOrEmpty(CurrentSessionId)) return;

        _cache.CancelStream(CurrentSessionId);
        await _messageRepo.ClearSessionAsync(CurrentSessionId);

        foreach (var m in messages) m.Dispose();
        messages.Clear();
        _cache.Remove(CurrentSessionId);

        onSessionCleared();
        SessionChanged?.Invoke(CurrentSessionId);
    }

    /// <summary>新建一个空白会话并切换到它（由侧边栏 ✚ 按钮触发）</summary>
    public async Task<string> CreateNewSessionAsync(SmartObservableCollection<MessageDisplayModel> messages,
        long earliestLoadedId, bool loadedAll,
        Action<string> onNewSession)
    {
        // 先取消当前会话的流式生成
        _cache.CancelStream(CurrentSessionId);

        // 保存当前会话消息和分页游标
        if (!string.IsNullOrEmpty(CurrentSessionId))
        {
            _cache.SaveMessages(CurrentSessionId, [.. messages], earliestLoadedId, loadedAll);
        }

        messages.Clear();

        var newId = await _sessionRepo.CreateAsync("新对话");
        CurrentSessionId = newId;
        CurrentState = _cache.GetOrCreate(newId);
        CurrentTitle = "新对话";

        onNewSession(newId);
        SessionChanged?.Invoke(newId);
        return newId;
    }

    /// <summary>切换到指定会话</summary>
    public async Task SwitchSessionAsync(string sessionId,
        SmartObservableCollection<MessageDisplayModel> messages,
        long earliestLoadedId, bool loadedAll,
        Func<string, Task> loadFromCacheAsync,
        Func<Task> loadInitialAsync,
        Func<Task> onSessionLoaded)
    {
        DebugLog.WriteLine(
            $"[C002] SwitchSessionAsync(sessionId={sessionId}, CurrentSessionId={CurrentSessionId}, Messages.Count={messages.Count})");

        if (sessionId == CurrentSessionId || string.IsNullOrEmpty(sessionId))
            return;

        // 保存当前会话的消息列表和分页游标
        if (!string.IsNullOrEmpty(CurrentSessionId))
        {
            _cache.SaveMessages(CurrentSessionId, [.. messages], earliestLoadedId, loadedAll);
        }

        // 清空显示的消息列表
        messages.Clear();

        CurrentSessionId = sessionId;
        CurrentState = _cache.GetOrCreate(sessionId);

        // 加载目标会话标题
        var session = await _sessionRepo.GetByIdAsync(sessionId);
        CurrentTitle = session?.Title ?? "新对话";

        // 优先从缓存加载
        if (_cache.TryGetMessages(sessionId, out var cached))
        {
            foreach (var m in cached!)
                messages.Add(m);

            await loadFromCacheAsync(sessionId);
        }
        else
        {
            await loadInitialAsync();
        }

        await onSessionLoaded();
        SessionChanged?.Invoke(sessionId);
    }

    // ──────────────── 缓存管理 ────────────────

    /// <summary>从缓存中获取指定会话的最后一条助手消息预览</summary>
    public string? GetCachedPreview(string sessionId) => _cache.GetPreview(sessionId);

    /// <summary>清理被删除会话的缓存（取消流 + 释放消息）</summary>
    public void CleanupSessionCache(string sessionId)
    {
        _cache.Remove(sessionId);
    }

    /// <summary>取消指定会话的流式生成</summary>
    public void CancelStream(string sessionId) => _cache.CancelStream(sessionId);

    /// <summary>设置指定会话的流式生成状态</summary>
    public void SetGenerating(string sessionId, bool value) => _cache.SetGenerating(sessionId, value);

    /// <summary>创建流式取消令牌</summary>
    public CancellationTokenSource CreateStreamCts(string sessionId, int timeoutSeconds)
        => _cache.CreateStreamCts(sessionId, timeoutSeconds);

    /// <summary>保存消息快照到缓存</summary>
    public void SaveMessages(string sessionId, SmartObservableCollection<MessageDisplayModel> messages,
        long earliestLoadedId, bool loadedAll)
        => _cache.SaveMessages(sessionId, [.. messages], earliestLoadedId, loadedAll);

    /// <summary>获取或创建会话状态</summary>
    public SessionState GetOrCreateState(string sessionId) => _cache.GetOrCreate(sessionId);

    /// <summary>查询指定会话 ID 是否与当前会话匹配</summary>
    public bool IsCurrentSession(string sessionId) => sessionId == CurrentSessionId;

    /// <summary>恢复会话的分页游标</summary>
    public bool TryGetCursor(string sessionId, out long earliestId, out bool loadedAll)
        => _cache.TryGetCursor(sessionId, out earliestId, out loadedAll);
}
