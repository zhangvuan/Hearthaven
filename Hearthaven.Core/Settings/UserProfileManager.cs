using System.Text.Json;

namespace Hearthaven.Core.Settings;

/// <summary>
/// 用户数据目录管理器 — 管理 %APPDATA%\Hearthaven\ 目录下的所有用户数据。
/// 包括 agent.json / SOUL.md / AGENT.md / avatar.png 的读写。
/// </summary>
public static class UserProfileManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>获取用户数据目录路径（%APPDATA%\Hearthaven\）</summary>
    public static string GetDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Hearthaven");
    }

    /// <summary>确保用户目录和默认文件存在，返回目录路径</summary>
    public static string EnsureDirectory()
    {
        var dir = GetDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        EnsureDefaultFiles(dir);
        return dir;
    }

    /// <summary>首次运行时创建默认模板文件</summary>
    private static void EnsureDefaultFiles(string dir)
    {
        // agent.json
        var agentJsonPath = Path.Combine(dir, "agent.json");
        if (!File.Exists(agentJsonPath))
        {
            var defaultAgent = new AgentProfileData
            {
                Name = "萌萌",
                Identity = "女儿",
                CallAs = "爸爸",
                Avatar = "avatar.png"
            };
            File.WriteAllText(agentJsonPath, JsonSerializer.Serialize(defaultAgent, _jsonOptions));
        }

        // SOUL.md
        var soulPath = Path.Combine(dir, "SOUL.md");
        if (!File.Exists(soulPath))
        {
            File.WriteAllText(soulPath, "# 性格设定\n\n在这里写下角色的性格特征和行为风格……\n");
        }

        // AGENT.md
        var agentMdPath = Path.Combine(dir, "AGENT.md");
        if (!File.Exists(agentMdPath))
        {
            File.WriteAllText(agentMdPath, "# 额外行为指令\n\n此内容将追加到系统提示词末尾，可用于添加额外的行为规则或约束。\n");
        }
    }

    /// <summary>读取 agent.json</summary>
    public static AgentProfileData ReadAgentJson()
    {
        var path = Path.Combine(GetDirectory(), "agent.json");
        if (!File.Exists(path))
            return new AgentProfileData();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AgentProfileData>(json) ?? new AgentProfileData();
        }
        catch
        {
            return new AgentProfileData();
        }
    }

    /// <summary>写入 agent.json</summary>
    public static void WriteAgentJson(AgentProfileData data)
    {
        var dir = GetDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "agent.json");
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>读取 SOUL.md 内容</summary>
    public static string? ReadSoulMd()
    {
        var path = Path.Combine(GetDirectory(), "SOUL.md");
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path)?.Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    /// <summary>读取 AGENT.md 内容</summary>
    public static string? ReadAgentMd()
    {
        var path = Path.Combine(GetDirectory(), "AGENT.md");
        if (!File.Exists(path)) return null;
        var content = File.ReadAllText(path)?.Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    /// <summary>聚合读取完整 AgentProfile（含 SOUL.md 和 AGENT.md）</summary>
    public static AgentProfile ReadProfile()
    {
        var data = ReadAgentJson();
        return new AgentProfile
        {
            Name = data.Name,
            Identity = data.Identity,
            CallAs = data.CallAs,
            AvatarFileName = data.Avatar,
            Personality = ReadSoulMd(),
            CustomPromptSuffix = ReadAgentMd()
        };
    }

    /// <summary>获取头像文件完整路径（不存在则返回 null）</summary>
    public static string? GetAvatarPath()
    {
        var data = ReadAgentJson();
        var path = Path.Combine(GetDirectory(), data.Avatar);
        return File.Exists(path) ? path : null;
    }
}
