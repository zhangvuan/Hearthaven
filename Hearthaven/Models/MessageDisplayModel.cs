using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;

namespace Hearthaven.Models;

/// <summary>
/// 单条消息的显示模型 — 包含 UI 展示状态和交互命令。
/// 非纯 ViewModel，而是每个消息气泡的 DisplayModel。
/// </summary>
public partial class MessageDisplayModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _role = "user";   // system / user / assistant

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegenerate))]
    private string _content = "";

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegenerate))]
    private bool _isStreaming;

    /// <summary>
    /// 时间线条目列表 — 按时间线顺序混合存放思考块和工具调用块。
    /// 例如：[思考块1] → [工具调用1] → [思考块2] → [文本回复]
    /// </summary>
    public ObservableCollection<ITimelineItem> TimelineItems { get; } = [];

    /// <summary>
    /// 轮次 ID — 同一轮对话（用户消息+后续助手回复）共享同一个 GroupId。
    /// 用于按轮次删除消息。
    /// </summary>
    [ObservableProperty]
    private string? _groupId;

    /// <summary>对应的数据库消息 ID（0 表示尚未保存到 DB）</summary>
    public long MessageId { get; set; }

    // ──────────────── 复制功能 ────────────────

    [ObservableProperty]
    private string _copyButtonText = "📋 复制";

    private CancellationTokenSource? _copyDelayCts;

    /// <summary>获取适合复制到剪贴板的完整文本内容</summary>
    public string ContentForClipboard
    {
        get
        {
            if (!string.IsNullOrEmpty(Content))
                return Content;

            // 从 TimelineItems 中收集所有 RoundBlock 的文本
            var sb = new StringBuilder();
            foreach (var item in TimelineItems)
            {
                if (item is RoundBlock round && !string.IsNullOrEmpty(round.Content))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(round.Content);
                }
            }
            return sb.ToString();
        }
    }

    [RelayCommand]
    private async Task Copy()
    {
        var text = ContentForClipboard;
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard 操作在特殊环境下可能失败，静默忽略
            return;
        }

        CopyButtonText = "✅ 已复制";

        // 取消前一个延时，防止多次快速点击导致按钮文字在预期之前恢复
        _copyDelayCts?.Cancel();
        _copyDelayCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(1500, _copyDelayCts.Token).ConfigureAwait(true);
            CopyButtonText = "📋 复制";
        }
        catch (TaskCanceledException)
        {
            // 被下次复制取消，忽略
        }
    }

    // ──────────────── A5 重新生成回复 ────────────────

    /// <summary>重新生成回调（由 ChatViewModel 在创建气泡时设置）</summary>
    public Func<Task>? RegenerateCallback { get; set; }

    /// <summary>是否可重新生成（仅助手消息、非流式、非错误）</summary>
    public bool CanRegenerate => Role == "assistant" && !IsStreaming && !HasError;

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (RegenerateCallback != null)
            await RegenerateCallback();
    }

    // ──────────────── A6 编辑已发送消息 ────────────────

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editingContent = "";

    [RelayCommand]
    private void StartEdit()
    {
        if (Role != "user") return;
        EditingContent = Content;
        IsEditing = true;
    }

    /// <summary>保存编辑回调（由 ChatViewModel 在创建气泡时设置）</summary>
    public Func<Task>? SaveEditCallback { get; set; }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (string.IsNullOrEmpty(EditingContent?.Trim())) return;
        Content = EditingContent;
        IsEditing = false;
        if (SaveEditCallback != null)
            await SaveEditCallback();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    // ──────────────── A7 错误重试 ────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(CanRegenerate))]
    private bool _isError;

    /// <summary>是否有错误（由错误处理逻辑显式设置，不依赖内容前缀判断）</summary>
    public bool HasError => IsError;

    /// <summary>重试回调（由 ChatViewModel 在创建错误气泡时设置）</summary>
    public Func<Task>? RetryCallback { get; set; }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (RetryCallback != null)
            await RetryCallback();
    }

    public MessageDisplayModel()
    {
    }

    public MessageDisplayModel(string role, string content)
    {
        _role = role;
        _content = content;
    }

    public void Dispose()
    {
        foreach (var item in TimelineItems)
        {
            if (item is RoundBlock round)
                round.Dispose();
        }
        TimelineItems.Clear();

        // 清空大字符串，加速 GC 回收
        Content = "";
    }
}
