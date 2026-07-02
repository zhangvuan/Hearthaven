using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hearthaven.Models;

/// <summary>
/// 轮次块 — 助手回复中一轮完整的输出。
/// 包含本轮思考内容、工具调用和文本内容，可复用。
/// 有什么显示什么，没有则不显示。
/// </summary>
public partial class RoundBlock : ObservableObject, ITimelineItem, IDisposable
{
    /// <summary>本轮思考内容（可选）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReasoning))]
    private ReasoningBlock? _reasoning;
    public bool HasReasoning => Reasoning != null;

    /// <summary>本轮工具调用列表（0~N个）</summary>
    public ObservableCollection<ToolCallBlock> ToolCalls { get; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>本轮文本内容（流式更新，最终作为 Markdown 渲染）</summary>
    [ObservableProperty]
    private string _content = "";

    /// <summary>是否正在流式输出（流式时显示纯文本，完成后渲染 Markdown）</summary>
    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>是否既有文本又有工具调用（需要分隔线）</summary>
    public bool HasBothContentAndTools =>
        (!string.IsNullOrEmpty(Content) || IsStreaming) && HasToolCalls;

    /// <summary>是否有实际文本内容</summary>
    public bool HasContent => !string.IsNullOrEmpty(Content);

    /// <summary>是否应显示内容区（有实际内容，或正在流式等待内容）</summary>
    public bool ShouldShowContent => !string.IsNullOrEmpty(Content) || IsStreaming;

    /// <summary>是否已释放</summary>
    private bool _disposed;

    public RoundBlock()
    {
        ToolCalls.CollectionChanged += OnToolCallsCollectionChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ToolCalls.CollectionChanged -= OnToolCallsCollectionChanged;

        Reasoning = null;
        foreach (var tc in ToolCalls)
            tc.Dispose();
        ToolCalls.Clear();

        Content = "";
    }

    private void OnToolCallsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasToolCalls));
        OnPropertyChanged(nameof(HasBothContentAndTools));
    }

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasBothContentAndTools));
        OnPropertyChanged(nameof(ShouldShowContent));
        OnPropertyChanged(nameof(HasContent));
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasBothContentAndTools));
        OnPropertyChanged(nameof(ShouldShowContent));
    }
}
