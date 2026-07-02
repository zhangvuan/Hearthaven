using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Hearthaven.Controls;

/// <summary>
/// 基于 RichTextBox(IsReadOnly=True) 的 Markdown 渲染控件。
/// 使用 MarkdownRenderService 将 Markdown 解析为 FlowDocument，支持文本选择和复制。
/// </summary>
public class MarkdownViewer : RichTextBox
{
    /// <summary>
    /// Markdown 文本内容依赖属性。
    /// 设置后自动解析并渲染为富文本。
    /// </summary>
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownViewer),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    /// <summary>
    /// 获取或设置 Markdown 文本内容。
    /// </summary>
    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// 构造函数：配置为只读展示模式。
    /// </summary>
    public MarkdownViewer()
    {
        IsReadOnly = true;
        BorderThickness = new Thickness(0);
        Background = System.Windows.Media.Brushes.Transparent;
        Padding = new Thickness(0);
        Margin = new Thickness(0);

        // 禁用内部滚动，由外层 ScrollViewer 统一管理
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        IsDocumentEnabled = true;

        // 控件从可视化树移除时释放 FlowDocument，避免 WPF 内部缓存
        Unloaded += (s, e) => Document = new FlowDocument
        {
            PagePadding = new Thickness(0)
        };
    }

    /// <summary>
    /// 异步渲染阈值（字符数）。超过此长度的 Markdown 走后台线程解析。
    /// </summary>
    private const int AsyncRenderThreshold = 5000;

    /// <summary>
    /// 每次 Markdown 更新时递增的版本号，用于丢弃过时的异步渲染结果。
    /// </summary>
    private volatile int _renderVersion;

    /// <summary>
    /// Markdown 属性变化时触发重新渲染。
    /// 短文本（≤5K）同步渲染，长文本在后台线程解析后再切回 UI 线程构建 FlowDocument。
    /// 使用版本号机制避免异步竞态：旧请求完成时若版本号已变更，丢弃结果。
    /// </summary>
    private static async void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkdownViewer viewer) return;

        var markdown = e.NewValue as string ?? string.Empty;
        await viewer.RenderAsync(markdown);
    }

    /// <summary>
    /// 核心渲染方法。每次调用递增版本号，异步返回后校验版本号一致性。
    /// </summary>
    private async Task RenderAsync(string markdown)
    {
        // 递增版本号，捕获当前请求的版本快照
        var currentVersion = Interlocked.Increment(ref _renderVersion);

        try
        {
            // 短文本：同步渲染，直接覆盖（版本号已保证后续的异步不会回退）
            if (markdown.Length < AsyncRenderThreshold)
            {
                Document = MarkdownRenderService.Render(markdown);
                return;
            }

            // 长文本：后台线程解析 Markdig 语法树，不涉及 WPF 对象，线程安全
            var mdDoc = await Task.Run(() => MarkdownRenderService.Parse(markdown));

            // ⭐ 版本号检查：如果在此期间有更新的渲染请求，丢弃本次结果
            if (currentVersion != _renderVersion) return;

            // ⭐ 控件已卸载检查：不在视觉树中则不渲染，避免白做工 + 资源泄漏
            if (!IsLoaded) return;

            // UI 线程构建 FlowDocument
            Document = MarkdownRenderService.BuildFlowDocument(mdDoc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkdownViewer] Markdown 渲染异常: {ex.Message}");
        }
    }
}
