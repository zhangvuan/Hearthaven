namespace Hearthaven.Core.Tools;

/// <summary>
/// 获取当前日期和时间的工具。
/// 无参数，返回格式 "yyyy-MM-dd HH:mm:ss"。
/// </summary>
public class NowTimeTool : ITool
{
    public string Name => "now_time";
    public string Description => "获取当前日期和时间（含时区偏移）。无参数。";
    public bool IsLongRunning => false;

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public string GetDisplayTitle(string argsJson) => "查看时间";

    public ToolResultViewData FormatResult(string result) => new() { SummaryTag = result };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        var localTime = DateTime.Now;
        var offset = TimeZoneInfo.Local.GetUtcOffset(localTime);
        var offsetStr = offset.TotalHours >= 0
            ? $"UTC+{offset:hh\\:mm}"
            : $"UTC{offset:hh\\:mm}";
        return Task.FromResult(ToolOutput.Success($"{localTime:yyyy-MM-dd HH:mm:ss} ({offsetStr})"));
    }
}
