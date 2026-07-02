namespace Hearthaven.Core.Tools;

/// <summary>
/// 检查点恢复工具 — 将文件恢复到指定检查点的版本。
/// </summary>
public class CheckpointRestoreTool : ITool
{
    public string Name => "checkpoint_restore";
    public string Description => "将文件恢复到指定检查点的版本。如果文件编辑或写入出错，可用此工具恢复。先从 checkpoint_list 获取 id。";
    public bool IsLongRunning => false;

    public string GetDisplayTitle(string argsJson)
    {
        var id = Utilities.JsonHelper.ExtractString(argsJson, "id");
        return id != null ? $"恢复检查点 [{id}]" : "恢复检查点";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var restoreMatch = System.Text.RegularExpressions.Regex.Match(result, @"已恢复 (.+)");
        return new ToolResultViewData
        {
            SummaryTag = restoreMatch.Success ? "已恢复" : null
        };
    }

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            id = new
            {
                type = "string",
                description = "检查点 ID（从 checkpoint_list 获取），格式如 20260603_101530_001"
            }
        },
        required = new[] { "id" }
    };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<CheckpointRestoreArgs>(argsJson);
            if (string.IsNullOrWhiteSpace(args?.Id))
                return Task.FromResult(ToolOutput.Error("错误：缺少 id 参数"));

            var (success, message) = CheckpointManager.Restore(args.Id.Trim());
            return Task.FromResult(success
                ? ToolOutput.Success(message)
                : ToolOutput.Error(message));
        }
        catch (System.Text.Json.JsonException)
        {
            return Task.FromResult(ToolOutput.Error("错误：参数解析失败，需要提供 id 字符串参数"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：{ex.Message}"));
        }
    }

    private class CheckpointRestoreArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
