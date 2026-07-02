using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hearthaven.Models;

/// <summary>
/// 单次思考块（Agent Loop 中每次独立的思考过程），支持折叠显示。
/// </summary>
public partial class ReasoningBlock : ObservableObject
{
    /// <summary>是否正在思考中（true=⏳思考中，false=💭已思考）</summary>
    [ObservableProperty]
    private bool _isThinking = true;

    [ObservableProperty]
    private string _content = "";

    /// <summary>是否展开显示完整思考内容（默认折叠）</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 折叠时显示的摘要（取内容前 80 个字符）
    /// </summary>
    public string Summary
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return "(空)";

            // 去掉首尾空白，取前 80 个字符
            var trimmed = Content.Trim();
            return trimmed.Length <= 80
                ? trimmed
                : trimmed[..80] + "…";
        }
    }

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
