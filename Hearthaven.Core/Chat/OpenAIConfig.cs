namespace Hearthaven.Core.Chat;

/// <summary>
/// OpenAI兼容API的配置
/// </summary>
public class OpenAIConfig
{
    /// <summary>API地址 (默认OpenAI官方，可改成本地地址如 http://localhost:11434/v1 )</summary>
    public string Endpoint { get; set; } = "https://api.openai.com/v1";

    /// <summary>API Key</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>模型名称 (可被ChatRequest中的Model覆盖)</summary>
    public string DefaultModel { get; set; } = "gpt-4o-mini";
}
