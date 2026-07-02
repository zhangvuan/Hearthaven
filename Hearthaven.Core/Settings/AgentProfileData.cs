namespace Hearthaven.Core.Settings;

/// <summary>
/// agent.json 的序列化数据模型 — 用户在设置界面配置的 Agent 基本信息。
/// </summary>
public class AgentProfileData
{
    /// <summary>Agent 名称（如"萌萌"）</summary>
    public string Name { get; set; } = "萌萌";

    /// <summary>身份（如"女儿"、"助手"、"管家"）</summary>
    public string Identity { get; set; } = "";

    /// <summary>用户称呼（如"爸爸"、"主人"、"先生"）</summary>
    public string CallAs { get; set; } = "";

    /// <summary>头像文件名（如"avatar.png"），相对于用户目录</summary>
    public string Avatar { get; set; } = "avatar.png";
}
