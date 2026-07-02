namespace Hearthaven.Core.Tools;

/// <summary>
/// 工具执行结果的结构化展示数据。
/// 由 <see cref="ITool.FormatResult"/> 返回，用于 UI 层展示。
/// 纯数据 record，不依赖任何 WPF 类型。
/// </summary>
public sealed record ToolResultViewData
{
    /// <summary>摘要标签，如 "共 12 项"、"4.0 KB"</summary>
    public string? SummaryTag { get; init; }

    /// <summary>新增行数（绿色 +N 显示）</summary>
    public int LinesAdded { get; init; }

    /// <summary>删除行数（红色 -N 显示）</summary>
    public int LinesRemoved { get; init; }
}
