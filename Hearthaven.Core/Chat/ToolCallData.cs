namespace Hearthaven.Core.Chat;

/// <summary>
/// 完整的工具调用信息 — 流式 SSE 结束后，将分块的 ToolCallChunk 按 index 拼接后得到。
/// </summary>
/// <param name="Id">工具调用 ID（用于匹配 tool 角色的消息）</param>
/// <param name="FunctionName">函数名称</param>
/// <param name="Arguments">完整的参数 JSON</param>
public sealed record ToolCallData(string Id, string FunctionName, string Arguments);
