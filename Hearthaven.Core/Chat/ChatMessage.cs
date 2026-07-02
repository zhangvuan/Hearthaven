namespace Hearthaven.Core.Chat;

/// <summary>
/// 对话中的单条消息
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "user";   // system / user / assistant / tool
    public string Content { get; set; } = "";
    public string? ToolCallId { get; set; }

    /// <summary>
    /// 工具调用列表（仅 assistant 角色使用）。
    /// 强类型替代原有的 object?，消除运行时类型判断和手动 JsonSerializer 操作。
    /// </summary>
    public List<ToolCallEntry>? ToolCalls { get; set; }

    /// <summary>
    /// 思考内容（DeepSeek thinking mode 的 reasoning_content）。
    /// assistant 角色使用，需要透传给 API 以确保上下文完整。
    /// </summary>
    public string? ReasoningContent { get; set; }

    public ChatMessage() { }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public ChatMessage(string role, string content, string? toolCallId)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
    }
}
