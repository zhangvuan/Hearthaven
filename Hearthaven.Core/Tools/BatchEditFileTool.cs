using Hearthaven.Core.Services;
using Hearthaven.Core.Utilities;
using System.Text;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 批量编辑工具 — 一次对多个文件进行 SEARCH/REPLACE 编辑。
/// 两阶段提交：全部验证通过后才写入磁盘，写入失败时自动回滚已修改的文件。
/// </summary>
public class BatchEditFileTool : ToolBase, ITool
{
    public string Name => "batch_edit_file";
    public string Description => "一次性批量替换多项内容，全部验证通过后才会统一写入。最多 20 项，仅支持精确匹配。";
    public bool IsLongRunning => true;

    public BatchEditFileTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var edits = Utilities.JsonHelper.ExtractString(argsJson, "edits");
        if (edits != null)
        {
            var count = System.Text.RegularExpressions.Regex.Matches(edits, "old_text").Count;
            return count > 0 ? $"批量编辑 {count} 项" : "批量编辑";
        }
        return "批量编辑";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var addedMatch = System.Text.RegularExpressions.Regex.Match(result, @"总计 \+(\d+) 行");
        var removedMatch = System.Text.RegularExpressions.Regex.Match(result, @"总计 -(\d+) 行");
        var added = addedMatch.Success && int.TryParse(addedMatch.Groups[1].Value, out var a) ? a : 0;
        var removed = removedMatch.Success && int.TryParse(removedMatch.Groups[1].Value, out var r) ? r : 0;

        // 提取 "共修改 X 项" 作为摘要标签
        var summaryMatch = System.Text.RegularExpressions.Regex.Match(result, @"共修改 (\d+) 项");
        var summaryTag = summaryMatch.Success ? $"共修改 {summaryMatch.Groups[1].Value} 项" : null;

        return new ToolResultViewData
        {
            SummaryTag = summaryTag,
            LinesAdded = added,
            LinesRemoved = removed
        };
    }

    /// <summary>最大允许编辑的文件大小（1MB）</summary>
    private const int MaxEditFileSize = 1024 * 1024;

    /// <summary>单次最多批量编辑数</summary>
    private const int MaxBatchEdits = 20;

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            edits = new
            {
                type = "array",
                description = "要执行的编辑列表（最多 20 项），所有编辑验证通过后才会统一写入",
                items = new
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
                            description = "要替换的原文（精确匹配，区分大小写，在忽略换行符差异的前提下匹配）"
                        },
                        new_text = new
                        {
                            type = "string",
                            description = "替换后的新内容"
                        }
                    },
                    required = new[] { "path", "old_text", "new_text" }
                }
            }
        },
        required = new[] { "edits" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<BatchEditArgs>(argsJson);
            if (args?.Edits == null || args.Edits.Count == 0)
                return ToolOutput.Error("错误：缺少 edits 参数，需要提供至少一项编辑");

            if (args.Edits.Count > MaxBatchEdits)
                return ToolOutput.Error($"错误：批量编辑最多支持 {MaxBatchEdits} 项，当前为 {args.Edits.Count} 项");

            // ──────────────────────────────────────────────
            // Phase 1：全部在内存中验证
            // ──────────────────────────────────────────────
            var preparedEdits = new List<PreparedEdit>(args.Edits.Count);
            var validationErrors = new List<string>();

            for (int i = 0; i < args.Edits.Count; i++)
            {
                var edit = args.Edits[i];
                var idx = i + 1; // 1-indexed for user messages

                // 基本参数检查
                if (string.IsNullOrWhiteSpace(edit.Path))
                {
                    validationErrors.Add($"第 {idx} 项：缺少 path");
                    continue;
                }
                if (edit.OldText == null)
                {
                    validationErrors.Add($"第 {idx} 项：缺少 old_text");
                    continue;
                }
                if (edit.NewText == null)
                {
                    validationErrors.Add($"第 {idx} 项：缺少 new_text");
                    continue;
                }

                var fullPath = Path.GetFullPath(ResolvePath(edit.Path.Trim()));

                // 文件存在性检查
                if (!File.Exists(fullPath))
                {
                    validationErrors.Add($"第 {idx} 项：文件不存在 '{fullPath}'");
                    continue;
                }

                // 文件大小检查
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > MaxEditFileSize)
                {
                    validationErrors.Add($"第 {idx} 项：文件过大（{FormatHelper.FormatSize(fileInfo.Length)}），仅支持编辑 {FormatHelper.FormatSize(MaxEditFileSize)} 以内的文件");
                    continue;
                }

                // 读取文件内容
                string content;
                try
                {
                    content = await File.ReadAllTextAsync(fullPath, System.Text.Encoding.UTF8, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    validationErrors.Add($"第 {idx} 项：读取文件失败 — {ex.Message}");
                    continue;
                }

                // 检测行尾风格
                var hasCrlf = content.Contains("\r\n");

                // 归一化到 LF 进行匹配
                var normalizedContent = content.Replace("\r\n", "\n");
                var normalizedOldText = edit.OldText.Replace("\r\n", "\n");

                // 查找匹配位置
                var matchPositions = new List<int>();
                int searchFrom = 0;
                while (true)
                {
                    var pos = normalizedContent.IndexOf(normalizedOldText, searchFrom, StringComparison.Ordinal);
                    if (pos < 0) break;
                    matchPositions.Add(pos);
                    searchFrom = pos + normalizedOldText.Length;
                }

                if (matchPositions.Count == 0)
                {
                    validationErrors.Add($"第 {idx} 项：在 '{edit.Path}' 中未找到匹配的文本");
                    continue;
                }

                if (matchPositions.Count > 1)
                {
                    validationErrors.Add($"第 {idx} 项：在 '{edit.Path}' 中有 {matchPositions.Count} 处匹配，请补充更多上下文使 old_text 唯一");
                    continue;
                }

                // 验证通过，记录准备数据
                preparedEdits.Add(new PreparedEdit
                {
                    Index = idx,
                    FullPath = fullPath,
                    OriginalContent = content,
                    HasCrlf = hasCrlf,
                    NormalizedContent = normalizedContent,
                    NormalizedOldText = normalizedOldText,
                    MatchPosition = matchPositions[0],
                    NewText = edit.NewText
                });
            }

            // Phase 1 验证结果
            if (validationErrors.Count > 0)
            {
                var errorSb = new StringBuilder();
                errorSb.AppendLine($"❌ 批量编辑验证失败（{validationErrors.Count} 项问题），未修改任何文件：");
                errorSb.AppendLine();
                foreach (var err in validationErrors)
                    errorSb.AppendLine($"  {err}");
                return ToolOutput.Error(errorSb.ToString().TrimEnd());
            }

            // ──────────────────────────────────────────────
            // Phase 2：全部写入（带回滚）
            // 按文件分组，同一文件的多项编辑累积执行，修复多项编辑同一文件时互相覆盖的问题。
            // ──────────────────────────────────────────────
            var results = new List<string>();
            int totalAdded = 0, totalRemoved = 0;
            var writtenPaths = new List<string>();     // 已写入的文件路径（用于回滚）
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 原内容快照

            try
            {
                // 按文件路径分组，每组内的编辑按传入顺序累积应用
                var fileGroups = preparedEdits
                    .GroupBy(pe => pe.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToList(); // 转为 List 以便获取总数
                var totalFiles = fileGroups.Count;
                var fileIndex = 0;

                foreach (var group in fileGroups)
                {
                    fileIndex++;
                    ct.ThrowIfCancellationRequested();
                    var filePath = group.Key;
                    var fileEdits = group.ToList();

                    ProgressCallback?.Invoke($"正在处理文件（{fileIndex}/{totalFiles}）…");

                    // 从原始内容开始累积替换
                    var first = fileEdits[0];
                    var hasCrlf = first.HasCrlf;
                    var currentContent = first.OriginalContent;

                    int fileAdded = 0, fileRemoved = 0;

                    foreach (var pe in fileEdits)
                    {
                        // 归一化当前内容进行匹配
                        var normalized = currentContent.Replace("\r\n", "\n");
                        var normalizedOldText = pe.NormalizedOldText;

                        var pos = normalized.IndexOf(normalizedOldText, StringComparison.Ordinal);
                        if (pos < 0)
                        {
                            throw new InvalidOperationException(
                                $"编辑项第 {pe.Index} 项：在 '{filePath}' 中找不到匹配的 old_text" +
                                "（可能在同文件的之前编辑中已被修改）");
                        }

                        // 执行替换（基于累积后的当前内容）
                        var newNormalized = normalized[..pos]
                            + pe.NewText
                            + normalized[(pos + normalizedOldText.Length)..];

                        currentContent = hasCrlf
                            ? newNormalized.Replace("\n", "\r\n")
                            : newNormalized;

                        // 统计
                        var oldLines = normalizedOldText.Split('\n').Length;
                        var newLinesCount = pe.NewText.Split('\n').Length;
                        fileAdded += newLinesCount;
                        fileRemoved += oldLines;
                    }

                    // 全部累积完成 → 一次性写入
                    var fileName = Path.GetFileName(filePath);
                    ProgressCallback?.Invoke($"正在备份文件 [{fileName}]（{fileIndex}/{totalFiles}）…");
                    await CheckpointManager.BackupAsync(filePath).ConfigureAwait(false);
                    originals[filePath] = first.OriginalContent;

                    var tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
                    try
                    {
                        ProgressCallback?.Invoke($"正在写入文件 [{fileName}]（{fileIndex}/{totalFiles}）…");
                        await File.WriteAllTextAsync(tempPath, currentContent, new System.Text.UTF8Encoding(false), ct)
                            .ConfigureAwait(false);
                        File.Move(tempPath, filePath, overwrite: true);
                    }
                    catch
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                        throw;
                    }

                    writtenPaths.Add(filePath);
                    totalAdded += fileAdded;
                    totalRemoved += fileRemoved;

                    results.Add($"  ✅ {filePath}（+{fileAdded} 行，-{fileRemoved} 行）");
                }

                // 全部成功
                var successSb = new StringBuilder();
                successSb.AppendLine($"✅ 批量编辑完成，共修改 {preparedEdits.Count} 项（总计 +{totalAdded} 行，-{totalRemoved} 行）");
                successSb.AppendLine();
                successSb.Append(string.Join("\n\n", results));

                // 重置状态栏为"就绪"
                ProgressCallback?.Invoke("批量编辑完成 💬");

                return ToolOutput.Success(successSb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                // ── 回滚：反向恢复已写入的文件 ──
                var rollbackSb = new StringBuilder();
                rollbackSb.AppendLine($"❌ 批量编辑写入失败，正在回滚已修改的 {writtenPaths.Count} 个文件...");

                int rollbackSuccess = 0, rollbackFail = 0;
                for (int i = writtenPaths.Count - 1; i >= 0; i--)
                {
                    var rollbackPath = writtenPaths[i];
                    try
                    {
                        if (originals.TryGetValue(rollbackPath, out var originalContent))
                        {
                            await File.WriteAllTextAsync(rollbackPath, originalContent, new System.Text.UTF8Encoding(false), ct)
                                .ConfigureAwait(false);
                            rollbackSuccess++;
                        }
                    }
                    catch (Exception rex)
                    {
                        rollbackFail++;
                        rollbackSb.AppendLine($"  ⚠️ 回滚失败：{rollbackPath} — {rex.Message}");
                    }
                }

                rollbackSb.AppendLine(rollbackFail == 0
                    ? $"✅ 已成功回滚 {rollbackSuccess} 个文件"
                    : $"⚠️ 回滚 {rollbackSuccess}/{rollbackSuccess + rollbackFail} 个文件（{rollbackFail} 个失败）");
                rollbackSb.AppendLine($"\n原始错误：{ex.Message}");

                return ToolOutput.Error(rollbackSb.ToString().TrimEnd());
            }
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 edits 数组参数");
        }
        catch (OperationCanceledException)
        {
            return ToolOutput.Warning("警告：批量编辑已被取消，未修改任何文件");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：批量编辑时发生异常 — {ex.Message}");
        }
    }

    // ──────────────── 内部类型 ────────────────

    private class BatchEditArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("edits")]
        public List<SingleEdit>? Edits { get; set; }
    }

    private class SingleEdit
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("old_text")]
        public string? OldText { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("new_text")]
        public string? NewText { get; set; }
    }

    /// <summary>Phase 1 验证通过后，为每项编辑准备的执行数据</summary>
    private sealed class PreparedEdit
    {
        public int Index { get; set; }
        public string FullPath { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public bool HasCrlf { get; set; }
        public string NormalizedContent { get; set; } = "";
        public string NormalizedOldText { get; set; } = "";
        public int MatchPosition { get; set; }
        public string NewText { get; set; } = "";
    }
}
