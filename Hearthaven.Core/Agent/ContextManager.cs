using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using SharpToken;
using static Hearthaven.Core.Chat.SseJsonContext;

namespace Hearthaven.Core.Agent;

/// <summary>
/// 上下文管理器 — 负责 Token 计数、上下文构建与裁剪策略。
/// 确保 API 调用始终在预算内，防止超长上下文导致费用飙升。
/// </summary>
public class ContextManager
{
    private readonly IMessageReader _messageRepo;
    private readonly int _maxContextTokens;
    private readonly int _maxResponseTokens;
    private readonly GptEncoding _encoding;

    /// <summary>当前上下文已用 Token 数（最近一次 BuildContextAsync 后的值）</summary>
    public int CurrentTokens { get; private set; }

    /// <summary>Token 使用占比 (0~1)</summary>
    public double UsageRatio => _maxContextTokens > 0
        ? (double)CurrentTokens / _maxContextTokens
        : 0;

    /// <summary>最大上下文 Token 数（从配置注入）</summary>
    public int MaxContextTokens => _maxContextTokens;

    public ContextManager(IMessageReader messageRepo,
        int maxContextTokens, int maxResponseTokens)
    {
        _messageRepo = messageRepo;
        _maxContextTokens = maxContextTokens;
        _maxResponseTokens = maxResponseTokens;
        _encoding = GptEncoding.GetEncoding("cl100k_base");
    }

    /// <summary>
    /// 构建上下文消息列表 — 分批反向加载策略：
    /// 1. 从最新消息开始，分批倒序加载（每次 pageSize 条）
    /// 2. 每加载一批就估算总 Token，够用即停
    /// 3. 追加新的用户消息
    /// 4. 添加 system prompt（如果提供）
    /// 5. 超出预算时自动裁剪最早的消息
    /// 
    /// 相比全量加载，长对话的 IO 和反序列化成本从 O(N) 降至 O(Token预算/平均消息Token)，
    /// 早期历史消息不会被加载（除非预算极大或消息极短）。
    /// </summary>
    public async Task<List<ChatMessage>> BuildContextAsync(
        string sessionId, string? systemPrompt, ChatMessage? newUserMessage = null)
    {
        // 1. 从最新消息开始分批反向加载
        var history = new List<ChatMessage>();
        const int pageSize = 20;
        long cursor = long.MaxValue;
        var budget = _maxContextTokens - _maxResponseTokens;

        while (true)
        {
            var batch = await _messageRepo.GetPagedBeforeAsync(sessionId, cursor, pageSize)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;

            var batchMessages = batch.Select(ToChatMessage).ToList();
            // 插入到头部（每批按 Id 正序，且比已加载的更早）
            history.InsertRange(0, batchMessages);

            // 估算当前总 Token（仅历史消息，不含 system prompt 和新消息）
            var estimatedTokens = CountContextTokens(history);

            // 已满足预算 → 停止加载更早消息
            if (estimatedTokens >= budget || batch.Count < pageSize)
                break;

            cursor = batch[0].Id;
        }

        // 2. 追加新消息
        if (newUserMessage != null)
            history.Add(newUserMessage);

        // 3. 构建完整上下文（含 system prompt）
        var fullContext = BuildFullContext(systemPrompt, history);

        // 4. 记录 Token 数
        CurrentTokens = CountContextTokens(fullContext);

        // 5. 超出预算则裁剪
        if (CurrentTokens > budget)
        {
            fullContext = TrimContext(fullContext);
            CurrentTokens = CountContextTokens(fullContext);
        }

        return fullContext;
    }

    /// <summary>
    /// 计算指定消息列表的总 Token 数。
    /// </summary>
    public int CountContextTokens(List<ChatMessage> messages)
    {
        return messages.Sum(CountTokens);
    }

    /// <summary>
    /// 计算单条 MessageEntity 的 Token 数（用于预计算 TokenCount 字段）。
    /// 与 CountTokens(ChatMessage) 保持一致的计数逻辑。
    /// </summary>
    public int CountMessageTokens(MessageEntity entity)
    {
        int count = 0;
        count += CountTokenString(entity.Role);
        count += CountTokenString(entity.Content ?? "");
        if (!string.IsNullOrEmpty(entity.ToolCallsJson))
            count += CountTokenString(entity.ToolCallsJson);
        if (!string.IsNullOrEmpty(entity.ReasoningContent))
            count += CountTokenString(entity.ReasoningContent);
        count += 4; // 每条消息的 overhead
        return count;
    }

    /// <summary>
    /// 检查已有 context 是否超出 Token 预算，超出则自动裁剪最早的消息轮次。
    /// 用于 Agent Loop 中上下文不断增长时的预算控制。
    /// 返回裁剪后的 context 列表（可能无变化）。
    /// </summary>
    public List<ChatMessage> EnsureContextWithinBudget(List<ChatMessage> context)
    {
        var tokens = CountContextTokens(context);
        CurrentTokens = tokens;

        if (tokens > _maxContextTokens - _maxResponseTokens)
        {
            context = TrimContext(context);
            CurrentTokens = CountContextTokens(context);
            return context;
        }
        return context;
    }

    // ──────────────── 内部方法 ────────────────

    private ChatMessage ToChatMessage(MessageEntity entity)
    {
        var msg = new ChatMessage(entity.Role, entity.Content)
        {
            ToolCallId = entity.ToolCallId
        };

        if (!string.IsNullOrEmpty(entity.ToolCallsJson))
        {
            try
            {
                msg.ToolCalls = System.Text.Json.JsonSerializer.Deserialize<List<ToolCallEntry>>(entity.ToolCallsJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContextManager] ToolCallsJson 反序列化失败: {ex.Message}");
            }
        }

        if (!string.IsNullOrEmpty(entity.ReasoningContent))
        {
            msg.ReasoningContent = entity.ReasoningContent;
        }

        return msg;
    }

    private List<ChatMessage> BuildFullContext(string? systemPrompt, List<ChatMessage> history)
    {
        var context = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            context.Add(new ChatMessage("system", systemPrompt));
        }

        context.AddRange(history);
        return context;
    }

    /// <summary>
    /// 裁剪策略：从最早的消息开始，每次移除完整的对话轮次，
    /// 直到总 Token 数 ≤ MaxContextTokens - MaxResponseTokens。
    /// 
    /// 以"轮次"为单位裁剪——完整轮次 = user + assistant(±tool_calls) + tool(可选)，
    /// 确保不会破坏 user-assistant-tool 的配对关系，避免 API 因角色链断裂而报错。
    /// [L003 FIX] 在极端配置（budget 极小）时，快速收敛而非逐轮低效循环。
    /// </summary>
    private List<ChatMessage> TrimContext(List<ChatMessage> messages)
    {
        var budget = _maxContextTokens - _maxResponseTokens;
        if (budget <= 0) budget = _maxContextTokens / 2;

        // 极端情况：budget 太小，直接以总 Token 的 1/4 为下限，避免逐轮低效
        var minThreshold = CountContextTokens(messages) / 4;
        if (budget < minThreshold)
            budget = minThreshold;

        // 始终保留 system prompt（第一条）
        var systemMsg = messages.Count > 0 && messages[0].Role == "system"
            ? messages[0]
            : null;

        // 要保留的消息（不含 system）
        var toKeep = systemMsg != null
            ? messages.Skip(1).ToList()
            : [.. messages];

        // 从最早的消息开始，每次移除一个完整的对话轮次
        while (toKeep.Count > 1 && CountContextTokens(toKeep) > budget)
        {
            var roundEnd = FindRoundEnd(toKeep, 0);

            // 保护：至少保留 1 轮完整的 user-assistant 配对（含 tool 消息）
            // roundEnd 决定了会移除 [0..roundEnd)，如果剩余条数少于 roundEnd 说明会伤到最后一轮
            if (roundEnd <= 0 || toKeep.Count - roundEnd < 2)
                break;

            toKeep.RemoveRange(0, roundEnd);
        }

        // 重组
        var result = new List<ChatMessage>();
        if (systemMsg != null)
            result.Add(systemMsg);
        result.AddRange(toKeep);

        return result;
    }

    /// <summary>
    /// 查找从 startIndex 开始的一个完整对话轮次的结束位置（不含）。
    /// 
    /// 规则：
    /// - 从 startIndex 的消息开始作为轮次起点
    /// - 遇到下一个 user 或 assistant（纯文本回复，无 tool_calls）时结束
    /// - 如果中间有 assistant(含 tool_calls)，必须连带其后的所有连续 tool 消息一起包含
    /// 
    /// 这样可确保移除的始终是完整轮次，不会破坏消息的配对链。
    /// </summary>
    private static int FindRoundEnd(List<ChatMessage> messages, int startIndex)
    {
        if (startIndex >= messages.Count) return startIndex;

        for (int i = startIndex; i < messages.Count; i++)
        {
            var msg = messages[i];

            // assistant 消息带有 tool_calls → 需要包含其后所有连续的 tool 消息
            if (msg.Role == "assistant" && msg.ToolCalls != null)
            {
                // 判断是否有实际的工具调用
                bool hasToolCalls = msg.ToolCalls is { Count: > 0 };

                if (hasToolCalls)
                {
                    // 跳过后面所有连续 tool 消息
                    while (i + 1 < messages.Count && messages[i + 1].Role == "tool")
                        i++;
                    return i + 1; // 包含最后一个 tool 消息
                }
            }

            // 遇到下一个 user 或 assistant（纯文本，无 tool_calls）→ 当前轮次结束
            if (i > startIndex && (msg.Role == "user" ||
                (msg.Role == "assistant" && msg.ToolCalls == null)))
            {
                return i;
            }
        }

        return messages.Count;
    }

    private int CountTokens(ChatMessage message)
    {
        int count = 0;

        // 角色 + 内容估算
        count += CountTokenString(message.Role);
        count += CountTokenString(message.Content ?? "");

        // tool_calls 额外 Token
        if (message.ToolCalls != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message.ToolCalls, Options);
            count += CountTokenString(json);
        }

        // reasoning_content
        if (!string.IsNullOrEmpty(message.ReasoningContent))
        {
            count += CountTokenString(message.ReasoningContent);
        }

        // 每条消息的 overhead（OpenAI 格式 ≈ 4 tokens）
        count += 4;

        return count;
    }

    /// <summary>计算一段纯文本的 Token 数（公开方法，供外部轻量估算使用）</summary>
    public int CountTokenString(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return _encoding.Encode(text).Count;
    }
}
