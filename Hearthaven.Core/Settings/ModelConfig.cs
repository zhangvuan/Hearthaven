namespace Hearthaven.Core.Settings;

/// <summary>
/// 单模型配置 — 每个实例代表一个可用模型。
/// 各字段可覆盖 HearthavenSettings 根级同名配置。
/// </summary>
public class ModelConfig
{
    /// <summary>模型标识（如 "deepseek-v4-flash"、"gpt-4o"），API 调用时使用的名称</summary>
    public string Name { get; set; } = "";

    /// <summary>显示名称（用户友好名称），为空时回退到 <see cref="Name"/></summary>
    public string? DisplayName { get; set; }

    /// <summary>服务商类型（如 "OpenAI Compatible"、"Azure"）</summary>
    public string Provider { get; set; } = "OpenAI Compatible";

    /// <summary>API 地址（覆盖根级 Endpoint）</summary>
    public string? Endpoint { get; set; }

    /// <summary>API Key（覆盖根级 ApiKey，优先使用模型专属 Key）</summary>
    public string? ApiKey { get; set; }

    /// <summary>最大上下文 Token（覆盖根级 MaxContextTokens）</summary>
    public int? MaxContextTokens { get; set; }

    /// <summary>最大输出 Token（覆盖根级 MaxTokens）</summary>
    public int? MaxTokens { get; set; }
}
