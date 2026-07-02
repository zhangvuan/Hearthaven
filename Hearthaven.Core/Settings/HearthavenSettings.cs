using System.Text.Json;

namespace Hearthaven.Core.Settings;

/// <summary>
/// 炉心运行时配置实例 — 从 appsettings.json 加载/保存。
/// 通过构造函数注入到各 Service/ViewModel，取代原有的静态 HearthavenConfig。
/// </summary>
public class HearthavenSettings
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ═══════════════════════ 根级平面字段（保持向前兼容） ═══════════════════════

    /// <summary>AI API 地址</summary>
    public string Endpoint { get; set; } = "https://api.deepseek.com";

    /// <summary>API Key（不从配置文件读写，运行时从 Models 列表默认模型的 ApiKey 获取）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>默认模型名称</summary>
    public string DefaultModel { get; set; } = "deepseek-v4-flash";

    /// <summary>请求超时时间（秒）</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>上下文裁剪时，为回复预留的 Token 空间</summary>
    public int MaxResponseTokens { get; set; } = 2000;

    /// <summary>工具调用时 Token 使用率上限（0~1）</summary>
    public double MaxToolTokenRatio { get; set; } = 0.85;

    /// <summary>是否启用调试日志</summary>
    public bool EnableDebugLog { get; set; } = false;

    /// <summary>推理温度（0~2），越高越有创造力</summary>
    public double Temperature { get; set; } = 0.7;

    // ═══════════════════════ 新层级结构 ═══════════════════════

    /// <summary>Agent 人格配置（从 agent.json + SOUL.md + AGENT.md 加载，不序列化到 appsettings.json）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public AgentProfile Agent { get; set; } = new();

    /// <summary>模型配置列表（支持多模型切换）</summary>
    public List<ModelConfig> Models { get; set; } = [];

    /// <summary>UI 设置（主题、字体等）</summary>
    public UiSettings? UI { get; set; }

    /// <summary>工具开关配置</summary>
    public ToolSettings? Tools { get; set; }

    /// <summary>代理配置（预留）</summary>
    public ProxySettings? Proxy { get; set; }

    // ═══════════════════════ 运行时字段（不序列化到文件） ═══════════════════════

    /// <summary>数据库文件路径 — 运行时由 BaseDirectory + "Hearthaven.db" 拼接</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DbPath { get; set; } = "";

    // ═══════════════════════ 文件读写 ═══════════════════════

    /// <summary>从指定目录加载 appsettings.json，若文件不存在则返回默认配置</summary>
    public static HearthavenSettings LoadFromDirectory(string baseDir)
    {
        var settings = LoadFromFile(Path.Combine(baseDir, "appsettings.json"));

        // DbPath 始终由运行时拼接，不从文件读取
        settings.DbPath = Path.Combine(baseDir, "Hearthaven.db");

        return settings;
    }

    /// <summary>保存当前配置到指定目录的 appsettings.json</summary>
    public void SaveToDirectory(string baseDir)
    {
        var path = Path.Combine(baseDir, "appsettings.json");
        SaveToFile(path, this);
    }

    // ═══════════════════════ 内部实现 ═══════════════════════

    /// <summary>将 Settings 实例序列化写入指定路径</summary>
    private static void SaveToFile(string path, HearthavenSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>从指定路径加载配置文件，不存在则创建默认配置</summary>
    internal static HearthavenSettings LoadFromFile(string configPath)
    {
        if (!File.Exists(configPath))
        {
            // 首次运行 → 创建默认配置，用户后续可在设置窗口中修改
            var defaults = CreateDefault();
            SaveToFile(configPath, defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var settings = JsonSerializer.Deserialize<HearthavenSettings>(json)
                           ?? CreateDefault();

            // 向后兼容：如果 Models 为空，从根级平面字段构造默认模型
            if (settings.Models.Count == 0)
            {
                settings.Models.Add(new ModelConfig
                {
                    Name = settings.DefaultModel,
                    Endpoint = settings.Endpoint,
                    ApiKey = settings.ApiKey,
                    MaxContextTokens = null,
                    MaxTokens = null
                });
            }

            return settings;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[HearthavenSettings] 配置文件解析失败: {ex.Message}");
            return CreateDefault();
        }
    }

    private static HearthavenSettings CreateDefault()
    {
        var settings = new HearthavenSettings
        {
            Models =
            [
                new ModelConfig
                {
                    Name = "deepseek-v4-flash",
                    Endpoint = "https://api.deepseek.com",
                    MaxContextTokens = 65536,
                    MaxTokens = 8192
                }
            ]
        };
        return settings;
    }
}
