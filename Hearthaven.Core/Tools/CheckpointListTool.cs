using Hearthaven.Core.Utilities;
using System.Text;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 检查点列表工具 — 列出所有可恢复的备份点。
/// </summary>
public class CheckpointListTool : ITool
{
    public string Name => "checkpoint_list";
    public string Description => "列出编辑/写入文件时自动创建的所有备份检查点（按时间倒序，最多 30 条）。可指定 path 过滤。";
    public bool IsLongRunning => false;

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        return path != null ? $"检查点列表 [{path}]" : "检查点列表";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var match = System.Text.RegularExpressions.Regex.Match(result, @"共 (\d+) 个检查点");
        return new ToolResultViewData
        {
            SummaryTag = match.Success ? $"{match.Groups[1].Value} 个" : null
        };
    }

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "可选，只显示指定文件的检查点（完整路径）"
            }
        },
        required = Array.Empty<string>()
    };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<CheckpointListArgs>(argsJson);
            var filter = args?.Path?.Trim();

            var checkpoints = CheckpointManager.ListCheckpoints(filter);
            if (checkpoints.Count == 0)
            {
                var msg = filter != null
                    ? $"未找到 '{filter}' 的检查点"
                    : "暂无检查点";
                return Task.FromResult(ToolOutput.Success(msg));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"共 {checkpoints.Count} 个检查点：");
            sb.AppendLine();
            foreach (var cp in checkpoints)
            {
                var size = FormatHelper.FormatSize(cp.FileSize);
                sb.AppendLine($"  #{cp.Id}  {cp.CreatedAt:yyyy-MM-dd HH:mm:ss}  {cp.FileName}  ({size})");
                sb.AppendLine($"        原路径：{cp.OriginalPath}");
            }
            sb.AppendLine();
            sb.AppendLine("使用 checkpoint_restore 工具恢复，如：checkpoint_restore {\"id\":\"20260603_101530_001\"}");

            return Task.FromResult(ToolOutput.Success(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：{ex.Message}"));
        }
    }

    private class CheckpointListArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }
    }
}
