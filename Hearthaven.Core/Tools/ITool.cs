namespace Hearthaven.Core.Tools;

/// <summary>
/// 工具接口 — AI Agent 可执行的外部能力。
/// 实现此接口后通过 ToolRegistry 注册，即可被 AI 触发。
/// </summary>
public interface ITool
{
    /// <summary>工具名称（AI 通过此名称引用），如 "now_time"</summary>
    string Name { get; }

    /// <summary>工具描述（AI 理解工具用途），如 "获取当前日期和时间"</summary>
    string Description { get; }

    /// <summary>
    /// 获取参数 JSON Schema。
    /// 返回符合 OpenAI Function Calling 参数格式的 JSON Schema 对象。
    /// 无参工具返回 <c>new { type = "object", properties = new { } }</c>。
    /// </summary>
    object GetParametersSchema();

    /// <summary>
    /// 执行工具函数。
    /// </summary>
    /// <param name="argsJson">AI 传入的参数 JSON 字符串</param>
    /// <param name="ct">取消令牌（用户点击"停止生成"时取消，用于中断耗时工具执行）</param>
    /// <returns>工具执行输出（包含结果文本和状态种类）</returns>
    Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default);

    // ── UI 展示相关（纯数据处理，不依赖 WPF）──

    /// <summary>
    /// 从参数 JSON 构建显示标题。
    /// 如 "查看文件 [E:\path]"、"计算 (15+3)*2"。
    /// 默认回退 <see cref="Name"/>。
    /// </summary>
    string GetDisplayTitle(string argsJson) => Name;

    /// <summary>
    /// 格式化执行结果为结构化展示数据。
    /// 默认返回无摘要、无行数变更的空数据。
    /// </summary>
    ToolResultViewData FormatResult(string result) => new();

    /// <summary>
    /// 此工具是否可能耗时较长，需要显示"执行中"状态和中止按钮。
    /// 快速工具（毫秒级纯内存计算、简单 IO 等）返回 false。
    /// </summary>
    bool IsLongRunning => true;
}
