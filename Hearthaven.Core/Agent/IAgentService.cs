using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;

namespace Hearthaven.Core.Agent;

/// <summary>
/// Agent 循环事件回调 — 所有回调在 StreamChatWithToolsAsync 的上下文中触发，
/// 调用方（ChatViewModel）应处理回调更新 UI。
/// </summary>
public sealed class AgentEvents
{
    /// <summary>收到文本块（流式实时更新）</summary>
    public Action<string>? OnTextChunk { get; init; }

    /// <summary>收到思考块（DeepSeek reasoning）</summary>
    public Action<string>? OnThinkingChunk { get; init; }

    /// <summary>
    /// 流式接收到 tool_call 的第一个 chunk 时触发（参数还未完整）。
    /// UI 层可借此提前创建工具块、关闭文本流式态，避免用户感知"卡顿"。
    /// </summary>
    /// <param name="idx">工具调用在同批中的索引</param>
    /// <param name="name">工具名称</param>
    public Action<int, string>? OnToolCallChunkStarted { get; init; }

    /// <summary>工具开始执行</summary>
    /// <param name="idx">工具调用在同批中的索引</param>
    /// <param name="name">工具名称</param>
    /// <param name="arguments">工具参数 JSON</param>
    /// <param name="cts">AgentService 为此工具创建的独立取消令牌源，UI 层绑定到中止按钮</param>
    public Action<int, string, string, CancellationTokenSource?>? OnToolCallStart { get; init; }

    /// <summary>工具执行完成</summary>
    /// <param name="idx">工具调用在同批中的索引</param>
    /// <param name="name">工具名称</param>
    /// <param name="result">执行结果文本</param>
    /// <param name="isError">是否执行出错（红色 ❗）</param>
    /// <param name="isWarning">是否执行警告（黄色 ⚠️）</param>
    public Action<int, string, string, bool, bool>? OnToolCallEnd { get; init; }

    /// <summary>发生异常</summary>
    public Action<Exception>? OnError { get; init; }

    /// <summary>状态变更（如 "正在调用工具..."）</summary>
    public Action<string>? OnStatusChange { get; init; }

    /// <summary>上下文构建完成，报告 Token 使用情况</summary>
    /// <param name="tokenCount">当前上下文已用 Token 数</param>
    /// <param name="usageRatio">使用占比 (0~1)</param>
    public Action<int, double>? OnContextReady { get; init; }

    /// <summary>本轮 Agent Loop 完成（工具调用已全部执行完毕）</summary>
    /// <remarks>
    /// 多轮工具调用场景中，每轮完成后触发一次。
    /// UI 层可借此重置 RoundBlock 状态，开始新的一轮。
    /// </remarks>
    public Action? OnRoundComplete { get; init; }

    /// <summary>上下文被裁剪，通知 UI 当前 Token 使用情况</summary>
    /// <param name="trimmedCount">被裁剪掉的消息条数</param>
    /// <param name="remainingTokens">裁剪后的上下文 Token 数</param>
    public Action<int, int>? OnContextTrimmed { get; init; }

    /// <summary>
    /// [A8] 检查是否有待处理的追加消息。
    /// AgentService 在每轮工具执行完毕后调用，UI 层返回待注入的追加消息列表。
    /// 追加消息会在下一轮请求中自动注入上下文，不打断当前正在执行的工具。
    /// </summary>
    public Func<Task<List<ChatMessage>>>? OnCheckPendingFollowUp { get; init; }
}

/// <summary>
/// Agent 循环执行结果
/// </summary>
/// <param name="ContextMessages">本轮产生的所有上下文消息（需调用方持久化）</param>
public sealed record AgentResult(List<MessageEntity> ContextMessages);

/// <summary>
/// Agent 服务接口 — 定义 AI Agent 对话循环。
/// 实现类必须为纯业务逻辑，不依赖任何 WPF 类型。
/// </summary>
public interface IAgentService
{
    /// <summary>当前活跃模型名称，null 时使用配置中的 DefaultModel。由 ChatViewModel 在切换模型时设置。</summary>
    string? CurrentModelName { get; set; }

    /// <summary>
    /// 运行 Agent 循环。
    /// </summary>
    /// <param name="sessionId">当前会话 ID</param>
    /// <param name="userInput">用户输入</param>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="roundGroupId">轮次分组 ID（用于 UI 和 DB 一致分组）</param>
    /// <param name="events">事件回调（用于 UI 更新）</param>
    /// <param name="persistedUserMsg">[B11] 已持久化的用户消息实体（由调用方先存 DB 后传入），
    /// AgentService 不再重复写入 DB</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Agent 执行结果</returns>
    Task<AgentResult> RunAsync(
        string sessionId,
        string userInput,
        string systemPrompt,
        string roundGroupId,
        AgentEvents events,
        MessageEntity persistedUserMsg,
        CancellationToken ct = default);
}
