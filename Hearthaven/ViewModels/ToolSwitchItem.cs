using CommunityToolkit.Mvvm.ComponentModel;

namespace Hearthaven.ViewModels;

/// <summary>
/// 工具开关条目 — 设置界面中单个工具的启用/禁用状态。
/// </summary>
public partial class ToolSwitchItem : ObservableObject
{
    /// <summary>工具标识名称（如 read_file）</summary>
    public string ToolName { get; init; } = "";

    /// <summary>工具显示名称（如"读取文件"）</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>工具描述</summary>
    public string Description { get; init; } = "";

    /// <summary>是否启用</summary>
    [ObservableProperty]
    private bool _isEnabled = true;
}
