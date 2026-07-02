using System.Text.Json;

namespace Hearthaven.Core.Utilities;

/// <summary>
/// JSON 辅助方法 — 安全地从 JSON 字符串中提取值。
/// 避免各工具类重复 try-catch 和 JsonDocument 解析逻辑。
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 从 JSON 字符串中安全提取指定 key 的字符串值。
    /// key 不存在、值类型不对或 JSON 解析失败时返回 null。
    /// </summary>
    public static string? ExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        catch (JsonException)
        {
            // JSON 格式异常，安静返回 null
        }

        return null;
    }

    /// <summary>
    /// 从 JSON 字符串中安全提取指定 key 的整数值。
    /// key 不存在、值类型不对或 JSON 解析失败时返回 null。
    /// </summary>
    public static int? ExtractInt32(string json, string key)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
        }
        catch (JsonException)
        {
            // JSON 格式异常，安静返回 null
        }

        return null;
    }
}
