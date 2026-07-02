using System.Collections.Frozen;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 全局工具注册表 — 所有可被 AI 调用的工具都在此注册。
/// 单例使用，在应用启动时注册所有内置工具。
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册一个工具（同名工具会覆盖）</summary>
    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    /// <summary>获取所有已注册的工具</summary>
    public IReadOnlyList<ITool> GetAll() => _tools.Values.ToList();

    /// <summary>按名称查找工具（不区分大小写）</summary>
    public ITool? FindByName(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;
}
