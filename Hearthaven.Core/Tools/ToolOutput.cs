namespace Hearthaven.Core.Tools;

/// <summary>
/// 工具执行结果的状态种类 — 由工具内部自行判断。
/// </summary>
public enum ToolResultKind
{
    /// <summary>执行成功</summary>
    Success,

    /// <summary>执行成功但结果不符合预期（如编辑工具未找到匹配文本），UI 显示黄色 ⚠️</summary>
    Warning,

    /// <summary>执行出错，UI 显示红色 ❗</summary>
    Error
}

/// <summary>
/// 工具执行的输出 — 包含文本内容和结果状态。
/// 由每个工具自行决定执行结果属于 Success / Warning / Error。
/// </summary>
/// <param name="Content">执行结果文本（注入 AI 上下文的内容）</param>
/// <param name="Kind">结果状态，默认 Success</param>
public sealed record ToolOutput(string Content, ToolResultKind Kind = ToolResultKind.Success)
{
    /// <summary>快捷创建成功结果</summary>
    public static ToolOutput Success(string content) => new(content, ToolResultKind.Success);

    /// <summary>快捷创建警告结果（如编辑工具未匹配到文本）</summary>
    public static ToolOutput Warning(string content) => new(content, ToolResultKind.Warning);

    /// <summary>快捷创建错误结果</summary>
    public static ToolOutput Error(string content) => new(content, ToolResultKind.Error);
}
