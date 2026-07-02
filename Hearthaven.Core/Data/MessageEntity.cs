namespace Hearthaven.Core.Data;

/// <summary>
/// 消息表
/// </summary>
public class MessageEntity
{
    public long Id { get; set; }

    public string SessionId { get; set; } = "";

    public string Role { get; set; } = "";   // system / user / assistant

    public string Content { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>工具调用 ID（tool 角色消息关联的调用 ID）</summary>
    public string? ToolCallId { get; set; }

    /// <summary>工具调用 JSON（assistant 消息触发的 tool_calls 数组 JSON）</summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>思考内容（DeepSeek thinking mode 的 reasoning_content）</summary>
    public string? ReasoningContent { get; set; }

    /// <summary>轮次分组 ID，用于按轮次删除</summary>
    public string? GroupId { get; set; }

    /// <summary>预计算的 Token 数（保存时由 AgentService 计算，用于快速聚合统计）</summary>
    public int? TokenCount { get; set; }

    /// <summary>
    /// 是否为追加消息（用户在 AI 生成期间发送的补充消息）。
    /// 追加消息与当前轮次共享 GroupId，在 UI 中嵌入到 AI 消息的 TimelineItems 显示。
    /// </summary>
    public bool IsFollowUp { get; set; }

    // 导航属性
    public SessionEntity? Session { get; set; }
}
