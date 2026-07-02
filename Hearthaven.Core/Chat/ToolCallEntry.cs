using System.Text.Json.Serialization;

namespace Hearthaven.Core.Chat;

/// <summary>
/// 工具调用条目 DTO — 用于序列化 tool_calls 到 API 请求格式。
/// 强类型替代匿名类型，避免反射和装箱开销。
/// 序列化后格式：{"id":"xxx","type":"function","function":{"name":"xxx","arguments":"xxx"}}
/// </summary>
public sealed record ToolCallEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; init; } = new();
}

/// <summary>
/// 工具调用的函数信息
/// </summary>
public sealed record ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "";
}
