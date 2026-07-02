using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hearthaven.Core.Agent;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using Hearthaven.Core.Services;
using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;
using Hearthaven.Core.Utilities;
using Hearthaven.Diagnostics;
using Hearthaven.Models;
using Hearthaven.Services;
using Hearthaven.Utilities;
using System.IO;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace Hearthaven.ViewModels;

/// <summary>
/// 对话主 ViewModel — 负责 UI 属性绑定、命令和业务编排。
/// 消息构建委托给 <see cref="MessageBuilder"/>，缓存管理委托给 <see cref="SessionCacheManager"/>，
/// 流式 UI 更新委托给 <see cref="StreamUiUpdater"/>，会话生命周期委托给 <see cref="SessionManager"/>，
/// 消息加载委托给 <see cref="MessageLoader"/>，对话流程委托给 <see cref="ChatFlowOrchestrator"/>。
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IMessageRepository _messageRepo;
    private readonly ToolRegistry _toolRegistry;
    private readonly ContextManager _contextManager;
    private readonly IAgentService _agentService;
    private readonly SessionCache _cacheManager;
    private readonly StreamUpdater _streamUiUpdater;
    private readonly SessionService _sessionManager;
    private readonly MessageLoader _messageLoader;
    private readonly ChatFlowOrchestrator _flowOrchestrator;
    private readonly HearthavenSettings _settings;
    private string? _cachedSystemPrompt;

    // ──────────────── 常量 ────────────────

    private const int MaxInputHistory = 50;
    private const int PreviewMaxLength = 20;

    // ──────────────── 输入历史 ────────────────

    private readonly List<string> _inputHistory = [];
    private int _inputHistoryIndex = 0;
    private string? _savedDraft;

    // ──────────────── 非缓存状态（单会话） ────────────────

    /// <summary>UI 初始化完成标志（XAML 实例化后延迟初始化）</summary>
    private bool _initialized;

    // ──────────────── CollectionChanged 事件处理 ────────────────

    /// <summary>Messages.CollectionChanged 的命名 handler，保存引用以便取消订阅</summary>
    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyState));

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (MessageDisplayModel msg in e.NewItems)
            {
                var preview = msg.Content?.Length > PreviewMaxLength ? msg.Content[..PreviewMaxLength] : msg.Content;
                DebugLog.WriteLine(
                    $"[C002] Messages.Add: Role={msg.Role}, GroupId={msg.GroupId ?? "null"}, " +
                    $"ContentPreview={preview}");
                DebugLog.WriteLine(
                    $"[C002] Messages.Add StackTrace: ---BEGIN---");
                DebugLog.WriteLine(Environment.StackTrace);
                DebugLog.WriteLine(
                    $"[C002] Messages.Add StackTrace: ---END---");
            }
        }
    }

    // ──────────────── 可观察属性 ────────────────

    [ObservableProperty]
    private string _currentInput = "";

    /// <summary>
    /// 当前是否正在生成回复 — 从当前会话状态读取。
    /// </summary>
    public bool IsGenerating => CurrentState?.IsGenerating ?? false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenDisplayText))]
    [NotifyPropertyChangedFor(nameof(UsageBarBrush))]
    private int _tokenCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenDisplayText))]
    [NotifyPropertyChangedFor(nameof(UsageBarBrush))]
    [NotifyPropertyChangedFor(nameof(ContextWarningText))]
    [NotifyPropertyChangedFor(nameof(HasContextWarning))]
    [NotifyPropertyChangedFor(nameof(ContextWarningBrush))]
    private double _tokenUsageRatio;

    /// <summary>底部状态栏文本</summary>
    [ObservableProperty]
    private string _statusMessage = "就绪 💬";

    // ═══════════════════════ Phase 5: 工具栏 ═══════════════════════

    /// <summary>工作目录解析器（由 CompositionRoot 通过构造函数注入）</summary>
    public IWorkingDirectoryResolver DirResolver { get; }

    /// <summary>工作目录显示名称</summary>
    [ObservableProperty]
    private string _workingDirDisplayName = "Hearthaven";

    /// <summary>工作目录完整路径（用于 ToolTip）</summary>
    public string? WorkingDirFullPath => DirResolver?.Resolve(null);

    /// <summary>可用模型列表</summary>
    [ObservableProperty]
    private List<ModelConfig> _availableModels = [];

    /// <summary>当前选中的模型</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModelName))]
    private ModelConfig? _selectedModel;

    /// <summary>选中模型的名称（用于显示和保存到 DB）</summary>
    public string? SelectedModelName => SelectedModel?.Name;

    /// <summary>聊天模式（纯对话，无工具）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMode))]
    private bool _isModeChat = true;

    /// <summary>完整模式（工具 + 规则全开）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMode))]
    private bool _isModeFull;

    /// <summary>当前模式标识（"chat"/"full"）</summary>
    public string CurrentMode => IsModeFull ? "full" : "chat";

    /// <summary>上下文使用率告警提示文字（>=85% 时显示）</summary>
    public string? ContextWarningText => TokenUsageRatio switch
    {
        >= 0.95 => "🔴 上下文即将耗尽！建议开启新会话",
        >= 0.85 => "🟡 上下文已使用较多，建议开启新会话",
        _ => null
    };

    /// <summary>是否有上下文告警</summary>
    public bool HasContextWarning => ContextWarningText != null;

    /// <summary>上下文告警文字颜色：>=95% 红色，>=85% 橙色，<85% 绿色</summary>
    public Brush ContextWarningBrush => TokenUsageRatio switch
    {
        >= 0.95 => RedBrush,
        >= 0.85 => OrangeBrush,
        _ => GreenBrush
    };

    /// <summary>当前消息列表</summary>
    public SmartObservableCollection<MessageDisplayModel> Messages { get; } = [];

    /// <summary>当前会话 ID（委托给 SessionManager）</summary>
    public string CurrentSessionId => _sessionManager.CurrentSessionId;

    /// <summary>当前会话的运行时状态（委托给 SessionManager）</summary>
    public SessionState? CurrentState => _sessionManager.CurrentState;

    /// <summary>当前会话标题（委托给 SessionManager）</summary>
    public string CurrentTitle => _sessionManager.CurrentTitle;

    /// <summary>发送按钮是否可用 — 始终可用（生成中发送的是追加消息）</summary>
    public bool CanSendEnabled => true;

    /// <summary>[A8] 输入框占位文字 — 生成期间提示追加消息</summary>
    public string InputPlaceholder => IsGenerating
        ? "💬 追加消息（Enter 发送）..."
        : "输入消息...（Enter 发送）";

    /// <summary>当前会话状态变化时统一通知所有相关 UI 属性</summary>
    private void NotifyCurrentStateChanged()
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanSendEnabled));
        OnPropertyChanged(nameof(InputPlaceholder));
        OnPropertyChanged(nameof(CurrentTitle));
        SendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>是否显示空状态引导页（无消息 + 未在生成时）</summary>
    public bool ShowEmptyState => _initialized && Messages.Count == 0 && !IsGenerating;

    // ──────────────── Token 状态 ────────────────

    /// <summary>显示文字：如 "1,248 / 8,000 tokens"（从配置动态读取）</summary>
    public string TokenDisplayText =>
        $"{FormatHelper.FormatTokenSize(TokenCount)} / {FormatHelper.FormatTokenSize(_contextManager.MaxContextTokens)} tokens";

    // 三段色缓存（优先从 XAML 资源加载，有 fallback）
    private static Brush? _greenBrush;
    private static Brush? _orangeBrush;
    private static Brush? _redBrush;

    private static Brush GreenBrush => _greenBrush ??= GetTokenBrush("BrushTokenGreen", 76, 175, 80);
    private static Brush OrangeBrush => _orangeBrush ??= GetTokenBrush("BrushTokenOrange", 255, 152, 0);
    private static Brush RedBrush => _redBrush ??= GetTokenBrush("BrushTokenRed", 244, 67, 54);

    private static Brush GetTokenBrush(string resourceKey, byte r, byte g, byte b)
    {
        var brush = Application.Current?.TryFindResource(resourceKey) as Brush
            ?? new SolidColorBrush(Color.FromRgb(r, g, b));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    /// <summary>进度条颜色：使用率低→绿，中→橙，高→红</summary>
    public Brush UsageBarBrush => TokenUsageRatio switch
    {
        < 0.6 => GreenBrush,
        < 0.8 => OrangeBrush,
        _ => RedBrush
    };

    // ──────────────── 构造函数 ────────────────

    public ChatViewModel(
        ISessionRepository sessionRepo,
        IMessageRepository messageRepo,
        ToolRegistry toolRegistry,
        ContextManager contextManager,
        IAgentService agentService,
        SessionCache cacheManager,
        StreamUpdater streamUiUpdater,
        SessionService sessionManager,
        MessageLoader messageLoader,
        ChatFlowOrchestrator flowOrchestrator,
        HearthavenSettings settings,
        IWorkingDirectoryResolver dirResolver)
    {
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _toolRegistry = toolRegistry;
        _contextManager = contextManager;
        _agentService = agentService;
        _cacheManager = cacheManager;
        _streamUiUpdater = streamUiUpdater;
        _sessionManager = sessionManager;
        _messageLoader = messageLoader;
        _flowOrchestrator = flowOrchestrator;
        _settings = settings;
        DirResolver = dirResolver;

        Messages.CollectionChanged += OnMessagesCollectionChanged;
    }

    /// <summary>注册所有内置工具（由 CompositionRoot 在构造 ChatViewModel 前调用）</summary>
    public static void RegisterBuiltInTools(ToolRegistry registry, IWorkingDirectoryResolver dirResolver)
    {
        // ── 📄 文件操作 ──
        registry.Register(new ReadFileTool(dirResolver));
        registry.Register(new WriteFileTool(dirResolver));
        registry.Register(new EditFileTool(dirResolver));
        registry.Register(new BatchEditFileTool(dirResolver));
        registry.Register(new ListFilesTool(dirResolver));
        // ── 🔍 搜索 ──
        registry.Register(new SearchFilesTool(dirResolver));
        registry.Register(new FindFileTool(dirResolver));
        // ── 🗂️ 导航查看 ──
        registry.Register(new DirectoryTreeTool(dirResolver));
        // ── 📌 检查点 ──
        registry.Register(new CheckpointListTool());
        registry.Register(new CheckpointRestoreTool());

        // ── ⚡ 命令执行 ──
        registry.Register(new ExecCommandTool(dirResolver));
        // ── 🧰 辅助工具 ──
        registry.Register(new NowTimeTool());
        registry.Register(new CalculatorTool());
    }

    /// <summary>
    /// 在 UI 加载完成后调用（替代构造函数中 fire-and-forget）
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;

        // 构建 WorkRuleLoader 并注入 FlowOrchestrator
        var userDataDir = Path.GetDirectoryName(_settings.DbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        _flowOrchestrator.WorkRuleLoader = new WorkRuleLoader(DirResolver, userDataDir);
        _flowOrchestrator.Mode = "chat";

        // 初始化工具栏状态
        AvailableModels = _settings.Models;
        SelectedModel = AvailableModels.FirstOrDefault(m => m.Name == _settings.DefaultModel);
        UpdateWorkingDirDisplay();

        // 不创建会话，显示空状态引导页。首次发送消息时才会创建。
        RefreshCachedSystemPrompt();

        // 配置 ChatFlowOrchestrator 委托（通过 OrchestratorDependencies record 一次性注入）
        _flowOrchestrator.Configure(new OrchestratorDependencies
        {
            GetCachedSystemPrompt = () => _cachedSystemPrompt!,
            SetStatusMessage = msg => StatusMessage = msg,
            OnContextReady = (count, ratio) => { TokenCount = count; TokenUsageRatio = ratio; },
            NotifyCurrentStateChanged = NotifyCurrentStateChanged,
            RefreshTokenInfoAsync = RefreshTokenInfoAsync,
            CheckWindowMinimized = () =>
                Application.Current.MainWindow.WindowState == WindowState.Minimized
                || !Application.Current.MainWindow.IsVisible,
            RestoreInputText = text => CurrentInput = text
        });

        TokenCount = 0;
        TokenUsageRatio = 0;

        OnPropertyChanged(nameof(ShowEmptyState));

        return Task.CompletedTask;
    }

    /// <summary>当前会话切换事件（委托给 SessionManager）</summary>
    public event Action<string>? SessionChanged
    {
        add => _sessionManager.SessionChanged += value;
        remove => _sessionManager.SessionChanged -= value;
    }

    // ──────────────── 清除 / 新建会话 ────────────────

    [RelayCommand]
    internal async Task ClearChatAsync()
    {
        await _sessionManager.ClearChatAsync(Messages, () =>
        {
            _messageLoader.EarliestLoadedId = long.MaxValue;
            _messageLoader.LoadedAll = true;
            TokenCount = 0;
            TokenUsageRatio = 0;
        });
    }

    /// <summary>新建一个空白会话并切换到它（由侧边栏 ✚ 按钮触发）</summary>
    public async Task<string> CreateNewSessionAsync()
    {
        return await _sessionManager.CreateNewSessionAsync(Messages, _messageLoader.EarliestLoadedId, _messageLoader.LoadedAll, newId =>
        {
            _messageLoader.EarliestLoadedId = long.MaxValue;
            _messageLoader.LoadedAll = true;
            TokenCount = 0;
            TokenUsageRatio = 0;

            // 重置工具栏状态为默认值（新会话的 DB 记录中这些字段为 null/default）
            DirResolver!.SetWorkingDirectory(null);
            UpdateWorkingDirDisplay();
            _flowOrchestrator.Mode = "chat";
            IsModeChat = true;
            IsModeFull = false;
            SelectedModel = AvailableModels.FirstOrDefault(m => m.Name == _settings.DefaultModel);
            RefreshCachedSystemPrompt();

            NotifyCurrentStateChanged();
        });
    }

    /// <summary>切换到指定会话</summary>
    public async Task SwitchSessionAsync(string sessionId)
    {
        _flowOrchestrator.IncrementSessionVersion();
        var shouldUpdateState = true;
        await _sessionManager.SwitchSessionAsync(
            sessionId, Messages, _messageLoader.EarliestLoadedId, _messageLoader.LoadedAll,
            loadFromCacheAsync: async (sid) =>
            {
                // 恢复该会话的分页游标
                _sessionManager.TryGetCursor(sid, out var cursorEarliestId, out var cursorLoadedAll);
                _messageLoader.EarliestLoadedId = cursorEarliestId;
                _messageLoader.LoadedAll = cursorLoadedAll;
                PopulateMessageCallbacks();
                shouldUpdateState = false;
                await Task.CompletedTask;
            },
            loadInitialAsync: async () =>
            {
                await _messageLoader.LoadInitialMessagesAsync(
                    Messages, sessionId, PopulateMessageCallbacks);

                // [Fix] 从 DB 加载后立即写入缓存，确保侧边栏预览可用
                _sessionManager.SaveMessages(
                    sessionId, Messages,
                    _messageLoader.EarliestLoadedId, _messageLoader.LoadedAll);
            },
            onSessionLoaded: async () =>
            {
                if (shouldUpdateState)
                {
                    NotifyCurrentStateChanged();
                }
                else
                {
                    // 缓存加载时仍要通知 CurrentTitle 变化
                    OnPropertyChanged(nameof(CurrentTitle));
                }
                await RefreshTokenInfoAsync();

                // 从 DB 加载当前会话的工作目录/模型/模式
                var session = await _sessionRepo.GetByIdAsync(sessionId);
                if (session != null)
                {
                    // 工作目录：还原会话自己的工作目录（null = 使用默认用户数据目录）
                    DirResolver!.SetWorkingDirectory(session.WorkingDirectory);
                    UpdateWorkingDirDisplay();

                    // 模型
                    if (!string.IsNullOrEmpty(session.ModelName))
                    {
                        var model = AvailableModels.FirstOrDefault(m => m.Name == session.ModelName);
                        if (model != null) SelectedModel = model;
                    }

                    // 模式（兼容旧值 normal/code/creative → chat）
                    if (!string.IsNullOrEmpty(session.Mode))
                    {
                        var mode = session.Mode switch
                        {
                            "full" => "full",
                            _ => "chat" // normal / code / creative → chat
                        };
                        _flowOrchestrator.Mode = mode;
                        IsModeChat = mode == "chat";
                        IsModeFull = mode == "full";
                        RefreshCachedSystemPrompt();
                    }
                }
            });
    }

    // ──────────────── 消息加载 ────────────────

    /// <summary>给 Messages 中的所有消息设置回调（重新生成/编辑/保存）</summary>
    private void PopulateMessageCallbacks()
    {
        _messageLoader.PopulateMessageCallbacks(Messages,
            regenerateAsync: async (groupId) => await RegenerateAsync(groupId),
            saveEditAsync: async (msg) => await SaveEditedMessageAsync(msg));
    }

    // ──────────────── 命令 ────────────────

    [RelayCommand]
    internal async Task SendAsync()
    {
        var input = CurrentInput;
        if (string.IsNullOrEmpty(input?.Trim())) return;

        AddInputHistory(input);
        CurrentInput = "";
        await _flowOrchestrator.SendAsync(input, Messages);
    }

    /// <summary>将输入加入历史记录，重复内容不重复添加</summary>
    private void AddInputHistory(string input)
    {
        if (_inputHistory.Count > 0 && _inputHistory[^1] == input) return;

        _inputHistory.Add(input);
        if (_inputHistory.Count > MaxInputHistory)
            _inputHistory.RemoveAt(0);
        _inputHistoryIndex = _inputHistory.Count;
        _savedDraft = null;
    }

    /// <summary>Ctrl+↑ 浏览上一条历史输入</summary>
    internal void NavigateHistoryUp()
    {
        if (_inputHistory.Count == 0 || _inputHistoryIndex <= 0) return;

        if (_inputHistoryIndex == _inputHistory.Count)
            _savedDraft = CurrentInput;

        _inputHistoryIndex--;
        CurrentInput = _inputHistory[_inputHistoryIndex];
    }

    /// <summary>Ctrl+↓ 浏览下一条历史输入</summary>
    internal void NavigateHistoryDown()
    {
        if (_inputHistoryIndex >= _inputHistory.Count) return;

        _inputHistoryIndex++;
        CurrentInput = _inputHistoryIndex < _inputHistory.Count
            ? _inputHistory[_inputHistoryIndex]
            : _savedDraft ?? "";
    }

    [RelayCommand]
    private void Stop()
    {
        _flowOrchestrator.Stop();
    }

    // ═══════════════════════ Phase 5: 工具栏命令 ═══════════════════════

    /// <summary>在资源管理器中打开工作目录</summary>
    [RelayCommand]
    private void OpenWorkingDir()
    {
        if (DirResolver == null) return;
        var path = DirResolver.Resolve(null);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            System.Diagnostics.Process.Start("explorer.exe", path);
    }

    /// <summary>切换工作目录（弹出文件夹选择对话框）</summary>
    [RelayCommand]
    private async Task SwitchWorkingDirAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        dialog.FolderName = DirResolver!.Resolve(null);
        dialog.Title = "选择工作目录";

        if (dialog.ShowDialog() == true)
        {
            var sessionId = _sessionManager.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId)) return;

            await _sessionRepo.UpdatePropertiesAsync(sessionId, workingDirectory: dialog.FolderName);
            DirResolver!.SetWorkingDirectory(dialog.FolderName);
            UpdateWorkingDirDisplay();
            RefreshCachedSystemPrompt();
            StatusMessage = $"📂 工作目录已切换到 {dialog.FolderName}";
        }
    }

    /// <summary>切换对话模式</summary>
    [RelayCommand]
    private async Task SwitchModeAsync(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;

        try
        {
            _flowOrchestrator.Mode = mode;
            RefreshCachedSystemPrompt();

            // 状态栏反馈
            var modeName = mode == "full" ? "完整" : "聊天";
            StatusMessage = $"🎯 已切换到{modeName}模式";

            var sessionId = _sessionManager.CurrentSessionId;
            if (!string.IsNullOrEmpty(sessionId))
                await _sessionRepo.UpdatePropertiesAsync(sessionId, mode: mode);
        }
        catch (Exception ex)
        {
            Diagnostics.DebugLog.WriteLine($"[SwitchMode] 切换模式异常: {ex.Message}");
            StatusMessage = $"⚠️ 模式切换失败: {ex.Message}";
        }
    }

    /// <summary>切换模型</summary>
    [RelayCommand]
    private async Task SwitchModelAsync(ModelConfig? model)
    {
        if (model == null) return;
        SelectedModel = model;

        // 更新 AgentService 的当前模型名
        _agentService.CurrentModelName = model.Name;

        // 状态栏反馈
        StatusMessage = $"🤖 已切换到 {model.Name}";

        // 保存到 DB
        var sessionId = _sessionManager.CurrentSessionId;
        if (!string.IsNullOrEmpty(sessionId))
            await _sessionRepo.UpdatePropertiesAsync(sessionId, modelName: model.Name);
    }

    /// <summary>更新工作目录显示</summary>
    public void UpdateWorkingDirDisplay()
    {
        WorkingDirDisplayName = DirResolver?.GetDisplayDirectory() ?? "Hearthaven";
        OnPropertyChanged(nameof(WorkingDirFullPath));
    }

    // ──────────────── A5 重新生成回复 ────────────────

    private async Task RegenerateAsync(string groupId)
    {
        await _flowOrchestrator.RegenerateAsync(groupId, Messages);
    }

    // ──────────────── A6 编辑已发送消息 ────────────────

    private async Task SaveEditedMessageAsync(MessageDisplayModel userMsg)
    {
        await _flowOrchestrator.SaveEditedMessageAsync(userMsg);
    }


    /// <summary>模式属性变更自动同步到 FlowOrchestrator 并刷新 system prompt</summary>
    partial void OnIsModeChatChanged(bool value)
    {
        if (value && _initialized) _ = SwitchModeAsync("chat");
    }

    partial void OnIsModeFullChanged(bool value)
    {
        if (value && _initialized) _ = SwitchModeAsync("full");
    }

    /// <summary>模型选择变更自动同步</summary>
    partial void OnSelectedModelChanged(ModelConfig? value)
    {
        if (value != null && _initialized) _ = SwitchModelAsync(value);
    }

    /// <summary>刷新缓存的系统提示词（在模式/工作规则/工作目录变更后调用）</summary>
    private void RefreshCachedSystemPrompt()
    {
        var workRules = _flowOrchestrator.WorkRuleLoader?.LoadWorkRules();
        var workingDir = DirResolver?.Resolve(null);

        _cachedSystemPrompt = SystemPromptBuilder.Build(
            _toolRegistry, UserProfileManager.ReadProfile(),
            workRules, _flowOrchestrator.Mode, workingDir);
    }

    /// <summary>查看系统提示词</summary>
    [RelayCommand]
    private void ShowSystemPrompt()
    {
        var window = new Views.SystemPromptWindow
        {
            PromptText = _cachedSystemPrompt ?? SystemPromptBuilder.Build(
                _toolRegistry, mode: _flowOrchestrator.Mode)
        };
        window.ShowDialog();
    }

    /// <summary>按轮次删除消息</summary>
    [RelayCommand]
    private async Task DeleteMessageGroup(string? groupId)
    {
        if (string.IsNullOrEmpty(groupId) || IsGenerating) return;

        // 从 DB 删除该轮次的所有消息
        await _messageRepo.DeleteByGroupIdAsync(CurrentSessionId, groupId);

        // 从 UI 列表移除
        var toRemove = Messages.Where(m => m.GroupId == groupId).ToList();
        foreach (var msg in toRemove)
        {
            msg.Dispose();
            Messages.Remove(msg);
        }

        // 删除后刷新 Token 统计（DB 中 SUM 聚合已自动排除被删消息）
        await RefreshTokenInfoAsync();
    }

    /// <summary>
    /// 刷新当前会话的 Token 统计。
    /// 轻量估算：对 DB 中预存的 TokenCount 做 SUM 聚合 + 在线计算 system prompt 的 Token。
    /// 不再加载任何消息到内存，切换长会话时不会卡顿。
    /// </summary>
    private async Task RefreshTokenInfoAsync()
    {
        var sumTokens = await _messageRepo.SumTokenCountAsync(CurrentSessionId);

        // 加上 system prompt 的 Token
        var systemPrompt = _cachedSystemPrompt ?? SystemPromptBuilder.Build(_toolRegistry);
        var systemTokens = _contextManager.CountTokenString(systemPrompt ?? "");
        var totalTokens = (int)Math.Min(sumTokens + systemTokens, int.MaxValue);

        TokenCount = totalTokens;
        TokenUsageRatio = _contextManager.MaxContextTokens > 0
            ? (double)totalTokens / _contextManager.MaxContextTokens
            : 0;
    }

    /// <summary>从缓存中获取指定会话的最后一条助手消息预览（供后台流完成时更新侧边栏）</summary>
    public string? GetCachedPreview(string sessionId) => _sessionManager.GetCachedPreview(sessionId);

    /// <summary>加载更早的历史消息（向上滚动时由 MainWindow 调用）</summary>
    public async Task LoadMoreHistoryAsync()
    {
        await _messageLoader.LoadMoreHistoryAsync(Messages, CurrentSessionId, PopulateMessageCallbacks);
    }

    /// <summary>清理被删除会话的缓存（取消流 + 释放消息）</summary>
    public void CleanupSessionCache(string sessionId)
    {
        _sessionManager.CleanupSessionCache(sessionId);

        // 如果清理的是当前会话，通知属性变化
        if (sessionId == CurrentSessionId)
        {
            NotifyCurrentStateChanged();
        }
    }

    /// <summary>取消 Messages.CollectionChanged 订阅（应用退出时调用）</summary>
    public void UnsubscribeMessagesCollectionChanged()
    {
        Messages.CollectionChanged -= OnMessagesCollectionChanged;
    }
}
