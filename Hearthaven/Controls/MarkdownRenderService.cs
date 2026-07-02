using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// 类型别名：消除 WPF 和 Markdig 之间 Block / Inline 的命名冲突
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfInline = System.Windows.Documents.Inline;

namespace Hearthaven.Controls;

/// <summary>
/// 使用 Markdig 解析 Markdown 并生成 WPF FlowDocument 的渲染服务。
/// 生成的 FlowDocument 可直接用于 RichTextBox(IsReadOnly=True)。
/// </summary>
public static class MarkdownRenderService
{
    // ─── 样式常量 ───

    private const double ParagraphFontSize = 14;
    private const double ParagraphLineHeight = 22;
    private const double ParagraphBottomMargin = 6;

    private const double H1FontSize = 20;
    private const double H2FontSize = 17;
    private const double H3FontSize = 15;

    private const double CodeFontSize = 12.5;

    // ─── 主题色 Brush 解析（渲染时从当前主题资源获取，支持实时切换）───

    /// <summary>从 Application 资源中解析主题画刷，找不到时使用 fallback</summary>
    private static SolidColorBrush ThemeBrush(string key, byte r, byte g, byte b)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    /// <summary>正文文字色 — BrushText</summary>
    private static SolidColorBrush BrText => ThemeBrush("BrushText", 0x21, 0x25, 0x29);

    /// <summary>次要文字色 — BrushTextSecondary</summary>
    private static SolidColorBrush BrTextSecondary => ThemeBrush("BrushTextSecondary", 0x86, 0x8E, 0x96);

    /// <summary>链接色 — BrushAccent</summary>
    private static SolidColorBrush BrLink => ThemeBrush("BrushAccent", 0x33, 0x9A, 0xF0);

    /// <summary>代码块背景 — 比 BrushHover 稍深</summary>
    private static SolidColorBrush BrCodeBlockBg => ThemeBrush("BrushHover", 0xF5, 0xF5, 0xF5);

    /// <summary>行内代码背景 — BrushHover</summary>
    private static SolidColorBrush BrInlineCodeBg => ThemeBrush("BrushHover", 0xEE, 0xEE, 0xEE);

    /// <summary>引用块左边框 — BrushBorder</summary>
    private static SolidColorBrush BrQuoteBorder => ThemeBrush("BrushBorder", 0xCC, 0xCC, 0xCC);

    /// <summary>表格边框 — BrushBorder</summary>
    private static SolidColorBrush BrTableBorder => ThemeBrush("BrushBorder", 0xCC, 0xCC, 0xCC);

    /// <summary>表头背景 — BrushHover</summary>
    private static SolidColorBrush BrTableHeaderBg => ThemeBrush("BrushHover", 0xF5, 0xF5, 0xF5);

    /// <summary>表格斑马纹交替行背景 — BrushRowAlt</summary>
    private static SolidColorBrush BrTableRowAlt => ThemeBrush("BrushRowAlt", 0xF8, 0xF5, 0xF0);

    /// <summary>分隔线色 — BrushDivider</summary>
    private static SolidColorBrush BrThematicBreak => ThemeBrush("BrushDivider", 0xDD, 0xDD, 0xDD);
    private static readonly FontFamily FontConsolas = new("Consolas");

    // ─── 音频扩展名（用于检测 Markdown 链接是否为音频文件）───
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac", ".wma", ".opus"
    };

    private static bool IsAudioUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        var ext = Path.GetExtension(url);
        return AudioExtensions.Contains(ext);
    }

    // ─── Diff 代码变更高亮配色（半透明，亮暗主题通用）───
    private static readonly SolidColorBrush DiffAddedBg = new(Color.FromArgb(40, 34, 197, 94));   // 新增行：半透明绿
    private static readonly SolidColorBrush DiffRemovedBg = new(Color.FromArgb(40, 239, 68, 68)); // 删除行：半透明红
    private static readonly SolidColorBrush DiffHeaderBg = new(Color.FromArgb(40, 59, 130, 246));  // 块标头 @@：半透明蓝

    // ─── 缓存解析器 ───

    private static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseGridTables()
        .UseListExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .UseDefinitionLists()
        .UseGenericAttributes()
        .Build();

    // ─── 公共入口 ───

    /// <summary>
    /// 将 Markdown 文本渲染为 FlowDocument（同步版本，短文本用）。
    /// </summary>
    public static FlowDocument Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = ParagraphLineHeight,
                FontSize = ParagraphFontSize
            };

        var mdDoc = Markdig.Markdown.Parse(markdown, Pipeline);
        return BuildFlowDocument(mdDoc);
    }

    /// <summary>
    /// 解析 Markdown 文本为 Markdig 语法树（纯 .NET，可在后台线程执行）。
    /// 返回的 MarkdownDocument 不依赖 WPF，可安全在 Task.Run 中调用。
    /// </summary>
    public static Markdig.Syntax.MarkdownDocument Parse(string markdown)
    {
        return Markdig.Markdown.Parse(markdown, Pipeline);
    }

    /// <summary>
    /// 将 Markdig 语法树构建为 WPF FlowDocument（必须在 UI 线程执行）。
    /// 与 Parse() 配对使用，实现 Markdown 后台解析 + UI 线程渲染。
    /// </summary>
    public static FlowDocument BuildFlowDocument(Markdig.Syntax.MarkdownDocument mdDoc)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            LineHeight = ParagraphLineHeight,
            FontSize = ParagraphFontSize,
            Foreground = BrText,  // 正文默认色跟随主题
            IsHyphenationEnabled = true  // 英文自动断词，减少行末留白不均
        };

        foreach (var block in mdDoc)
        {
            var rendered = RenderBlock(block);
            if (rendered != null)
                doc.Blocks.Add(rendered);
        }

        return doc;
    }

    // ─── 块级渲染 ───

    private static WpfBlock? RenderBlock(MdBlock block)
    {
        return block switch
        {
            ParagraphBlock p => RenderParagraph(p),
            HeadingBlock h => RenderHeading(h),
            FencedCodeBlock c => RenderFencedCodeBlock(c),
            CodeBlock c => RenderIndentedCodeBlock(c),
            ListBlock l => RenderList(l),
            QuoteBlock q => RenderQuote(q),
            ThematicBreakBlock => RenderThematicBreak(),
            Markdig.Extensions.Tables.Table t => RenderTable(t),
            _ => null
        };
    }

    private static Paragraph RenderParagraph(ParagraphBlock paragraph)
    {
        var para = new Paragraph { Margin = new Thickness(0, 0, 0, ParagraphBottomMargin) };
        if (paragraph.Inline != null)
        {
            foreach (var inline in paragraph.Inline)
            {
                var rendered = RenderInline(inline);
                if (rendered != null)
                    para.Inlines.Add(rendered);
            }
        }
        return para;
    }

    private static Paragraph RenderHeading(HeadingBlock heading)
    {
        var para = new Paragraph
        {
            FontWeight = heading.Level == 1 || heading.Level == 2
                ? FontWeights.Bold
                : FontWeights.SemiBold
        };

        switch (heading.Level)
        {
            case 1:
                para.FontSize = H1FontSize;
                para.Foreground = BrText;
                para.FontWeight = FontWeights.Bold;
                para.Margin = new Thickness(0, 12, 0, 6);
                break;
            case 2:
                para.FontSize = H2FontSize;
                para.Foreground = BrText;
                para.FontWeight = FontWeights.Bold;
                para.Margin = new Thickness(0, 10, 0, 4);
                break;
            default:
                para.FontSize = H3FontSize;
                para.Foreground = BrTextSecondary;
                para.Margin = new Thickness(0, 8, 0, 4);
                break;
        }

        if (heading.Inline != null)
        {
            foreach (var inline in heading.Inline)
            {
                var rendered = RenderInline(inline);
                if (rendered != null)
                    para.Inlines.Add(rendered);
            }
        }

        return para;
    }

    private static WpfBlock RenderFencedCodeBlock(FencedCodeBlock codeBlock)
    {
        var language = codeBlock.Info?.Trim();

        // Diff 代码变更高亮
        if (string.Equals(language, "diff", StringComparison.OrdinalIgnoreCase) ||
            (language?.StartsWith("diff-", StringComparison.OrdinalIgnoreCase) == true))
        {
            return RenderDiffBlock(codeBlock);
        }

        var code = codeBlock.Lines.ToString().TrimEnd('\n');

        var section = new Section();

        // 语言标签（如 csharp、json、xml）
        if (!string.IsNullOrEmpty(language))
        {
            section.Blocks.Add(new Paragraph(new Run(language))
            {
                FontSize = 10,
                Foreground = BrTextSecondary,
                FontFamily = FontConsolas,
                Background = BrCodeBlockBg,
                Margin = new Thickness(12, 8, 12, 0),
                Padding = new Thickness(0),
                TextAlignment = TextAlignment.Right,
                LineHeight = 14
            });
        }

        var run = new Run(code)
        {
            FontFamily = FontConsolas,
            FontSize = CodeFontSize,
            Foreground = BrText
        };

        section.Blocks.Add(new Paragraph(run)
        {
            Background = BrCodeBlockBg,
            FontFamily = FontConsolas,
            FontSize = CodeFontSize,
            Padding = new Thickness(12, language != null ? 4 : 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6)
        });

        return section;
    }

    private static Paragraph RenderIndentedCodeBlock(CodeBlock codeBlock)
    {
        var code = codeBlock.Lines.ToString();
        var run = new Run(code.TrimEnd('\n'))
        {
            FontFamily = FontConsolas,
            FontSize = CodeFontSize,
            Foreground = BrText,
            Background = BrInlineCodeBg
        };
        return new Paragraph(run)
        {
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(12, 10, 12, 10),
            Background = BrCodeBlockBg
        };
    }

    // ─── Diff 代码变更高亮 ───

    /// <summary>
    /// 渲染 diff 代码块：检测行前缀，新增/删除/块标头分别着色。
    /// 每行独立 Paragraph，行间距归零，视觉上像连续块。
    /// </summary>
    private static Section RenderDiffBlock(FencedCodeBlock codeBlock)
    {
        var lines = codeBlock.Lines.ToString().TrimEnd('\n');
        var section = new Section();

        // 逐行拆分并渲染
        int start = 0;
        while (start < lines.Length)
        {
            var end = lines.IndexOf('\n', start);
            if (end < 0) end = lines.Length;

            var line = lines.AsSpan(start, end - start);
            var bg = BrCodeBlockBg;
            var prefix = line.Length > 0 ? line[0] : ' ';

            switch (prefix)
            {
                case '+':
                    bg = DiffAddedBg;
                    break;
                case '-':
                    bg = DiffRemovedBg;
                    break;
                case '@':
                    bg = DiffHeaderBg;
                    break;
            }

            section.Blocks.Add(new Paragraph(new Run(line.ToString()))
            {
                FontFamily = FontConsolas,
                FontSize = CodeFontSize,
                Foreground = BrText,
                Background = bg,
                Margin = new Thickness(0),
                Padding = new Thickness(12, 1, 12, 1),
                LineHeight = 18,
                KeepTogether = true
            });

            start = end + 1;
        }

        return section;
    }

    private static System.Windows.Documents.List RenderList(ListBlock listBlock)
    {
        var isOrdered = listBlock.IsOrdered;
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = isOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(8, 0, 0, 6)
        };

        foreach (var item in listBlock)
        {
            if (item is ListItemBlock listItem)
            {
                var li = new ListItem
                {
                    Margin = new Thickness(0)
                };
                foreach (var childBlock in listItem)
                {
                    var rendered = RenderBlock(childBlock);
                    if (rendered is Paragraph para)
                    {
                        // 列表项内的段落底部间距缩小，避免列表项之间间距过大
                        para.Margin = new Thickness(0, 0, 0, 2);
                        li.Blocks.Add(para);
                    }
                    else if (rendered != null)
                    {
                        li.Blocks.Add(rendered);
                    }
                }
                list.ListItems.Add(li);
            }
        }

        return list;
    }

    private static Section RenderQuote(QuoteBlock quoteBlock)
    {
        var section = new Section
        {
            Padding = new Thickness(12, 6, 0, 6),
            BorderThickness = new Thickness(4, 0, 0, 0),
            BorderBrush = BrQuoteBorder,
            Margin = new Thickness(0, 4, 0, 4)
        };

        foreach (var child in quoteBlock)
        {
            var rendered = RenderBlock(child);
            if (rendered != null)
                section.Blocks.Add(rendered);
        }

        return section;
    }

    private static Paragraph RenderThematicBreak()
    {
        return new Paragraph(new Run(new string('─', 40)))
        {
            Foreground = BrThematicBreak,
            FontSize = 10,
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    // ─── 表格渲染 ───

    private static System.Windows.Documents.Table RenderTable(Markdig.Extensions.Tables.Table mdTable)
    {
        var table = new System.Windows.Documents.Table
        {
            Margin = new Thickness(0, 6, 0, 6),
            CellSpacing = 0,
            BorderBrush = BrTableBorder
        };

        // 列宽使用 Auto（内容自适应），避免等比分配下序号等短内容列浪费空间、
        // 标题等长内容列被挤换行。列宽由各列最宽内容决定，短列窄、长列宽。
        if (mdTable.ColumnDefinitions != null)
        {
            foreach (var _ in mdTable.ColumnDefinitions)
            {
                table.Columns.Add(new TableColumn
                {
                    Width = GridLength.Auto
                });
            }
        }

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);

        int rowIndex = 0;
        foreach (var rowObj in mdTable)
        {
            if (rowObj is not Markdig.Extensions.Tables.TableRow mdRow) continue;

            var wpfRow = new System.Windows.Documents.TableRow();

            // 表头行：加粗 + 浅灰背景
            if (mdRow.IsHeader)
            {
                wpfRow.FontWeight = FontWeights.Bold;
                wpfRow.Background = BrTableHeaderBg;
            }
            // 数据行斑马纹：奇数行用交替色，偶数行用透明（自然透出页面背景）
            else if (rowIndex % 2 == 1)
            {
                wpfRow.Background = BrTableRowAlt;
            }

            foreach (var cellObj in mdRow)
            {
                if (cellObj is not Markdig.Extensions.Tables.TableCell mdCell) continue;

                // 渲染单元格内的内联内容（支持加粗、链接、行内代码等）
                var cellPara = new Paragraph
                {
                    Margin = new Thickness(6, 3, 6, 3)
                };

                // Markdig TableCell 继承 ContainerBlock，通过子 ParagraphBlock 获取内联内容
                foreach (var childBlock in mdCell)
                {
                    if (childBlock is Markdig.Syntax.ParagraphBlock paraBlock && paraBlock.Inline != null)
                    {
                        foreach (var inline in paraBlock.Inline)
                        {
                            var rendered = RenderInline(inline);
                            if (rendered != null)
                                cellPara.Inlines.Add(rendered);
                        }
                    }
                }

                var wpfCell = new System.Windows.Documents.TableCell
                {
                    BorderBrush = BrTableBorder,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(6, 3, 6, 3)
                };

                wpfCell.Blocks.Add(cellPara);

                // 合并列（使用附加属性方式）
                if (mdCell.ColumnSpan > 1)
                    wpfCell.SetValue(System.Windows.Documents.TableCell.ColumnSpanProperty, mdCell.ColumnSpan);

                // 合并行（使用附加属性方式）
                if (mdCell.RowSpan > 1)
                    wpfCell.SetValue(System.Windows.Documents.TableCell.RowSpanProperty, mdCell.RowSpan);

                // 列对齐 — 设置到 TableCell 上（直接设置 Paragraph.TextAlignment
                // 可能被 TableCell 的布局覆盖，双重保障更可靠）
                if (mdTable.ColumnDefinitions != null &&
                    mdCell.ColumnIndex >= 0 &&
                    mdCell.ColumnIndex < mdTable.ColumnDefinitions.Count)
                {
                    var alignment = mdTable.ColumnDefinitions[mdCell.ColumnIndex].Alignment;
                    if (alignment.HasValue)
                    {
                        var textAlign = alignment.Value switch
                        {
                            Markdig.Extensions.Tables.TableColumnAlign.Center => TextAlignment.Center,
                            Markdig.Extensions.Tables.TableColumnAlign.Right => TextAlignment.Right,
                            _ => TextAlignment.Left
                        };
                        wpfCell.TextAlignment = textAlign;
                        cellPara.TextAlignment = textAlign;
                    }
                }

                wpfRow.Cells.Add(wpfCell);
            }

            rowGroup.Rows.Add(wpfRow);

            // 只对数据行计数（表头不参与斑马纹交替）
            if (!mdRow.IsHeader)
                rowIndex++;
        }

        return table;
    }

    // ─── 内联渲染 ───

    private static WpfInline? RenderInline(MdInline inline)
    {
        return inline switch
        {
            LiteralInline literal => new Run(literal.Content.ToString()),
            CodeInline code => RenderCodeInline(code),
            LinkInline link when IsAudioUrl(link.Url) => RenderAudio(link),
            LinkInline link when link.IsImage => RenderImage(link),
            LinkInline link => RenderLink(link),
            EmphasisInline emphasis => RenderEmphasis(emphasis),
            LineBreakInline => new LineBreak(),
            AutolinkInline autoLink => new Hyperlink(new Run(autoLink.Url)) { NavigateUri = new Uri(autoLink.Url) },
            _ => null
        };
    }

    private static Run RenderCodeInline(CodeInline codeInline)
    {
        return new Run(codeInline.Content)
        {
            FontFamily = FontConsolas,
            FontSize = CodeFontSize,
            Background = BrInlineCodeBg
        };
    }

    // ─── 图片渲染 ───

    /// <summary>
    /// 渲染 Markdown 图片 ![alt](url)。
    /// 用 InlineUIContainer 嵌入 Image 控件，点击用系统关联程序打开查看大图。
    /// 最大高度 300px，宽度自适应（宽 > 容器时等比缩宽，否则原尺寸）。
    /// </summary>
    private static WpfInline? RenderImage(LinkInline link)
    {
        var url = link.Url;
        if (string.IsNullOrEmpty(url))
            return null;

        var image = new Image
        {
            MaxHeight = 300,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = "🖼️ 点击查看大图"
        };

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(ResolveUrl(url));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (bitmap.CanFreeze) bitmap.Freeze();
            image.Source = bitmap;
        }
        catch
        {
            // 图片加载失败 → 显示 alt 文本作为 fallback
            return new Run($"🖼️ [{GetLinkDisplayText(link)}]");
        }

        // 用 Hyperlink 包裹 InlineUIContainer，点击用系统关联程序打开查看大图
        var hyperlink = CreateImageHyperlink(url);
        hyperlink.Inlines.Add(new InlineUIContainer(image));
        return hyperlink;
    }

    /// <summary>创建图片超链接，点击用系统关联程序打开（本地→图片查看器，网络→浏览器）</summary>
    private static Hyperlink CreateImageHyperlink(string url)
    {
        var hyperlink = new Hyperlink
        {
            NavigateUri = new Uri(ResolveUrl(url)),
            ToolTip = "🖼️ 点击查看大图"
        };

        hyperlink.RequestNavigate += (_, args) =>
        {
            try
            {
                // 本地文件直接用路径（避免 file:// 协议某些程序不识别），网络 URL 用完整 URI
                var target = args.Uri.IsFile ? args.Uri.LocalPath : args.Uri.AbsoluteUri;
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(target)
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
                // 忽略打开失败
            }
        };

        return hyperlink;
    }

    /// <summary>获取链接的显示文本（alt 文本）</summary>
    private static string GetLinkDisplayText(LinkInline link)
    {
        if (link.FirstChild is LiteralInline lit)
            return lit.Content.ToString();
        return link.Url ?? "";
    }

    // ─── 音频渲染 ───

    /// <summary>
    /// 渲染音频链接 [audio.mp3](audio.mp3)。
    /// 用 Button + MediaPlayer 实现点击播放/停止。
    /// 支持本地文件路径和网络 URL。
    /// </summary>
    private static WpfInline? RenderAudio(LinkInline link)
    {
        var url = link.Url;
        if (string.IsNullOrEmpty(url)) return null;

        var fileName = Path.GetFileName(url);
        var resolvedUrl = ResolveUrl(url);

        var button = new Button
        {
            Content = $"▶️ {fileName}",
            Cursor = Cursors.Hand,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 2, 0, 2),
            FontSize = 12,
            BorderThickness = new Thickness(1),
            BorderBrush = BrTableBorder,
            Background = BrCodeBlockBg,
            Foreground = BrText
        };

        MediaPlayer? player = null;

        void CleanupPlayer()
        {
            if (player != null)
            {
                player.Stop();
                player.Close();
                player = null;
            }
            button.Content = $"▶️ {fileName}";
        }

        button.Click += (_, _) =>
        {
            if (player != null)
            {
                // 正在播放 → 停止
                CleanupPlayer();
                return;
            }

            try
            {
                player = new MediaPlayer();
                player.Open(new Uri(resolvedUrl));
                player.MediaEnded += (_, _) => CleanupPlayer();
                player.MediaFailed += (_, _) =>
                {
                    CleanupPlayer();
                    button.Content = $"❌ {fileName}";
                };
                player.Play();
                button.Content = $"⏹ {fileName}";
            }
            catch
            {
                CleanupPlayer();
                button.Content = $"❌ {fileName}";
            }
        };

        return new InlineUIContainer(button);
    }

    /// <summary>解析相对路径 → 绝对路径</summary>
    private static string ResolveUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri)
            return url;
        return Path.GetFullPath(url);
    }

    private static WpfInline RenderLink(LinkInline link)
    {
        var hyperlink = new Hyperlink
        {
            Foreground = BrLink,
            TextDecorations = null
        };

        if (link.Url != null)
            hyperlink.NavigateUri = new Uri(link.Url, UriKind.RelativeOrAbsolute);

        // 点击链接用系统浏览器打开
        hyperlink.RequestNavigate += (_, args) =>
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(args.Uri.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
            }
            catch
            {
                // 忽略链接打开失败
            }
        };

        // ContainerInline 实现了 IEnumerable<Inline>，可以直接 foreach
        foreach (var child in link)
        {
            var rendered = RenderInline(child);
            if (rendered is Run run)
                hyperlink.Inlines.Add(run);
            else if (rendered is Span span)
                hyperlink.Inlines.Add(span);
            else if (rendered != null)
                hyperlink.Inlines.Add(new Run(rendered.ToString() ?? ""));
        }

        if (hyperlink.Inlines.Count == 0 && link.Url != null)
            hyperlink.Inlines.Add(new Run(link.Url));

        return hyperlink;
    }

    private static WpfInline RenderEmphasis(EmphasisInline emphasis)
    {
        var delimiterChar = emphasis.DelimiterChar;
        var isDouble = emphasis.DelimiterCount == 2;

        // 删除线用 ~~ 表示（GFM 扩展），单个 ~ 不做特殊处理
        if (delimiterChar == '~')
        {
            var span = new Span();
            if (emphasis.DelimiterCount >= 2)
                span.TextDecorations = TextDecorations.Strikethrough;
            foreach (var child in emphasis)
            {
                var rendered = RenderInline(child);
                if (rendered != null)
                    span.Inlines.Add(rendered);
            }
            return span;
        }

        // 加粗（** 或 __）或斜体（* 或 _）
        if (isDouble)
        {
            var bold = new Bold();
            foreach (var child in emphasis)
            {
                var rendered = RenderInline(child);
                if (rendered != null)
                    bold.Inlines.Add(rendered);
            }
            return bold;
        }
        else
        {
            var italic = new Italic();
            foreach (var child in emphasis)
            {
                var rendered = RenderInline(child);
                if (rendered != null)
                    italic.Inlines.Add(rendered);
            }
            return italic;
        }
    }
}
