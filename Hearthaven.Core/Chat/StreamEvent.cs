namespace Hearthaven.Core.Chat;

/// <summary>
/// 流式事件 — 对流式响应中可能出现的不同数据类型的封装。
/// </summary>
public abstract record StreamEvent;

/// <summary>
/// 文本块事件 — 流式响应中的实时文本片段。
/// </summary>
/// <param name="Content">文本内容</param>
public sealed record TextChunk(string Content) : StreamEvent;

/// <summary>
/// 思考内容块事件 — DeepSeek 等模型的 reasoning_content（思考链）。
/// </summary>
/// <param name="Content">思考文本片段</param>
public sealed record ThinkingChunk(string Content) : StreamEvent;

/// <summary>
/// 工具调用块事件 — SSE 中 tool_calls 的分块数据。
/// 同一个 tool_call 的 id / name / arguments 可能分多个 chunk 到达，
/// 接收方需要按 index 分组拼接。
/// </summary>
/// <param name="Index">工具调用的索引（用于分组拼接）</param>
/// <param name="Id">工具调用 ID（可能为 null，直到完整 chunk 到达）</param>
/// <param name="Name">函数名称（可能为 null）</param>
/// <param name="Arguments">函数参数字符串片段（可能为 null，需拼接）</param>
public sealed record ToolCallChunk(int Index, string? Id, string? Name, string? Arguments) : StreamEvent;
