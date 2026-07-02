using CommunityToolkit.Mvvm.ComponentModel;

namespace Hearthaven.ViewModels;

/// <summary>
/// 侧边栏中的单条会话条目
/// </summary>
public partial class SessionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _title = "新对话";

    /// <summary>最后一条消息的预览文本</summary>
    [ObservableProperty]
    private string? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedAtDisplay))]
    private DateTime _updatedAt;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>格式化的更新时间</summary>
    public string UpdatedAtDisplay => UpdatedAt.ToLocalTime().ToString("MM/dd HH:mm");
}
