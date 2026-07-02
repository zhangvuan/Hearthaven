namespace Hearthaven.Core.Chat;

/// <summary>
/// AI通信接口 — 所有Provider都要实现此接口
/// </summary>
public interface IChatProvider
{
    /// <summary>Provider名称（用于UI显示和配置）</summary>
    string ProviderName { get; }

    /// <summary>
    /// 流式对话（支持工具调用）— 每次 yield 一个 StreamEvent，
    /// 可能是文本块（TextChunk）或工具调用块（ToolCallChunk）。
    /// 流式结束后调用方自行拼接 ToolCallChunk 为完整的 ToolCallData。
    /// </summary>
    IAsyncEnumerable<StreamEvent> StreamChatWithToolsAsync(
        ChatRequest request, CancellationToken ct = default);
}
