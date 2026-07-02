namespace Hearthaven.Core.Settings;

/// <summary>工具开关配置</summary>
public class ToolSettings
{
    /// <summary>白名单模式："*" 表示全部启用，或逐个列出工具名称</summary>
    public List<string> Enabled { get; set; } = ["*"];

    /// <summary>显式禁用的工具名称列表（仅在 Enabled 包含 "*" 时生效）</summary>
    public List<string> Disabled { get; set; } = [];
}
