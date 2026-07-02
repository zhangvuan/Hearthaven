using Hearthaven.Models;

namespace Hearthaven.Services;

/// <summary>
/// 单个会话的运行时状态 — 集中管理缓存、流式状态、分页游标。
/// 所有与会话相关的可变状态都应在此，不再分散在多个字典中。
/// </summary>
public class SessionState
{
    public string SessionId { get; }

    // ──────────────── 生成状态 ────────────────

    /// <summary>是否正在生成 AI 回复</summary>
    public bool IsGenerating { get; set; }

    /// <summary>流式 CancellationTokenSource（用于 ⏹ 停止按钮）</summary>
    public CancellationTokenSource? StreamCts { get; set; }

    /// <summary>创建或重建流式 CTS</summary>
    public CancellationTokenSource CreateStreamCts(int timeoutSeconds)
    {
        StreamCts?.Cancel();
        StreamCts?.Dispose();
        StreamCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        return StreamCts;
    }

    /// <summary>取消并清理流式 CTS</summary>
    public void CancelStream()
    {
        StreamCts?.Cancel();
        StreamCts?.Dispose();
        StreamCts = null;
    }

    // ──────────────── 消息缓存 ────────────────

    /// <summary>缓存的消息列表（切换会话时不丢失）</summary>
    public List<MessageDisplayModel>? Messages { get; set; }

    /// <summary>已加载消息中的最小 Id（分页游标）</summary>
    public long EarliestLoadedId { get; set; } = long.MaxValue;

    /// <summary>是否已加载全部历史</summary>
    public bool LoadedAll { get; set; } = true;

    // ──────────────── 预览 ────────────────

    /// <summary>获取最后一条助手消息的预览文本</summary>
    public string? GetPreview()
    {
        if (Messages == null) return null;

        var lastAssistant = Messages.LastOrDefault(m => m.Role == "assistant" && !m.IsStreaming);
        if (lastAssistant == null) return null;

        if (!string.IsNullOrEmpty(lastAssistant.Content))
            return lastAssistant.Content;

        for (int i = lastAssistant.TimelineItems.Count - 1; i >= 0; i--)
        {
            if (lastAssistant.TimelineItems[i] is RoundBlock round && !string.IsNullOrEmpty(round.Content))
                return round.Content;
        }

        return null;
    }

    /// <summary>释放所有消息资源</summary>
    public void DisposeMessages()
    {
        if (Messages == null) return;
        foreach (var m in Messages)
            m.Dispose();
        Messages = null;
    }

    public SessionState(string sessionId)
    {
        SessionId = sessionId;
    }
}
