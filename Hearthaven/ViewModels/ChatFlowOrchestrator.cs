using Hearthaven.Core.Agent;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using Hearthaven.Core.Services;
using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;
using Hearthaven.Diagnostics;
using Hearthaven.Models;
using Hearthaven.Services;
using Hearthaven.Utilities;
using System.Net.Http;

namespace Hearthaven.ViewModels;

/// <summary>
/// 对话流程编排器 — 负责消息发送、重新生成、错误重试、追加消息等核心对话流程。
/// 不持有对 <see cref="ChatViewModel"/> 的任何引用，通过委托和参数解耦。
/// </summary>
public class ChatFlowOrchestrator
{
    private readonly IAgentService _agentService;
    private readonly IMessageRepository _messageRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly ToolRegistry _toolRegistry;
    private readonly SessionCache _cache;
    private readonly SessionService _sessionService;
    private readonly MessageLoader _messageLoader;
    private readonly StreamUpdater _streamUpdater;
    private readonly HearthavenSettings _settings;

    // ──────────────── [A2] 注入委托（通过 Configure 方法一次性设置） ────────────────
    private Func<string> _getCachedSystemPrompt = null!;
    private Action<string> _setStatusMessage = null!;
    private Action<int, double> _onContextReady = null!;
    private Action _notifyCurrentStateChanged = null!;
    private Func<Task> _refreshTokenInfoAsync = null!;
    private Func<bool> _checkWindowMinimized = null!;
    private Action<string>? _restoreInputText;

    /// <summary>
    /// 一次性注入所有委托依赖。必须在首次调用任何公共方法前调用。
    /// 编译器通过 required 关键字保证所有必填项都已赋值。
    /// </summary>
    public void Configure(OrchestratorDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _getCachedSystemPrompt = deps.GetCachedSystemPrompt;
        _setStatusMessage = deps.SetStatusMessage;
        _onContextReady = deps.OnContextReady;
        _notifyCurrentStateChanged = deps.NotifyCurrentStateChanged;
        _refreshTokenInfoAsync = deps.RefreshTokenInfoAsync;
        _checkWindowMinimized = deps.CheckWindowMinimized;
        _restoreInputText = deps.RestoreInputText;
    }

    /// <summary>工作规则加载器（由 ChatViewModel 设置，用于注入 CLAUDE.md 规则）</summary>
    public WorkRuleLoader? WorkRuleLoader { get; set; }

    /// <summary>当前对话模式（"chat"/"full"），由 ChatViewModel 维护</summary>
    public string Mode { get; set; } = "chat";
    public MessageDisplayModel? CurrentAssistantMsg { get; set; }

    /// <summary>会话版本号，每次切换会话时递增，用于防止异步回调操作已切换的会话</summary>
    private volatile int _sessionVersion;

    /// <summary>切换会话时调用，递增版本号使旧的回调自动失效</summary>
    public void IncrementSessionVersion() => Interlocked.Increment(ref _sessionVersion);

    public ChatFlowOrchestrator(
        IAgentService agentService,
        IMessageRepository messageRepo,
        ISessionRepository sessionRepo,
        ToolRegistry toolRegistry,
        SessionCache cache,
        SessionService sessionService,
        MessageLoader messageLoader,
        StreamUpdater streamUpdater,
        HearthavenSettings settings)
    {
        _agentService = agentService;
        _messageRepo = messageRepo;
        _sessionRepo = sessionRepo;
        _toolRegistry = toolRegistry;
        _cache = cache;
        _sessionService = sessionService;
        _messageLoader = messageLoader;
        _streamUpdater = streamUpdater;
        _settings = settings;
    }

    // ──────────────── 发送消息 ────────────────

    /// <summary>主发送流程</summary>
    public async Task SendAsync(
        string input,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        var trimmedInput = input.Trim();
        if (string.IsNullOrEmpty(trimmedInput)) return;

        // [A8] 生成中发送 → 走追加消息流程
        if (_sessionService.CurrentState?.IsGenerating == true)
        {
            await SendFollowUpAsync(trimmedInput, messages);
            return;
        }

        // 首次发送 → 创建新会话
        if (string.IsNullOrEmpty(_sessionService.CurrentSessionId))
        {
            var newId = await _sessionRepo.CreateAsync("新对话");
            _sessionService.CurrentSessionId = newId;
            _sessionService.CurrentState = _sessionService.GetOrCreateState(newId);
            _sessionService.CurrentTitle = "新对话";
            _sessionService.RaiseSessionChanged(newId);
        }

        // 准备发送
        var (sendingSessionId, roundGroupId) = TryPrepareSend(trimmedInput);

        // 创建气泡
        var (userMsg, assistantMsg) = CreateSendBubbles(trimmedInput, roundGroupId, messages);

        // 构造事件回调
        var (events, finalizeSend) = _streamUpdater.BuildAgentEvents(
            assistantMsg, _setStatusMessage, _onContextReady);

        var versionSnapshot = _sessionVersion;

        // [B11] 用户消息立即保存到 DB
        var userEntity = await _messageRepo.AddAsync(new MessageEntity
        {
            SessionId = sendingSessionId,
            Role = "user",
            Content = trimmedInput,
            GroupId = roundGroupId,
            Timestamp = DateTime.UtcNow
        });

        // [FIX] 将 DB 生成的 Id 回传给 UI 模型，确保编辑能正确保存、重新生成不会重复创建
        userMsg.MessageId = userEntity.Id;

        await ExecuteAgentCallAsync(
            sendingSessionId, versionSnapshot, trimmedInput, roundGroupId,
            userEntity, assistantMsg, events, finalizeSend, messages,
            _cache.CreateStreamCts(sendingSessionId, _settings.TimeoutSeconds).Token);
    }

    /// <summary>准备发送前的状态处理：设置生成标志、自动更新标题</summary>
    private (string sendingSessionId, string roundGroupId) TryPrepareSend(string input)
    {
        var sendingSessionId = _sessionService.CurrentSessionId;

        _cache.SetGenerating(sendingSessionId, true);
        _notifyCurrentStateChanged();

        // 新会话首条消息 → 自动更新标题为用户输入内容
        if (_sessionService.CurrentTitle == "新对话" && !string.IsNullOrEmpty(input))
        {
            var title = input.Length > 30 ? input[..30] + "…" : input;
            _sessionService.CurrentTitle = title;
            _ = _sessionRepo.UpdateTitleAsync(sendingSessionId, title);
        }

        var roundGroupId = Guid.NewGuid().ToString("N");
        return (sendingSessionId, roundGroupId);
    }

    /// <summary>创建用户和助手的消息气泡（含回调绑定）</summary>
    private (MessageDisplayModel userMsg, MessageDisplayModel assistantMsg) CreateSendBubbles(
        string input, string roundGroupId,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        var userMsg = new MessageDisplayModel("user", input) { GroupId = roundGroupId };
        userMsg.SaveEditCallback = async () => await SaveEditedMessageAsync(userMsg);
        messages.Add(userMsg);

        var assistantMsg = new MessageDisplayModel("assistant", "") { IsStreaming = true, GroupId = roundGroupId };
        assistantMsg.RegenerateCallback = async () => await RegenerateAsync(roundGroupId, messages);
        CurrentAssistantMsg = assistantMsg;
        messages.Add(assistantMsg);

        return (userMsg, assistantMsg);
    }

    // ──────────────── 异常处理 ────────────────

    /// <summary>处理超时错误（可重试）</summary>
    private async Task HandleTimeoutErrorAsync(
        Action finalizeSend, MessageDisplayModel assistantMsg,
        string sessionId, string groupId, string input,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        SetErrorState(finalizeSend, assistantMsg, "❌ 请求超时，请检查网络连接");
        await SaveFailureMarkAsync(sessionId, groupId, assistantMsg.Content);
        var retryInput = input;
        var retryGroupId = groupId;
        assistantMsg.RetryCallback = async () =>
        {
            assistantMsg.Dispose();
            messages.Remove(assistantMsg);
            await SendRetryAsync(retryInput, retryGroupId, messages);
        };
    }

    /// <summary>处理 API Key 无效错误</summary>
    private async Task HandleUnauthorizedErrorAsync(
        Action finalizeSend, MessageDisplayModel assistantMsg,
        string sessionId, string groupId)
    {
        SetErrorState(finalizeSend, assistantMsg, "❌ API Key 无效，请在设置中检查");
        await SaveFailureMarkAsync(sessionId, groupId, assistantMsg.Content);
    }

    /// <summary>处理上下文超限错误</summary>
    private async Task HandleContextLengthErrorAsync(
        Action finalizeSend, MessageDisplayModel assistantMsg,
        string sessionId, string groupId)
    {
        SetErrorState(finalizeSend, assistantMsg, "❌ 上下文已超出限制，建议开启新会话");
        await SaveFailureMarkAsync(sessionId, groupId, assistantMsg.Content);
    }

    /// <summary>处理通用错误（可重试）</summary>
    private async Task HandleGeneralErrorAsync(
        Action finalizeSend, MessageDisplayModel assistantMsg,
        string sessionId, string groupId, string input,
        SmartObservableCollection<MessageDisplayModel> messages, Exception ex)
    {
        SetErrorState(finalizeSend, assistantMsg, $"❌ 错误: {ex.Message}");
        await SaveFailureMarkAsync(sessionId, groupId, assistantMsg.Content);
        var retryInput = input;
        var retryGroupId = groupId;
        assistantMsg.RetryCallback = async () =>
        {
            assistantMsg.Dispose();
            messages.Remove(assistantMsg);
            await SendRetryAsync(retryInput, retryGroupId, messages);
        };
    }

    // ──────────────── 统一执行管道 ────────────────

    /// <summary>
    /// 执行 Agent 调用管道：设置生成标志 → 调用 RunAsync → 异常处理 → 收尾。
    /// 由 SendAsync / RegenerateAsync / SendRetryAsync 共享，消除三处重复的执行+异常处理代码。
    /// </summary>
    /// <param name="saveFailureOnCancel">取消时是否保存失败标记（SendRetryAsync 需要）</param>
    private async Task ExecuteAgentCallAsync(
        string sessionId,
        int versionSnapshot,
        string input,
        string groupId,
        MessageEntity? persistedUserMsg,
        MessageDisplayModel assistantMsg,
        AgentEvents events,
        Action finalizeSend,
        SmartObservableCollection<MessageDisplayModel> messages,
        CancellationToken ct,
        bool saveFailureOnCancel = false)
    {
        _cache.SetGenerating(sessionId, true);
        _notifyCurrentStateChanged();

        // [A3] 确保传给 AgentService.RunAsync 的 persistedUserMsg 始终非 null。
        // Retry 路径中用户消息可能已被删除，此时就地创建兜底实体。
        persistedUserMsg ??= new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Content = input,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await _agentService.RunAsync(
                sessionId, input, _getCachedSystemPrompt(),
                groupId, events, persistedUserMsg, ct);

            finalizeSend();
        }
        catch (OperationCanceledException)
        {
            SetErrorState(finalizeSend, assistantMsg, "⏹ 已停止");
            if (saveFailureOnCancel && persistedUserMsg != null)
                await SaveFailureMarkAsync(sessionId, groupId, assistantMsg.Content);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            await HandleTimeoutErrorAsync(finalizeSend, assistantMsg, sessionId, groupId, input, messages);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await HandleUnauthorizedErrorAsync(finalizeSend, assistantMsg, sessionId, groupId);
        }
        catch (Exception ex) when (ex.Message.Contains("context_length", StringComparison.OrdinalIgnoreCase))
        {
            await HandleContextLengthErrorAsync(finalizeSend, assistantMsg, sessionId, groupId);
        }
        catch (Exception ex)
        {
            await HandleGeneralErrorAsync(finalizeSend, assistantMsg, sessionId, groupId, input, messages, ex);
        }
        finally
        {
            await FinalizeAfterSend(sessionId, versionSnapshot, messages);

            // [B1] 生成结束后，将未被 AI 消费的追加消息撤回输入框
            var drained = _streamUpdater.DrainPendingFollowUps();
            if (drained.Count > 0 && _restoreInputText != null)
            {
                var combined = string.Join("\n", drained);
                _restoreInputText(combined);
                _setStatusMessage($"💬 有 {drained.Count} 条追加消息未发送，已撤回输入框");
            }
        }
    }

    // ──────────────── 停止 ────────────────

    /// <summary>停止当前流式生成</summary>
    public void Stop()
    {
        _cache.CancelStream(_sessionService.CurrentSessionId);
    }

    // ──────────────── 重新生成回复 ────────────────

    /// <summary>重新生成指定轮次的助手回复</summary>
    public async Task RegenerateAsync(
        string groupId,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        var sendingSessionId = _sessionService.CurrentSessionId;
        var versionSnapshot = _sessionVersion;
        if (string.IsNullOrEmpty(sendingSessionId) ||
            _sessionService.CurrentState?.IsGenerating == true)
            return;

        var assistantMsg = messages.FirstOrDefault(m => m.GroupId == groupId && m.Role == "assistant");
        if (assistantMsg == null) return;

        await _messageRepo.DeleteAssistantByGroupIdAsync(_sessionService.CurrentSessionId, groupId);

        assistantMsg.Dispose();
        messages.Remove(assistantMsg);

        var userMsg = messages.FirstOrDefault(m => m.GroupId == groupId && m.Role == "user");
        if (userMsg == null) return;
        var input = userMsg.Content;

        var persistedUserMsg = await _messageRepo.GetByIdAsync(userMsg.MessageId);

        var newAssistantMsg = new MessageDisplayModel("assistant", "")
        {
            IsStreaming = true,
            GroupId = groupId,
            RegenerateCallback = async () => await RegenerateAsync(groupId, messages)
        };
        CurrentAssistantMsg = newAssistantMsg;

        var userIndex = messages.IndexOf(userMsg);
        messages.Insert(userIndex + 1, newAssistantMsg);

        var (events, finalizeSend) = _streamUpdater.BuildAgentEvents(
            newAssistantMsg, _setStatusMessage, _onContextReady);

        await ExecuteAgentCallAsync(
            sendingSessionId, versionSnapshot, input, groupId,
            persistedUserMsg, newAssistantMsg, events, finalizeSend, messages,
            _cache.CreateStreamCts(sendingSessionId, _settings.TimeoutSeconds).Token);
    }

    // ──────────────── 编辑已保存消息 ────────────────

    /// <summary>保存用户消息的编辑内容到 DB</summary>
    public async Task SaveEditedMessageAsync(MessageDisplayModel userMsg)
    {
        if (string.IsNullOrEmpty(_sessionService.CurrentSessionId) || userMsg.MessageId == 0)
            return;

        await _messageRepo.UpdateContentAsync(userMsg.MessageId, userMsg.Content);
        await _refreshTokenInfoAsync();
    }

    // ──────────────── 错误重试 ────────────────

    /// <summary>重试发送：使用指定输入和 GroupId 重新生成回复</summary>
    public async Task SendRetryAsync(
        string input, string groupId,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        var sendingSessionId = _sessionService.CurrentSessionId;
        var versionSnapshot = _sessionVersion;
        if (string.IsNullOrEmpty(sendingSessionId) ||
            _sessionService.CurrentState?.IsGenerating == true)
            return;

        var newAssistantMsg = new MessageDisplayModel("assistant", "")
        {
            IsStreaming = true,
            GroupId = groupId,
            RegenerateCallback = async () => await RegenerateAsync(groupId, messages)
        };
        CurrentAssistantMsg = newAssistantMsg;

        var userMsg = messages.FirstOrDefault(m => m.GroupId == groupId && m.Role == "user");
        MessageEntity? persistedUserMsg = null;
        if (userMsg != null)
        {
            persistedUserMsg = await _messageRepo.GetByIdAsync(userMsg.MessageId);
            var userIndex = messages.IndexOf(userMsg);
            messages.Insert(userIndex + 1, newAssistantMsg);
        }
        else
        {
            messages.Add(newAssistantMsg);
        }

        var (events, finalizeSend) = _streamUpdater.BuildAgentEvents(
            newAssistantMsg, _setStatusMessage, _onContextReady);

        // saveFailureOnCancel=true: 取消时保存失败标记（与其他两个方法的差异点）
        await ExecuteAgentCallAsync(
            sendingSessionId, versionSnapshot, input, groupId,
            persistedUserMsg, newAssistantMsg, events, finalizeSend, messages,
            _cache.CreateStreamCts(sendingSessionId, _settings.TimeoutSeconds).Token,
            saveFailureOnCancel: true);
    }

    // ──────────────── 追加消息 ────────────────

    /// <summary>生成期间发送追加消息</summary>
    public async Task SendFollowUpAsync(
        string input,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        if (string.IsNullOrEmpty(input)) return;

        var groupId = CurrentAssistantMsg?.GroupId ?? Guid.NewGuid().ToString("N");

        if (CurrentAssistantMsg != null)
        {
            int insertIdx;
            var lastFollowUp = CurrentAssistantMsg.TimelineItems
                .OfType<FollowUpBlock>().LastOrDefault();
            if (lastFollowUp != null)
            {
                insertIdx = CurrentAssistantMsg.TimelineItems.IndexOf(lastFollowUp) + 1;
            }
            else
            {
                var lastCompletedRound = CurrentAssistantMsg.TimelineItems
                    .OfType<RoundBlock>()
                    .LastOrDefault(r => !r.IsStreaming);
                insertIdx = lastCompletedRound != null
                    ? CurrentAssistantMsg.TimelineItems.IndexOf(lastCompletedRound) + 1
                    : CurrentAssistantMsg.TimelineItems.Count;
            }
            CurrentAssistantMsg.TimelineItems.Insert(insertIdx, new FollowUpBlock
            {
                Content = input
            });
        }
        else
        {
            messages.Add(new MessageDisplayModel("user", input) { GroupId = groupId });
        }

        await _messageRepo.AddAsync(new MessageEntity
        {
            SessionId = _sessionService.CurrentSessionId,
            Role = "user",
            Content = input,
            GroupId = groupId,
            IsFollowUp = true,
            Timestamp = DateTime.UtcNow
        });

        _streamUpdater.EnqueueFollowUp(new ChatMessage("user", input));

        await _refreshTokenInfoAsync();
    }

    // ──────────────── 收尾处理 ────────────────

    /// <summary>发送完成后的收尾处理</summary>
    private Task FinalizeAfterSend(
        string sendingSessionId,
        int versionSnapshot,
        SmartObservableCollection<MessageDisplayModel> messages)
    {
        DebugLog.WriteLine(
            $"SendAsync.finally: IsGenerating=false, Messages.Count={messages.Count}, " +
            $"SessionId={sendingSessionId}, Title={_sessionService.CurrentTitle}");

        _cache.SetGenerating(sendingSessionId, false);
        _notifyCurrentStateChanged();

        // 如果用户已经切换到了其他会话，或版本号不匹配（切换后又切回），后台流完成后只清理状态
        if (sendingSessionId != _sessionService.CurrentSessionId || versionSnapshot != _sessionVersion)
        {
            _cache.CancelStream(sendingSessionId);
            return Task.CompletedTask;
        }

        // 窗口最小化或隐藏到托盘时，弹出 Toast 通知
        if (_checkWindowMinimized()
            && GetPreviewFromMessages(messages) is { } finalContent
            && finalContent != "⏹ 已停止")
        {
            var summary = finalContent.Length > 100
                ? finalContent[..100] + "…"
                : finalContent;
            Services.NotificationService.Show("炉心有新回复", summary);
        }

        _cache.CancelStream(sendingSessionId);

        // 将当前消息快照保存到缓存
        _cache.SaveMessages(sendingSessionId, [.. messages],
            _messageLoader.EarliestLoadedId, _messageLoader.LoadedAll);

        DebugLog.WriteLine(
            $"SendAsync.finally: About to invoke SessionChanged, sendingSessionId={sendingSessionId}");

        _sessionService.RaiseSessionChanged(sendingSessionId);

        DebugLog.WriteLine(
            $"SendAsync.finally: SessionChanged done, Messages.Count={messages.Count}");

        return Task.CompletedTask;
    }

    // ──────────────── 辅助 ────────────────

    /// <summary>统一设置错误状态：结束流式 + 设置错误内容</summary>
    private static void SetErrorState(Action finalizeSend, MessageDisplayModel msg, string content)
    {
        finalizeSend();
        msg.Content = content;
        msg.IsError = true;
    }

    /// <summary>将生成失败的标记写入 DB</summary>
    private async Task SaveFailureMarkAsync(string sessionId, string groupId, string content)
    {
        await _messageRepo.AddAsync(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = content,
            GroupId = groupId,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>从实时消息列表中获取最后一条助手消息的预览（不依赖缓存）</summary>
    private static string? GetPreviewFromMessages(SmartObservableCollection<MessageDisplayModel> messages)
    {
        var lastAssistant = messages.LastOrDefault(m => m.Role == "assistant" && !m.IsStreaming);
        if (lastAssistant == null) return null;

        if (!string.IsNullOrEmpty(lastAssistant.Content))
            return lastAssistant.Content;

        for (int i = lastAssistant.TimelineItems.Count - 1; i >= 0; i--)
        {
            if (lastAssistant.TimelineItems[i] is RoundBlock round && !string.IsNullOrEmpty(round.Content))
                return round.Content;
        }

        return null;
    }
}

/// <summary>
/// ChatFlowOrchestrator 所有注入委托的编译时容器。
/// 在 ChatViewModel.InitializeAsync 中通过 <see cref="ChatFlowOrchestrator.Configure"/> 一次性传入，
/// 编译器保证所有必填委托都已赋值，消除运行时因遗忘设置而抛 InvalidOperationException 的风险。
/// </summary>
public sealed record OrchestratorDependencies
{
    /// <summary>获取缓存的系统提示词</summary>
    public required Func<string> GetCachedSystemPrompt { get; init; }

    /// <summary>更新状态栏文字</summary>
    public required Action<string> SetStatusMessage { get; init; }

    /// <summary>更新 Token 统计（Token 数 + 使用占比）</summary>
    public required Action<int, double> OnContextReady { get; init; }

    /// <summary>通知所有当前会话相关 UI 属性变化</summary>
    public required Action NotifyCurrentStateChanged { get; init; }

    /// <summary>刷新 Token 统计信息</summary>
    public required Func<Task> RefreshTokenInfoAsync { get; init; }

    /// <summary>检查窗口是否最小化或隐藏（用于判断是否弹出 Toast 通知）</summary>
    public required Func<bool> CheckWindowMinimized { get; init; }

    /// <summary>将文本恢复到输入框（用于追加消息未被 AI 消费时撤回，可选）</summary>
    public Action<string>? RestoreInputText { get; init; }
}
