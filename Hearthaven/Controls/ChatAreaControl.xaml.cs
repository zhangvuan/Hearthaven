using Hearthaven.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Hearthaven.Controls;

/// <summary>
/// 对话区域控件 — 包含标题栏、消息列表和输入区。
/// 依赖 DataContext 继承自父窗口（ChatViewModel）。
/// </summary>
public partial class ChatAreaControl : UserControl
{
    private bool _isLoadingMore;

    private ChatViewModel ViewModel => (ChatViewModel)DataContext;

    public ChatAreaControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // MarkdownViewer 内部 RichTextBox 拦截鼠标滚轮 → 转发到列表滚动
        MessageScrollViewer.AddHandler(
            UIElement.MouseWheelEvent,
            new MouseWheelEventHandler(OnMessageScrollViewerMouseWheel),
            true);

        // 初始化完成后滚动到底部（延迟到布局完成后执行）
        _ = Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToBottom());
    }

    /// <summary>MarkdownViewer 滚轮事件转发</summary>
    private static void OnMessageScrollViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled)
            return;

        var scroll = (ScrollViewer)sender;
        if (e.Delta > 0)
            scroll.LineUp();
        else
            scroll.LineDown();

        e.Handled = true;
    }

    // ──────────────── 输入框事件 ────────────────

    /// <summary>Enter 发送，Shift+Enter 换行，Ctrl+↑/↓ 浏览输入历史</summary>
    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            if (ViewModel.CanSendEnabled)
                _ = ViewModel.SendAsync();
            return;
        }

        if (e.Key == Key.Up && Keyboard.IsKeyDown(Key.LeftCtrl))
        {
            e.Handled = true;
            ViewModel.NavigateHistoryUp();
            return;
        }

        if (e.Key == Key.Down && Keyboard.IsKeyDown(Key.LeftCtrl))
        {
            e.Handled = true;
            ViewModel.NavigateHistoryDown();
            return;
        }
    }

    /// <summary>[A6] 编辑框快捷键：Enter 保存，Esc 取消</summary>
    private void EditingBox_KeyDown(object sender, KeyEventArgs e)
    {
        var textBox = (TextBox)sender;
        var msg = textBox.DataContext as Models.MessageDisplayModel;
        if (msg == null) return;

        if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            _ = msg.SaveEditCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            msg.CancelEditCommand.Execute(null);
        }
    }

    /// <summary>发送按钮点击事件</summary>
    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SendAsync();
    }

    // ──────────────── 滚动事件 ────────────────

    /// <summary>滚动事件：自动滚底 + 顶部加载历史</summary>
    private async void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var scroll = (ScrollViewer)sender;

        try
        {
            // 1. 滚动到最顶部时加载更早的历史消息
            if (!_isLoadingMore && e.VerticalOffset <= 0 && scroll.ExtentHeight > scroll.ViewportHeight)
            {
                await LoadMoreHistoryInternalAsync(scroll);
            }

            // 2. 新消息到达时自动滚底
            if (e.ExtentHeightChange > 0 && IsNearBottom(scroll))
            {
                scroll.ScrollToBottom();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatAreaControl] OnScrollChanged 异常: {ex.Message}");
        }
    }

    /// <summary>加载更多历史消息，并保持滚动位置</summary>
    private async Task LoadMoreHistoryInternalAsync(ScrollViewer scroll)
    {
        _isLoadingMore = true;
        try
        {
            var offsetBefore = scroll.VerticalOffset;
            var extentBefore = scroll.ExtentHeight;

            await ViewModel.LoadMoreHistoryAsync();

            if (scroll.ExtentHeight > extentBefore)
            {
                var heightAdded = scroll.ExtentHeight - extentBefore;
                scroll.ScrollToVerticalOffset(offsetBefore + heightAdded);
            }
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    private static bool IsNearBottom(ScrollViewer scroll)
    {
        if (scroll.CanContentScroll)
        {
            return scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 2;
        }
        else
        {
            return scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 100;
        }
    }
}
