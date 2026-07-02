using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hearthaven.Core.Tools;

namespace Hearthaven.Models;

/// <summary>
/// 工具调用块 — 时间线中的一次工具调用事件，支持折叠/展开显示。
/// 自动从执行结果中解析摘要标签（如文件大小、行数等）。
/// </summary>
public partial class ToolCallBlock : ObservableObject, IDisposable
{
    /// <summary>工具调用 ID（用于精确匹配 tool 消息的执行结果）</summary>
    public string? ToolCallId { get; set; }

    /// <summary>对应的工具实例（由 ChatViewModel 在创建时注入）</summary>
    public ITool? Tool { get; set; }

    /// <summary>独立的取消令牌源 — 用户点击"中止"时取消此工具的单独执行</summary>
    [ObservableProperty]
    private CancellationTokenSource? _cts;

    /// <summary>Cts 变化时刷新中止按钮的可见性和可用性</summary>
    partial void OnCtsChanged(CancellationTokenSource? value)
    {
        OnPropertyChanged(nameof(HasCancelButton));
        CancelToolCommand.NotifyCanExecuteChanged();
    }

    /// <summary>DisplayTitle 的缓存值，惰性计算、每次 ToolName/ArgumentsJson 变化时失效</summary>
    private string? _cachedDisplayTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _toolName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _argumentsJson = "";

    [ObservableProperty]
    private string _result = "";

    /// <summary>是否正在执行中（流式期间显示"正在调用"）</summary>
    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>是否展开显示完整结果</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否执行出错</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>是否执行警告（如编辑工具未匹配到文本）</summary>
    [ObservableProperty]
    private bool _isWarning;

    /// <summary>是否执行成功</summary>
    [ObservableProperty]
    private bool _isSuccess;

    /// <summary>摘要标签，如 "共12项"、"4.0 KB"</summary>
    [ObservableProperty]
    private string _summaryTag = "";

    /// <summary>新增行数（用于 write_file/edit_file 绿色显示）</summary>
    [ObservableProperty]
    private int _linesAdded;

    /// <summary>删除行数（用于 edit_file 红色显示）</summary>
    [ObservableProperty]
    private int _linesRemoved;

    /// <summary>是否有行数变更信息</summary>
    public bool HasLinesChanged => LinesAdded > 0 || LinesRemoved > 0;

    /// <summary>是否有新增行</summary>
    public bool HasLinesAdded => LinesAdded > 0;

    /// <summary>是否有删除行</summary>
    public bool HasLinesRemoved => LinesRemoved > 0;

    partial void OnLinesAddedChanged(int value)
    {
        OnPropertyChanged(nameof(HasLinesChanged));
        OnPropertyChanged(nameof(HasLinesAdded));
    }

    partial void OnLinesRemovedChanged(int value)
    {
        OnPropertyChanged(nameof(HasLinesChanged));
        OnPropertyChanged(nameof(HasLinesRemoved));
    }

    /// <summary>
    /// 友好显示标题，如 "查看文件 [E:\CSHAP\Hearthaven]" 或 "计算 [(15+3)*2]"
    /// 首次计算后缓存结果，后续属性变更时自动失效。
    /// 完全委托给 ITool.GetDisplayTitle 生成，ToolCallBlock 不做任何工具相关的判断。
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            if (_cachedDisplayTitle != null)
                return _cachedDisplayTitle;

            _cachedDisplayTitle = ComputeDisplayTitle();
            return _cachedDisplayTitle;
        }
    }

    /// <summary>ToolName 变化时清空标题缓存</summary>
    partial void OnToolNameChanged(string value) => _cachedDisplayTitle = null;

    /// <summary>ArgumentsJson 变化时清空标题缓存</summary>
    partial void OnArgumentsJsonChanged(string value) => _cachedDisplayTitle = null;

    /// <summary>计算 DisplayTitle（完全委托给 ITool，首次访问时缓存）</summary>
    private string ComputeDisplayTitle()
    {
        // 优先委托给工具自身生成标题，Tool 为 null 时回退 ToolName
        return Tool?.GetDisplayTitle(ArgumentsJson) ?? ToolName;
    }

    /// <summary>
    /// 应用工具执行结果。
    /// 在 ChatViewModel 收到 OnToolCallEnd 后调用。
    /// 完全委托给 ITool.FormatResult 解析展示数据，ToolCallBlock 不做任何工具相关的判断。
    /// </summary>
    public void ApplyResult(string result, bool isError, bool isWarning = false)
    {
        Result = result;
        IsError = isError;
        IsWarning = isWarning && !isError; // Warning 不覆盖 Error
        IsSuccess = !isError && !isWarning;

        if (Tool != null)
        {
            var viewData = Tool.FormatResult(result);
            SummaryTag = viewData.SummaryTag ?? "";
            LinesAdded = viewData.LinesAdded;
            LinesRemoved = viewData.LinesRemoved;
        }
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>是否可显示中止按钮（执行中且可取消）</summary>
    public bool HasCancelButton => IsExecuting && Cts != null;

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasCancelButton));
        CancelToolCommand.NotifyCanExecuteChanged();
    }

    /// <summary>中止此工具的执行</summary>
    [RelayCommand(CanExecute = nameof(CanCancelTool))]
    private void CancelTool()
    {
        if (Cts?.IsCancellationRequested == false)
        {
            Cts.Cancel();
            // 取消后结果设为"用户中止"，标记为 Warning
            ApplyResult("⚠️ 用户中止了此任务", isError: false, isWarning: true);
        }
    }

    private bool CanCancelTool() => IsExecuting && Cts != null && !Cts.IsCancellationRequested;

    /// <summary>已释放标志</summary>
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 释放独立取消令牌源
        Cts?.Dispose();
        Cts = null;

        // 释放大字符串（如文件内容可能几MB）
        Result = "";
        ArgumentsJson = "";
        _cachedDisplayTitle = null;
        SummaryTag = "";
        LinesAdded = 0;
        LinesRemoved = 0;
    }
}
