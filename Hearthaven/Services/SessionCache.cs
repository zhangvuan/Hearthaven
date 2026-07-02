using Hearthaven.Diagnostics;
using Hearthaven.Models;

namespace Hearthaven.Services;

/// <summary>
/// 会话缓存 — 独立管理多会话的运行时状态、缓存和流式生命周期。
/// 内部使用 <see cref="SessionState"/> 集中管理每个会话的所有状态。
/// 通过构造函数注入到 ChatViewModel。
/// 支持 LRU 淘汰：超过 <see cref="_maxCachedSessions"/> 上限时，淘汰最久未访问的会话。
/// </summary>
public class SessionCache
{
    /// <summary>最多缓存多少个会话的运行时状态</summary>
    private const int _maxCachedSessions = 50;

    /// <summary>所有会话的状态字典（sessionId → SessionState）</summary>
    private readonly Dictionary<string, SessionState> _states = new();

    /// <summary>LRU 访问顺序链表（队首 = 最近访问，队尾 = 最久未访问）</summary>
    private readonly LinkedList<string> _accessOrder = new();

    // ──────────────── 状态获取 ────────────────

    /// <summary>获取或创建指定会话的状态对象</summary>
    public SessionState GetOrCreate(string sessionId)
    {
        if (!_states.TryGetValue(sessionId, out var state))
        {
            // 达到上限 → 淘汰最久未访问的会话
            EvictIfNeeded();

            state = new SessionState(sessionId);
            _states[sessionId] = state;
            _accessOrder.AddFirst(sessionId);
        }
        else
        {
            // 已存在 → 移到队首（最近访问）
            Promote(sessionId);
        }
        return state;
    }

    /// <summary>尝试获取指定会话的状态对象</summary>
    public bool TryGet(string sessionId, out SessionState? state)
    {
        if (_states.TryGetValue(sessionId, out state))
        {
            Promote(sessionId);
            return true;
        }
        return false;
    }

    // ──────────────── 生成状态 ────────────────

    /// <summary>设置指定会话的流式生成状态</summary>
    public void SetGenerating(string sessionId, bool value)
    {
        GetOrCreate(sessionId).IsGenerating = value;
    }

    /// <summary>查询指定会话是否正在流式生成</summary>
    public bool IsGenerating(string sessionId)
    {
        return _states.TryGetValue(sessionId, out var state) && state.IsGenerating;
    }

    // ──────────────── 流式 CTS ────────────────

    /// <summary>为指定会话创建流式 CancellationTokenSource</summary>
    public CancellationTokenSource CreateStreamCts(string sessionId, int timeoutSeconds)
    {
        return GetOrCreate(sessionId).CreateStreamCts(timeoutSeconds);
    }

    /// <summary>取消并清理指定会话的流式令牌</summary>
    public void CancelStream(string sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
            state.CancelStream();
    }

    // ──────────────── 消息缓存 ────────────────

    /// <summary>保存某会话的消息列表快照和分页游标</summary>
    public void SaveMessages(string sessionId, List<MessageDisplayModel> messages, long earliestId, bool loadedAll)
    {
        var state = GetOrCreate(sessionId);
        state.Messages = messages;
        state.EarliestLoadedId = earliestId;
        state.LoadedAll = loadedAll;
    }

    /// <summary>尝试恢复某会话的消息缓存</summary>
    public bool TryGetMessages(string sessionId, out List<MessageDisplayModel>? messages)
    {
        messages = null;
        if (_states.TryGetValue(sessionId, out var state))
        {
            messages = state.Messages;
            return messages != null;
        }
        return false;
    }

    /// <summary>尝试恢复某会话的分页游标</summary>
    public bool TryGetCursor(string sessionId, out long earliestId, out bool loadedAll)
    {
        earliestId = long.MaxValue;
        loadedAll = true;
        if (_states.TryGetValue(sessionId, out var state))
        {
            earliestId = state.EarliestLoadedId;
            loadedAll = state.LoadedAll;
            return true;
        }
        return false;
    }

    // ──────────────── 预览 ────────────────

    /// <summary>获取指定会话的最后一条助手消息预览</summary>
    public string? GetPreview(string sessionId)
    {
        return _states.TryGetValue(sessionId, out var state) ? state.GetPreview() : null;
    }

    // ──────────────── 综合操作 ────────────────

    /// <summary>移除指定会话的所有缓存（消息 + 流 + 标志 + 游标）</summary>
    public void Remove(string sessionId)
    {
        if (_states.TryGetValue(sessionId, out var state))
        {
            state.CancelStream();
            state.DisposeMessages();
            _states.Remove(sessionId);
            _accessOrder.Remove(sessionId);
        }
    }

    /// <summary>清理所有会话的缓存（释放消息对象）</summary>
    public void ClearAll()
    {
        foreach (var state in _states.Values)
        {
            state.CancelStream();
            state.DisposeMessages();
        }
        _states.Clear();
        _accessOrder.Clear();
    }

    /// <summary>当前缓存的会话数量（仅用于诊断/测试）</summary>
    internal int CachedCount => _states.Count;

    // ──────────────── LRU 辅助方法 ────────────────

    /// <summary>将指定会话移到访问顺序链表的队首</summary>
    private void Promote(string sessionId)
    {
        if (_accessOrder.First?.Value == sessionId)
            return; // 已经在队首，无需操作

        _accessOrder.Remove(sessionId);
        _accessOrder.AddFirst(sessionId);
    }

    /// <summary>如果达到缓存上限，淘汰最久未访问的会话</summary>
    private void EvictIfNeeded()
    {
        var checkedCount = 0;
        while (_accessOrder.Count >= _maxCachedSessions)
        {
            var last = _accessOrder.Last;
            if (last == null) break;

            var evictId = last.Value;

            // [B4] 跳过正在生成的会话，避免淘汰后悬空引用导致 NullReferenceException
            if (_states.TryGetValue(evictId, out var state) && state.IsGenerating)
            {
                // 移到队首保护，继续检查下一个
                _accessOrder.RemoveLast();
                _accessOrder.AddFirst(evictId);

                checkedCount++;
                // 所有会话都在生成中 → 放弃淘汰
                if (checkedCount >= _accessOrder.Count)
                    break;
                continue;
            }

            DebugLog.WriteLine($"[SessionCache] LRU 淘汰会话: {evictId}");

            state?.CancelStream();
            state?.DisposeMessages();
            _states.Remove(evictId);
            _accessOrder.RemoveLast();
        }
    }
}
