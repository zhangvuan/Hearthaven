using System.Text;
using System.Text.Json;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using Hearthaven.Core.Settings;

namespace Hearthaven.Core.Agent;

/// <summary>
/// Agent 服务 — AI 对话循环的核心实现。
/// 不依赖任何 WPF 类型，可独立测试。
/// </summary>
public class AgentService : IAgentService
{
    private readonly IChatProvider _provider;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly ContextManager _contextManager;
    private readonly IMessagePersistenceService _messagePersistence;
    private readonly HearthavenSettings _settings;
    private readonly int _defaultMaxTokens;

    /// <summary>当前活跃模型名称，为 null 时使用 _settings.DefaultModel</summary>
    public string? CurrentModelName { get; set; }

    public AgentService(
        IChatProvider provider,
        ToolDispatcher toolDispatcher,
        ContextManager contextManager,
        IMessagePersistenceService messagePersistence,
        HearthavenSettings settings,
        int defaultMaxTokens = 8192)
    {
        _provider = provider;
        _toolDispatcher = toolDispatcher;
        _contextManager = contextManager;
        _messagePersistence = messagePersistence;
        _settings = settings;
        _defaultMaxTokens = defaultMaxTokens;
    }

    // ──────────────── 调试日志辅助 ────────────────

    // ──────────────── 主入口 ────────────────

    public async Task<AgentResult> RunAsync(
        string sessionId,
        string userInput,
        string systemPrompt,
        string roundGroupId,
        AgentEvents events,
        MessageEntity persistedUserMsg,
        CancellationToken ct = default)
    {
        // [A3] persistedUserMsg 由调用方（ChatFlowOrchestrator）在存 DB 后传入，始终非 null
        _messagePersistence.Reset(1);

        // 1. 准备消息列表 — 用户消息已由外层持久化，直接复用
        var contextMessages = new List<MessageEntity> { persistedUserMsg };

        // 2. 构建上下文（历史 + 新消息）
        // 用户消息已由外层持久化到 DB，BuildContextAsync 加载历史时会自动包含它，
        // 不再作为 newUserMessage 额外传入，避免上下文双发（BUG #17）。
        var context = await _contextManager.BuildContextAsync(sessionId, systemPrompt)
            .ConfigureAwait(false);

        // 🔔 通知 UI Token 使用情况
        events.OnContextReady?.Invoke(_contextManager.CurrentTokens, _contextManager.UsageRatio);

        // 3. Agent Loop
        while (!ct.IsCancellationRequested)
        {
            var result = await RunOneIterationAsync(context, contextMessages, events, sessionId, roundGroupId, ct)
                .ConfigureAwait(false);
            if (result.IsComplete)
                return result.AgentResult!;
            context = result.UpdatedContext!;

            // [A8] 每轮工具执行完毕后，检查是否有追加消息待注入
            if (events.OnCheckPendingFollowUp != null)
            {
                var followUps = await events.OnCheckPendingFollowUp().ConfigureAwait(false);
                if (followUps.Count > 0)
                {
                    context.AddRange(followUps);
                    // 追加消息已注入上下文，下一轮请求会自动包含
                }
            }
        }

        // 4. 退出时保存（while 循环因取消而正常退出）
        if (contextMessages.Count > 0)
        {
            await _messagePersistence.FlushIncrementalAsync(contextMessages, sessionId).ConfigureAwait(false);
        }

        return new AgentResult(contextMessages);
    }

    // ──────────────── 单轮迭代 ────────────────

    /// <summary>
    /// 执行一轮 Agent Loop 迭代：发送请求 → 接收流式响应 → 处理工具调用 → 上下文裁剪。
    /// 返回 IterationResult：IsComplete=true 表示对话结束（无工具调用），否则返回更新后的上下文。
    /// </summary>
    private async Task<IterationResult> RunOneIterationAsync(
        List<ChatMessage> context,
        List<MessageEntity> contextMessages,
        AgentEvents events,
        string sessionId,
        string roundGroupId,
        CancellationToken ct)
    {
        // 构建请求（优先使用 CurrentModelName，回退到 DefaultModel）
        var activeModel = CurrentModelName ?? _settings.DefaultModel;
        var request = new ChatRequest
        {
            Model = activeModel,
            Messages = context,
            Temperature = _settings.Temperature,
            MaxTokens = _defaultMaxTokens,
            Tools = _toolDispatcher.BuildDefinitions(_settings.Tools),
            ToolChoice = "auto"
        };

        // 流式接收
        var (sb, reasoningSb, pendingToolCalls) = await ProcessStreamResponse(_provider, request, events, ct)
            .ConfigureAwait(false);

        // 取消中断时收到了不完整的工具调用 → 通知 UI 收尾，跳过执行，避免残缺 JSON 报错
        if (ct.IsCancellationRequested && pendingToolCalls.Count > 0)
        {
            foreach (var (idx, (_, name, _)) in pendingToolCalls.OrderBy(kv => kv.Key))
            {
                if (!string.IsNullOrEmpty(name))
                    events.OnToolCallEnd?.Invoke(idx, name, "⏹ 已取消", false, true);
            }
            return new IterationResult(true, new AgentResult(contextMessages), null);
        }

        // 没有工具调用 → 结束
        if (pendingToolCalls.Count == 0)
        {
            return await HandleNonToolResponse(sb, reasoningSb, contextMessages, sessionId, roundGroupId)
                .ConfigureAwait(false);
        }

        // 有工具调用：拼接 ToolCallData
        var toolCalls = BuildToolCallsList(pendingToolCalls);

        // 将 assistant 消息（含 tool_calls）加入上下文和持久化列表
        AppendAssistantMessage(context, contextMessages, sb, reasoningSb, toolCalls, roundGroupId, sessionId);

        // 执行工具调用并处理结果
        await ExecuteToolCallsAsync(toolCalls, context, contextMessages, events, sessionId, roundGroupId, ct)
            .ConfigureAwait(false);

        // 工具执行后的收尾处理：上下文裁剪 → 通知 UI → 更新 Token → 增量保存 → 安全阀检查
        return await AfterToolExecutionCleanupAsync(context, contextMessages, events, sessionId, roundGroupId)
            .ConfigureAwait(false);
    }

    // ──────────────── SSE 流式接收 ────────────────

    /// <summary>
    /// 发送请求并流式接收 SSE 响应，返回解析后的文本、思考内容和待处理的工具调用。
    /// OperationCanceledException 在此方法内捕获，不会丢失已接收的数据。
    /// </summary>
    private static async Task<StreamResponse> ProcessStreamResponse(
        IChatProvider provider, ChatRequest request, AgentEvents events, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var reasoningSb = new StringBuilder();
        var pendingToolCalls = new Dictionary<int, (string? id, string? name, StringBuilder args)>();
        var notifiedIndices = new HashSet<int>(); // 已触发 OnToolCallChunkStarted 的索引

        try
        {
            await foreach (var evt in provider.StreamChatWithToolsAsync(request, ct).ConfigureAwait(false))
            {
                if (evt is TextChunk chunk)
                {
                    sb.Append(chunk.Content);
                    events.OnTextChunk?.Invoke(chunk.Content);
                }
                else if (evt is ThinkingChunk tChunk)
                {
                    reasoningSb.Append(tChunk.Content);
                    events.OnThinkingChunk?.Invoke(tChunk.Content);
                }
                else if (evt is ToolCallChunk tcc)
                {
                    if (!pendingToolCalls.TryGetValue(tcc.Index, out var entry))
                        entry = (null, null, new StringBuilder());

                    if (tcc.Id != null) entry.id = tcc.Id;
                    if (tcc.Name != null) entry.name = tcc.Name;
                    if (tcc.Arguments != null) entry.args.Append(tcc.Arguments);

                    pendingToolCalls[tcc.Index] = entry;

                    // 首次获取到工具名称时立即通知 UI，不等参数传输完毕
                    if (entry.name != null && notifiedIndices.Add(tcc.Index))
                    {
                        events.OnToolCallChunkStarted?.Invoke(tcc.Index, entry.name);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户点击"停止生成" — 已收到的数据正常处理，不丢失
        }

        return new StreamResponse(sb, reasoningSb, pendingToolCalls);
    }

    // ──────────────── 无工具调用处理 ────────────────

    /// <summary>
    /// 处理无工具调用的响应：保存 assistant 回复并持久化。
    /// </summary>
    private async Task<IterationResult> HandleNonToolResponse(
        StringBuilder sb, StringBuilder reasoningSb,
        List<MessageEntity> contextMessages, string sessionId, string roundGroupId)
    {
        if (sb.Length > 0)
        {
            var entity = new MessageEntity
            {
                SessionId = sessionId,
                Role = "assistant",
                Content = sb.ToString(),
                ReasoningContent = reasoningSb.Length > 0 ? reasoningSb.ToString() : null,
                GroupId = roundGroupId,
                Timestamp = DateTime.UtcNow
            };
            contextMessages.Add(entity);
        }

        await _messagePersistence.FlushIncrementalAsync(contextMessages, sessionId).ConfigureAwait(false);

        return new IterationResult(true, new AgentResult(contextMessages), null);
    }

    // ──────────────── 安全阀策略 ────────────────

    /// <summary>
    /// Token 使用率上限安全阀 — 上下文快满了就主动停止工具调用。
    /// 追加 system 消息告知 LLM 当前状态，让 LLM 简洁回复。
    /// 返回 true 表示安全阀触发了结束流程。
    /// </summary>
    private async Task<bool> HandleSafetyValveAsync(
        List<ChatMessage> context,
        List<MessageEntity> contextMessages,
        AgentEvents events,
        string sessionId,
        string roundGroupId,
        double updatedRatio)
    {
        if (updatedRatio < _settings.MaxToolTokenRatio)
            return false;

        var pct = (updatedRatio * 100).ToString("F0");
        events.OnStatusChange?.Invoke($"⚠️ 上下文已使用 {pct}%，Token 接近上限，停止工具调用");

        // 追加 system 消息告知 LLM 当前状态
        context.Add(new ChatMessage("system",
            $"[系统通知] Token 使用量已达到 {pct}%，无法继续执行工具。" +
            "请用一句话简洁总结已完成的进度，并告知用户需要开启新对话继续。"));

        var finalRequest = new ChatRequest
        {
            Model = CurrentModelName ?? _settings.DefaultModel,
            Messages = context,
            Temperature = _settings.Temperature,
            MaxTokens = 512,
            Tools = null,
            ToolChoice = null
        };

        // [B002 FIX] 使用独立的 CancellationTokenSource，避免用户点击"停止生成"时打断 final request
        using var finalCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (finalSb, _, _) = await ProcessStreamResponse(_provider, finalRequest, events, finalCts.Token)
            .ConfigureAwait(false);

        // 保存 LLM 的结束语回复
        if (finalSb.Length > 0)
        {
            contextMessages.Add(new MessageEntity
            {
                SessionId = sessionId,
                Role = "assistant",
                Content = finalSb.ToString(),
                ReasoningContent = null,
                GroupId = roundGroupId,
                Timestamp = DateTime.UtcNow
            });
        }

        await _messagePersistence.FlushIncrementalAsync(contextMessages, sessionId).ConfigureAwait(false);
        return true;
    }

    // ──────────────── 工具调用相关方法 ────────────────

    /// <summary>从流式接收的 pendingToolCalls 构建工具调用列表</summary>
    private static List<ToolCallData> BuildToolCallsList(
        Dictionary<int, (string? id, string? name, StringBuilder args)> pendingToolCalls)
    {
        return pendingToolCalls
            .OrderBy(kv => kv.Key)
            .Select(kv => new ToolCallData(
                kv.Value.id ?? "",
                kv.Value.name ?? "",
                kv.Value.args.ToString()))
            .ToList();
    }

    /// <summary>
    /// 执行工具调用并处理结果：创建独立 CTS → 通知 UI → 执行工具 → 将结果注入上下文和持久化列表。
    /// 每个工具独立超时（默认 120 秒），防止单个工具卡死拖垮整个 Agent Loop。
    /// </summary>
    private async Task ExecuteToolCallsAsync(
        List<ToolCallData> toolCalls,
        List<ChatMessage> context,
        List<MessageEntity> contextMessages,
        AgentEvents events,
        string sessionId,
        string roundGroupId,
        CancellationToken ct)
    {
        // [N1] 每工具 120 秒超时，防止工具卡住导致 Agent Loop 永久阻塞
        const int perToolTimeoutSeconds = 120;

        // 为每个工具创建独立 CTS 并同步通知 UI（避免旧方案中 UI 异步写入字典导致的竞态）
        var perToolCtsArray = new CancellationTokenSource[toolCalls.Count];
        try
        {
            for (int i = 0; i < toolCalls.Count; i++)
            {
                perToolCtsArray[i] = new CancellationTokenSource(TimeSpan.FromSeconds(perToolTimeoutSeconds));
                events.OnToolCallStart?.Invoke(i, toolCalls[i].FunctionName, toolCalls[i].Arguments, perToolCtsArray[i]);
            }

            // 提取 Token 列表，与全局 ct 一起传给调度器
            var perToolTokens = perToolCtsArray.Select(cts => cts.Token).ToList();

            // 执行工具（传入全局 ct，让 ⏹ 按钮也能中断工具执行）
            // 同时传入 OnStatusChange 作为进度回调，工具执行期间实时报告状态到 UI
            var results = await _toolDispatcher.ExecuteBatchAsync(toolCalls, perToolTokens, ct,
                    progressCallback: msg => events.OnStatusChange?.Invoke(msg))
                .ConfigureAwait(false);

            // 处理执行结果
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                events.OnToolCallEnd?.Invoke(i, result.ToolName, result.Content, result.IsError, result.IsWarning);

                context.Add(new ChatMessage("tool", result.Content, result.ToolCallId));
                contextMessages.Add(new MessageEntity
                {
                    SessionId = sessionId,
                    Role = "tool",
                    Content = result.Content,
                    ToolCallId = result.ToolCallId,
                    GroupId = roundGroupId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        finally
        {
            // 确保所有 CTS 被释放 — 工具执行完毕后 UI 不再需要这些 Token
            foreach (var cts in perToolCtsArray)
            {
                cts?.Dispose();
            }
        }
    }

    /// <summary>
    /// 工具执行后的收尾处理：上下文裁剪 → 通知 UI 本轮完成 → 更新 Token 进度条 → 增量保存 → 安全阀检查。
    /// 返回 IterationResult：IsComplete=true 表示对话结束（安全阀触发或无工具调用），否则返回更新后的上下文。
    /// </summary>
    private async Task<IterationResult> AfterToolExecutionCleanupAsync(
        List<ChatMessage> context,
        List<MessageEntity> contextMessages,
        AgentEvents events,
        string sessionId,
        string roundGroupId)
    {
        // 上下文裁剪
        var beforeCount = context.Count;
        context = _contextManager.EnsureContextWithinBudget(context);
        var afterCount = context.Count;

        if (afterCount < beforeCount)
        {
            var trimmedCount = beforeCount - afterCount;
            events.OnContextTrimmed?.Invoke(trimmedCount, _contextManager.CurrentTokens);
        }

        // 通知 UI 本轮已完成
        events.OnRoundComplete?.Invoke();

        // 同步更新 Token 进度条（EnsureContextWithinBudget 已维护 CurrentTokens）
        var updatedTokens = _contextManager.CurrentTokens;
        var updatedRatio = _contextManager.MaxContextTokens > 0
            ? (double)updatedTokens / _contextManager.MaxContextTokens
            : 0;
        events.OnContextReady?.Invoke(updatedTokens, updatedRatio);

        // [B11] 每轮工具执行完毕后增量保存到 DB，确保中间结果不丢失
        await _messagePersistence.FlushIncrementalAsync(contextMessages, sessionId).ConfigureAwait(false);

        // 安全阀：Token 使用率上限 — 上下文快满了就主动停止工具调用
        var safetyCompleted = await HandleSafetyValveAsync(context, contextMessages, events, sessionId, roundGroupId, updatedRatio)
            .ConfigureAwait(false);
        if (safetyCompleted)
        {
            return new IterationResult(true, new AgentResult(contextMessages), null);
        }

        return new IterationResult(false, null, context);
    }

    // ──────────────── 辅助方法 ────────────────

    /// <summary>将 assistant 消息（含 tool_calls）加入上下文和持久化列表</summary>
    private static void AppendAssistantMessage(
        List<ChatMessage> context,
        List<MessageEntity> contextMessages,
        StringBuilder sb,
        StringBuilder reasoningSb,
        List<ToolCallData> toolCalls,
        string roundGroupId,
        string sessionId)
    {
        var toolCallEntries = toolCalls.Select(tc => new ToolCallEntry
        {
            Id = tc.Id,
            Function = new ToolCallFunction
            {
                Name = tc.FunctionName,
                Arguments = tc.Arguments
            }
        }).ToList();

        context.Add(new ChatMessage("assistant", sb.ToString())
        {
            ToolCalls = toolCallEntries,
            ReasoningContent = reasoningSb.Length > 0 ? reasoningSb.ToString() : null
        });

        contextMessages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = sb.ToString(),
            ToolCallsJson = JsonSerializer.Serialize(toolCallEntries, SseJsonContext.Options),
            ReasoningContent = reasoningSb.Length > 0 ? reasoningSb.ToString() : null,
            GroupId = roundGroupId,
            Timestamp = DateTime.UtcNow
        });
    }



    // ──────────────── 内部类型 ────────────────

    /// <summary>单轮迭代的结果</summary>
    private sealed record IterationResult(bool IsComplete, AgentResult? AgentResult, List<ChatMessage>? UpdatedContext);

    /// <summary>SSE 流式接收的结果</summary>
    private sealed record StreamResponse(
        StringBuilder Text,
        StringBuilder Reasoning,
        Dictionary<int, (string? id, string? name, StringBuilder args)> PendingToolCalls);
}
