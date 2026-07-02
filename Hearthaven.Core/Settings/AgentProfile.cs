namespace Hearthaven.Core.Settings;

/// <summary>
/// Agent 人格配置 — 运行时聚合对象。
/// 数据来自 agent.json / SOUL.md / AGENT.md，由 <see cref="UserProfileManager.ReadProfile"/> 构建。
/// 后续可扩展为列表以支持多 Agent。
/// </summary>
public class AgentProfile
{
    /// <summary>Agent 名称（如"萌萌"）</summary>
    public string Name { get; set; } = "炉心";

    /// <summary>身份关系（如"女儿"、"助手"、"管家"），注入到 System Prompt</summary>
    public string Identity { get; set; } = "";

    /// <summary>用户称呼（如"爸爸"、"主人"、"先生"），注入到 System Prompt</summary>
    public string CallAs { get; set; } = "";

    /// <summary>头像文件名（如"avatar.png"），相对于用户目录 <see cref="UserProfileManager.GetDirectory"/></summary>
    public string? AvatarFileName { get; set; }

    /// <summary>性格描述 — 从 SOUL.md 读取，注入到 System Prompt</summary>
    public string? Personality { get; set; }

    /// <summary>自定义 prompt 后缀 — 从 AGENT.md 读取，追加到 System Prompt 末尾</summary>
    public string? CustomPromptSuffix { get; set; }
}
