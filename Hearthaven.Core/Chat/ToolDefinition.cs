using System.Text.Json;

namespace Hearthaven.Core.Chat;

/// <summary>
/// 工具定义 — 在 ChatRequest 中传给 AI 的工具描述。
/// 对应 OpenAI API tools 数组中的每一项。
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonElement Parameters { get; set; }

    /// <summary>
    /// 序列化为 OpenAI API tools 参数格式的 DTO 对象（强类型，避免匿名类型反射）。
    /// </summary>
    public ToolApiObject ToApiObject() => new()
    {
        Type = "function",
        Function = new ToolApiFunction
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };
}

/// <summary>
/// 工具 API 对象 DTO — 对应 OpenAI API tools 数组中的每一项。
/// 强类型替代匿名类型，避免反射开销。
/// </summary>
public sealed record ToolApiObject
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("function")]
    public ToolApiFunction Function { get; init; } = new();
}

/// <summary>
/// 工具函数定义 DTO
/// </summary>
public sealed record ToolApiFunction
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}
