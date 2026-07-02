using Hearthaven.Core.Services;
using Hearthaven.Core.Utilities;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件编辑工具 — 在文件中搜索替换文本。
/// 支持精确匹配（默认）和正则匹配（use_regex: true），提供 diff 格式输出。
/// 自动保留原文件的行尾风格（CRLF/LF）。
/// </summary>
public class EditFileTool : ToolBase, ITool
{
    public string Name => "edit_file";
    public string Description => "在文件中搜索替换文本。支持精确匹配和正则匹配。最大支持 10MB 文件。";
    public bool IsLongRunning => true;

    public EditFileTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        return path != null ? $"编辑文件 [{path}]" : "编辑文件";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var addedMatch = System.Text.RegularExpressions.Regex.Match(result, @"\+(\d+) 行");
        var removedMatch = System.Text.RegularExpressions.Regex.Match(result, @"-(\d+) 行");
        var added = addedMatch.Success && int.TryParse(addedMatch.Groups[1].Value, out var a) ? a : 0;
        var removed = removedMatch.Success && int.TryParse(removedMatch.Groups[1].Value, out var r) ? r : 0;
        return new ToolResultViewData { LinesAdded = added, LinesRemoved = removed };
    }

    /// <summary>最大允许编辑的文件大小（10MB）</summary>
    private const int MaxEditFileSize = 10 * 1024 * 1024;

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
            old_text = new
            {
                type = "string",
                description = "要替换的原文（精确匹配，区分大小写，忽略换行符差异）。如果 use_regex 为 true，则为正则表达式 pattern"
            },
            new_text = new
            {
                type = "string",
                description = "替换后的新内容。如果 use_regex 为 true，支持 $1、$2 等捕获组引用"
            },
            use_regex = new
            {
                type = "boolean",
                description = "是否将 old_text 作为正则表达式匹配（默认 false）。匹配到多处时同样会拒绝，要求补充上下文"
            }
        },
        required = new[] { "path", "old_text", "new_text" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<EditFileArgs>(argsJson);
            if (args?.Path == null)
                return ToolOutput.Error("错误：缺少 path 参数");
            if (args.OldText == null)
                return ToolOutput.Error("错误：缺少 old_text 参数");
            if (args.NewText == null)
                return ToolOutput.Error("错误：缺少 new_text 参数");

            var filePath = ResolvePath(args.Path.Trim());
            var fullPath = Path.GetFullPath(filePath);

            if (!File.Exists(fullPath))
                return ToolOutput.Error($"错误：文件不存在 '{fullPath}'");

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaxEditFileSize)
                return ToolOutput.Error($"错误：文件过大（{FormatHelper.FormatSize(fileInfo.Length)}），仅支持编辑 {FormatHelper.FormatSize(MaxEditFileSize)} 以内的文件");

            // ⭐ 异步读取文件内容（保留原始行尾）
            var content = await File.ReadAllTextAsync(fullPath, System.Text.Encoding.UTF8, ct)
                .ConfigureAwait(false);

            // 检测原文件的行尾风格
            var hasCrlf = content.Contains("\r\n");
            var originalEol = hasCrlf ? "\r\n" : "\n";

            // 归一化到 LF 进行匹配（忽略 CRLF/LF 差异）
            var normalizedContent = content.Replace("\r\n", "\n");
            var normalizedOldText = args.OldText.Replace("\r\n", "\n");
            var isRegexMode = args.UseRegex;

            // 查找所有匹配位置
            var matchPositions = new List<int>();
            System.Text.RegularExpressions.MatchCollection? regexMatches = null;
            string modeLabel;

            if (isRegexMode)
            {
                modeLabel = "（正则匹配）";
                var regex = new System.Text.RegularExpressions.Regex(
                    normalizedOldText,
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(2));
                regexMatches = regex.Matches(normalizedContent);
                matchPositions = regexMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Index).ToList();
            }
            else
            {
                modeLabel = "（精确匹配）";
                int searchFrom = 0;
                while (true)
                {
                    var pos = normalizedContent.IndexOf(normalizedOldText, searchFrom, StringComparison.Ordinal);
                    if (pos < 0) break;
                    matchPositions.Add(pos);
                    searchFrom = pos + normalizedOldText.Length;
                }
            }

            if (matchPositions.Count == 0)
                return ToolOutput.Warning($"警告：在文件中未找到匹配的文本 {modeLabel}\n\n查找内容：{args.OldText}");

            // 有多处匹配 → 拒绝，让 AI 补充更多上下文使 old_text 唯一
            if (matchPositions.Count > 1)
            {
                return ToolOutput.Warning(
                    $"⚠️ old_text 在文件中有 {matchPositions.Count} 处匹配 {modeLabel}，请补充更多上下文使 old_text 在文件中唯一：\n" +
                    FormatMatchPositions(normalizedContent, normalizedOldText.Length, matchPositions) +
                    $"\n\n查找内容：{args.OldText}");
            }

            // 唯一匹配 → 执行替换
            var selectedPos = matchPositions[0];
            string newNormalized;
            string oldForDiff;

            if (isRegexMode)
            {
                var m = regexMatches![0];
                oldForDiff = m.Value;
                newNormalized = normalizedContent[..m.Index]
                    + m.Result(args.NewText)
                    + normalizedContent[(m.Index + m.Length)..];
            }
            else
            {
                oldForDiff = normalizedOldText;
                newNormalized = normalizedContent[..selectedPos]
                    + args.NewText
                    + normalizedContent[(selectedPos + normalizedOldText.Length)..];
            }

            // 还原为原文件的行尾风格（保留 CRLF）
            var finalContent = hasCrlf
                ? newNormalized.Replace("\n", "\r\n")
                : newNormalized;

            // ── 写入前备份 ──
            var fileName = Path.GetFileName(fullPath);
            ProgressCallback?.Invoke($"正在备份文件 [{fileName}]…");
            await CheckpointManager.BackupAsync(fullPath).ConfigureAwait(false);

            // ── 原子写入 ──
            var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                ProgressCallback?.Invoke($"正在写入文件 [{fileName}]…");
                await File.WriteAllTextAsync(tempPath, finalContent, new System.Text.UTF8Encoding(false), ct)
                    .ConfigureAwait(false);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            // 统计行数变化
            var oldLines = oldForDiff.Split('\n').Length;
            var newLinesCount = args.NewText.Split('\n').Length;

            // 重置状态栏为"就绪"
            ProgressCallback?.Invoke("编辑完成 💬");

            return ToolOutput.Success(
                $"成功替换文件 '{fullPath}' 中的内容（替换 1 处，+{newLinesCount} 行，-{oldLines} 行）");
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 path、old_text 和 new_text 参数");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：编辑文件时发生异常 — {ex.Message}");
        }
    }

    /// <summary>格式化匹配位置列表（每行显示第几处 + 行号）</summary>
    private static string FormatMatchPositions(string normalizedContent, int oldTextLength, List<int> positions)
    {
        return string.Join("\n", positions.Select((p, i) =>
        {
            var lineNo = normalizedContent[..p].Count(c => c == '\n') + 1;
            return $"  第 {i + 1} 处：第 {lineNo} 行";
        }));
    }



    private class EditFileArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("old_text")]
        public string? OldText { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("new_text")]
        public string? NewText { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("use_regex")]
        public bool UseRegex { get; set; }
    }
}
