using System.Text.Json;

namespace Hearthaven.Core.Chat;

/// <summary>
/// JSON 序列化选项 — OpenAI SSE 流式场景下统一使用 SnakeCase 命名策略。
/// </summary>
public static class SseJsonContext
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
}
