using Hearthaven.Core.Services;
using Hearthaven.Core.Utilities;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件写入工具 — 写入内容到指定文件（新建或覆盖）。
/// </summary>
public class WriteFileTool : ToolBase, ITool
{
    public string Name => "write_file";
    public string Description => "写入内容到指定文件（新建或覆盖）。最大 1MB。";
    public bool IsLongRunning => true;

    public WriteFileTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        return path != null ? $"写入文件 [{path}]" : "写入文件";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var sizeMatch = System.Text.RegularExpressions.Regex.Match(result, @"\(([\d.]+ [KM]B)\)");
        var linesMatch = System.Text.RegularExpressions.Regex.Match(result, @"共 (\d+) 行");
        var lines = linesMatch.Success && int.TryParse(linesMatch.Groups[1].Value, out var l) ? l : 0;
        return new ToolResultViewData
        {
            SummaryTag = sizeMatch.Success ? sizeMatch.Groups[1].Value : null,
            LinesAdded = lines
        };
    }

    /// <summary>最大允许写入的字节数（1MB）</summary>
    private const int MaxWriteBytes = 1024 * 1024;

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
            content = new
            {
                type = "string",
                description = "要写入的文件内容"
            }
        },
        required = new[] { "path", "content" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<WriteFileArgs>(argsJson);
            if (args?.Path == null)
                return ToolOutput.Error("错误：缺少 path 参数");
            if (args.Content == null)
                return ToolOutput.Error("错误：缺少 content 参数");

            // 内容大小检查
            var contentBytes = System.Text.Encoding.UTF8.GetByteCount(args.Content);
            if (contentBytes > MaxWriteBytes)
                return ToolOutput.Error($"错误：内容过大（{contentBytes} 字节），最多允许写入 {MaxWriteBytes} 字节（1MB）");

            var filePath = ResolvePath(args.Path.Trim());
            var fullPath = Path.GetFullPath(filePath);

            // 确保父目录存在（同步操作，极快）
            var parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // ── 写入前备份（已有文件才需要）──
            var fileName = Path.GetFileName(fullPath);
            ProgressCallback?.Invoke($"正在备份文件 [{fileName}]…");
            await CheckpointManager.BackupAsync(fullPath).ConfigureAwait(false);

            // ── 原子写入：先写临时文件，再替换原文件 ──
            var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                ProgressCallback?.Invoke($"正在写入文件 [{fileName}]…");
                await File.WriteAllTextAsync(tempPath, args.Content, new System.Text.UTF8Encoding(false), ct)
                    .ConfigureAwait(false);
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            var fileInfo = new FileInfo(fullPath);
            var lineCount = args.Content.Split('\n').Length;

            // 重置状态栏为"就绪"
            ProgressCallback?.Invoke("写入完成 💬");

            return ToolOutput.Success($"成功写入文件 '{fullPath}'（{FormatHelper.FormatSize(fileInfo.Length)}），共 {lineCount} 行");
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 path 和 content 参数");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：写入文件时发生异常 — {ex.Message}");
        }
    }



    private class WriteFileArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
