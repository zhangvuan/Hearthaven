using Hearthaven.Core.Services;
using Hearthaven.Core.Utilities;
using System.Text;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件读取工具 — 读取指定路径的文本文件内容。
/// 支持按行分片读取（offset/limit），避免一次性读取大文件。
/// LLM 按行号定位比字节偏移更直观，也更节省 token。
/// </summary>
public class ReadFileTool : ToolBase, ITool
{
    public string Name => "read_file";
    public string Description => "读取指定路径的文本文件内容。支持按行分片读取（head:N / tail:N / A-B / offset+limit）。最大支持 500MB。";

    public ReadFileTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        var range = Utilities.JsonHelper.ExtractString(argsJson, "range");
        var offset = Utilities.JsonHelper.ExtractInt32(argsJson, "offset") ?? 0;
        var limit = Utilities.JsonHelper.ExtractInt32(argsJson, "limit") ?? 0;

        var prefix = path != null ? $"查看文件 [{path}]" : "查看文件";

        if (!string.IsNullOrEmpty(range))
            return $"{prefix}  [{range}]";

        // 有明确的行范围时追加显示
        if (limit > 0)
        {
            var endLine = offset + limit - 1;
            return $"{prefix}  {offset}-{endLine} 行";
        }
        if (offset > 0)
            return $"{prefix}  {offset}-END 行";

        return prefix;
    }

    public ToolResultViewData FormatResult(string result)
    {
        // 匹配 info 行格式（以 [ 开头），避免误匹配文件内容中的 "共 N 行" 或 "第 X-Y 行"
        //   - 局部读取: [已读取第 1-100 行，共 200 行。如需继续读取...
        //   - 全部读取: [已读取全部内容，共 200 行]
        var infoLine = System.Text.RegularExpressions.Regex.Match(result, @"\[已读取.+?共 (\d+) 行");
        if (!infoLine.Success)
            return new ToolResultViewData();

        var totalLines = infoLine.Groups[1].Value;

        // 判断是否为局部读取（info 行含 "第 X-Y 行" 模式）
        var rangeMatch = System.Text.RegularExpressions.Regex.Match(result, @"\[已读取第 (\d+)-(\d+) 行");
        if (rangeMatch.Success)
        {
            var start = int.Parse(rangeMatch.Groups[1].Value);
            var end = int.Parse(rangeMatch.Groups[2].Value);
            var lineCount = end - start + 1;
            return new ToolResultViewData
            {
                SummaryTag = $"共 {totalLines} 行（显示 {lineCount} 行）"
            };
        }

        // 全部读取
        return new ToolResultViewData
        {
            SummaryTag = $"共 {totalLines} 行"
        };
    }

    /// <summary>最大允许读取的文件大小（500MB）</summary>
    private const long MaxFileSize = 500 * 1024 * 1024;

    /// <summary>二进制文件扩展名黑名单（读取这类文件时直接拒绝）</summary>
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".lib", ".obj",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".flv",
        ".ttf", ".otf", ".woff", ".woff2",
        ".ico", ".cur",
        ".pdb", ".ds_store", ".ds_Store"
    };

    /// <summary>二进制嗅探读取大小（前 8KB）</summary>
    private const int BinarySniffSize = 8 * 1024;

    /// <summary>常见代码/脚本文件后缀 — 这些文件应当使用 UTF-8 无 BOM 编码</summary>
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift",
        ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hxx",
        ".csproj", ".sln", ".fsproj", ".vbproj",
        ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".md", ".markdown", ".rst",
        ".css", ".scss", ".less", ".html", ".htm", ".vue", ".svelte",
        ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1",
        ".php", ".pl", ".pm", ".lua",
        ".sql", ".graphql", ".gql",
        ".gradle", ".groovy",
        ".editorconfig", ".gitignore", ".gitattributes",
        ".env", ".env.example",
        ".dockerfile", ".makefile", ".cmake",
        ".props", ".targets", ".nuspec", ".config",
        ".proto", ".yml", ".yaml",
    };

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "文件路径（相对于程序运行目录，或绝对路径）"
            },
            range = new
            {
                type = "string",
                description = "读取范围（可选），格式：head:N（前 N 行）、tail:N（最后 N 行）、A-B（第 A 到第 B 行，从 1 开始）。与 offset/limit 二选一，同时提供时 range 优先"
            },
            offset = new
            {
                type = "integer",
                description = "读取起始行号（从 0 开始），默认 0"
            },
            limit = new
            {
                type = "integer",
                description = "最大读取行数（不传则从 offset 读到文件末尾），与 range 二选一"
            }
        },
        required = new[] { "path" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ReadFileArgs>(argsJson);
            if (args?.Path == null)
                return ToolOutput.Error("错误：缺少 path 参数");

            var filePath = ResolvePath(args.Path.Trim());
            var fullPath = Path.GetFullPath(filePath);

            if (!File.Exists(fullPath))
                return ToolOutput.Error($"错误：文件不存在 '{fullPath}'");

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaxFileSize)
                return ToolOutput.Error($"错误：文件过大（{FormatHelper.FormatSize(fileInfo.Length)}），最多支持读取 {FormatHelper.FormatSize(MaxFileSize)}");

            // ── 二进制检测 ──
            // 扩展名黑名单检查
            var ext = Path.GetExtension(fullPath);
            if (!string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext))
                return ToolOutput.Error($"错误：文件 '{fullPath}' 是二进制文件（{ext}），无法以文本方式读取");

            // 内容嗅探：读取前 8KB 检查 NUL 字节
            if (fileInfo.Length > 0)
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BinarySniffSize);
                var sniffBuffer = new byte[Math.Min(BinarySniffSize, fileInfo.Length)];
                await fs.ReadExactlyAsync(sniffBuffer, 0, sniffBuffer.Length, ct).ConfigureAwait(false);

                // NUL 字节检测（文本文件不应含 NUL）
                for (int i = 0; i < sniffBuffer.Length; i++)
                {
                    if (sniffBuffer[i] == 0)
                        return ToolOutput.Error($"错误：文件 '{fullPath}' 包含 NUL 字节，看起来是二进制文件，无法以文本方式读取");
                }
            }

            // ── 编码检测 ──
            var isCodeFile = !string.IsNullOrEmpty(ext) && CodeExtensions.Contains(ext);
            var (detectedEncoding, detectedEncLabel) = DetectEncoding(fullPath, isCodeFile);

            // ── 解析 range 参数（优先于 offset/limit）──
            int startLine;
            int maxLineCount;
            bool readToEnd;
            bool isTailMode = false;

            if (!string.IsNullOrEmpty(args.Range))
            {
                var range = args.Range.Trim();

                if (range.StartsWith("head:", StringComparison.OrdinalIgnoreCase))
                {
                    var n = int.Parse(range["head:".Length..]);
                    if (n <= 0)
                        return ToolOutput.Error($"错误：head:N 的 N 必须大于 0");
                    startLine = 0;
                    maxLineCount = n;
                    readToEnd = false;
                }
                else if (range.StartsWith("tail:", StringComparison.OrdinalIgnoreCase))
                {
                    var n = int.Parse(range["tail:".Length..]);
                    if (n <= 0)
                        return ToolOutput.Error($"错误：tail:N 的 N 必须大于 0");
                    startLine = 0;
                    maxLineCount = n;
                    readToEnd = false;
                    isTailMode = true;
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(range, @"^\d+-\d+$"))
                {
                    var parts = range.Split('-');
                    var a = int.Parse(parts[0]);
                    var b = int.Parse(parts[1]);
                    if (a < 1 || b < a)
                        return ToolOutput.Error($"错误：无效范围 '{range}'，A-B 格式要求 A≥1 且 A≤B");
                    startLine = a - 1; // 转为 0-indexed
                    maxLineCount = b - a + 1;
                    readToEnd = false;
                }
                else
                {
                    return ToolOutput.Error($"错误：无法解析 range 参数 '{range}'，支持的格式：head:N、tail:N、A-B（如 10-20）");
                }
            }
            else
            {
                startLine = Math.Max(0, args.Offset);
                readToEnd = args.Limit <= 0;
                maxLineCount = readToEnd ? int.MaxValue : args.Limit;
            }

            // ── tail 模式：用环形缓冲区读取最后 N 行 ──
            if (isTailMode)
            {
                var buffer = new List<string>(maxLineCount);
                int totalLines = 0;

                foreach (var line in File.ReadLines(fullPath, detectedEncoding))
                {
                    buffer.Add(line);
                    totalLines++;
                    if (buffer.Count > maxLineCount)
                        buffer.RemoveAt(0);
                }

                if (totalLines == 0)
                    return ToolOutput.Success("[文件为空]");

                var actualStartLine = Math.Max(0, totalLines - maxLineCount);
                var numberedContent = new System.Text.StringBuilder();
                for (int i = 0; i < buffer.Count; i++)
                {
                    numberedContent.AppendLine($"{actualStartLine + i + 1,6}| {buffer[i]}");
                }

                var info = $"[已读取最后 {buffer.Count} 行（第 {actualStartLine + 1}-{actualStartLine + buffer.Count} 行），共 {totalLines} 行，{detectedEncLabel}]";
                return ToolOutput.Success($"{numberedContent}\n{info}");
            }

            // ── 普通模式 / range 模式：流式读取 ──
            var readEndLine = readToEnd ? int.MaxValue : startLine + maxLineCount;

            var selectedLines = new List<string>();
            int lineIdx = 0;
            int total = 0;

            foreach (var line in File.ReadLines(fullPath, detectedEncoding))
            {
                if (lineIdx >= startLine && lineIdx < readEndLine)
                    selectedLines.Add(line);

                lineIdx++;

                if (!readToEnd && lineIdx >= readEndLine)
                    break;
            }
            total = lineIdx;

            if (startLine >= total)
                return ToolOutput.Success($"[已到达文件末尾：startLine={startLine} 超出文件总行数 {total}]");

            var actualEndLine = readToEnd ? total : Math.Min(startLine + maxLineCount, total);
            var hasMore = !readToEnd && actualEndLine < total;

            var numbered = new System.Text.StringBuilder();
            for (int i = 0; i < selectedLines.Count; i++)
            {
                numbered.AppendLine($"{startLine + i + 1,6}| {selectedLines[i]}");
            }

            var infoMsg = hasMore
                ? $"[已读取第 {startLine + 1}-{actualEndLine} 行，共 {total} 行，{detectedEncLabel}。如需继续读取剩余 {total - actualEndLine} 行，请使用 range={actualEndLine}-{total} 或 offset={actualEndLine}]"
                : $"[已读取全部内容，共 {total} 行，{detectedEncLabel}]";

            return ToolOutput.Success($"{numbered}\n{infoMsg}");
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 path 字符串参数");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：读取文件时发生异常 — {ex.Message}");
        }
    }

    /// <summary>
    /// 检测文件编码并生成描述标签。
    /// 对代码/脚本文件：非无 BOM UTF-8 时附加 ⚠️ 警告。
    /// </summary>
    private static (Encoding encoding, string label) DetectEncoding(string filePath, bool isCodeFile)
    {
        try
        {
            var preamble = new byte[4];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4))
            {
                fs.ReadExactly(preamble, 0, 4);
            }

            // UTF-8 BOM: EF BB BF
            if (preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF)
            {
                var label = isCodeFile
                    ? "编码：UTF-8 ⚠️ 含 BOM 头（代码文件建议转为无 BOM UTF-8）"
                    : "编码：UTF-8（含 BOM 头）";
                return (Encoding.UTF8, label);
            }

            // UTF-16 LE BOM: FF FE
            if (preamble[0] == 0xFF && preamble[1] == 0xFE)
            {
                var label = isCodeFile
                    ? "编码：UTF-16 LE ⚠️ 代码文件建议转为 UTF-8 无 BOM"
                    : "编码：UTF-16 LE";
                return (Encoding.Unicode, label);
            }

            // UTF-16 BE BOM: FE FF
            if (preamble[0] == 0xFE && preamble[1] == 0xFF)
            {
                var label = isCodeFile
                    ? "编码：UTF-16 BE ⚠️ 代码文件建议转为 UTF-8 无 BOM"
                    : "编码：UTF-16 BE";
                return (Encoding.BigEndianUnicode, label);
            }

            // 无 BOM → 标准 UTF-8
            return (Encoding.UTF8, "编码：UTF-8");
        }
        catch
        {
            // 检测失败时默认 UTF-8
        }

        return (Encoding.UTF8, "编码：UTF-8");
    }



    private class ReadFileArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("range")]
        public string? Range { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("offset")]
        public int Offset { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("limit")]
        public int Limit { get; set; }
    }
}
