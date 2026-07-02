namespace Hearthaven.Core.Chat;

/// <summary>
/// 发送给AI的请求参数
/// </summary>
public class ChatRequest
{
    /// <summary>对话消息列表</summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>模型名称</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>最大生成Token数</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>温度参数 (0-2)</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>工具定义列表（用于 Function Calling）</summary>
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>
    /// 工具选择策略：
    /// - "auto" — AI 自主决定是否调用工具
    /// - "none" — 不调用工具
    /// - "{ \"type\": \"function\", \"function\": { \"name\": \"...\" } }" — 强制调用指定工具
    /// </summary>
    public string? ToolChoice { get; set; }
}
