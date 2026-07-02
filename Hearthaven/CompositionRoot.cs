using System.IO;
using Hearthaven.Core.Agent;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using Hearthaven.Core.Services;
using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;
using Hearthaven.Data.Database;
using Hearthaven.Data.Repositories;
using Hearthaven.Services;
using Hearthaven.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;

namespace Hearthaven;

/// <summary>
/// Composition Root — 纯手工 DI 组装工厂。
/// 负责创建和组装应用程序的所有依赖对象，替代 MainWindow 中的内联手动 DI。
/// </summary>
public static class CompositionRoot
{
    /// <summary>AI Provider 实例，持有 HttpClient 生命周期，应用退出时释放。</summary>
    private static IDisposable? _provider;

    /// <summary>
    /// 已注册的工具列表 — 供设置窗口读取工具名称列表。
    /// 在 CompositionRoot.Create 中赋值。
    /// </summary>
    public static ToolRegistry? ToolRegistry { get; internal set; }

    /// <summary>
    /// 释放 CompositionRoot 创建的所有全局资源。
    /// 应在应用退出前调用（如 MainWindow Exit_Click）。
    /// </summary>
    public static void Shutdown()
    {
        _provider?.Dispose();
        _provider = null;
    }

    /// <summary>
    /// 根据配置创建完整的 ViewModel 依赖树。
    /// </summary>
    /// <param name="settings">运行时配置实例（由 App.xaml.cs 加载）</param>
    /// <returns>(主对话 VM, 侧边栏 VM)</returns>
    public static (ChatViewModel ChatVm, SessionListViewModel SidebarVm) Create(
        HearthavenSettings settings)
    {
        // ═══════════════════════ 数据层 ═══════════════════════

        var options = new DbContextOptionsBuilder<HearthavenDbContext>()
            .UseSqlite($"Data Source={settings.DbPath}")
            .Options;
        var dbFactory = new HearthavenDbFactory(options);
        ISessionRepository sessionRepo = new SessionRepository(dbFactory);
        IMessageRepository messageRepo = new MessageRepository(dbFactory);

        // ═══════════════════════ AI Provider ═══════════════════════

        // 从 Models 列表中获取默认模型的配置（ApiKey/Endpoint 只在 model 级别保存）
        var defaultModelConfig = settings.Models.FirstOrDefault(m => m.Name == settings.DefaultModel);
        var endpoint = defaultModelConfig?.Endpoint ?? settings.Endpoint;
        var apiKey = defaultModelConfig?.ApiKey ?? settings.ApiKey;

        // 生命周期：OpenAIProvider 接手后自己释放 HttpClient，CompositionRoot 只释放 Provider
        _provider?.Dispose();

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };
        var openAiConfig = new OpenAIConfig
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            DefaultModel = settings.DefaultModel
        };
        var provider = new OpenAIProvider(openAiConfig, httpClient);
        _provider = provider;

        // ═══════════════════════ 应用服务 ═══════════════════════

        var cacheManager = new SessionCache();
        var sessionManager = new SessionService(sessionRepo, messageRepo, cacheManager);

        // ═══════════════════════ 工作目录解析 ═══════════════════════

        var userDataDir = Path.GetDirectoryName(settings.DbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        IWorkingDirectoryResolver dirResolver = new WorkingDirectoryResolver(userDataDir);

        // ═══════════════════════ 工具系统 ═══════════════════════

        var toolRegistry = new ToolRegistry();
        ChatViewModel.RegisterBuiltInTools(toolRegistry, dirResolver);
        ToolRegistry = toolRegistry;
        var toolDispatcher = new ToolDispatcher(toolRegistry);
        var maxContextTokens = defaultModelConfig?.MaxContextTokens ?? 65536;
        var contextManager = new ContextManager(
            messageRepo, maxContextTokens, settings.MaxResponseTokens);
        var defaultMaxTokens = defaultModelConfig?.MaxTokens ?? 8192;
        var messagePersistence = new MessagePersistenceService(messageRepo, sessionRepo, contextManager);
        var agentService = new AgentService(
            provider, toolDispatcher, contextManager, messagePersistence, settings, defaultMaxTokens);
        var messageLoader = new MessageLoader(messageRepo, toolRegistry);
        var streamUiUpdater = new StreamUpdater(toolRegistry);
        var flowOrchestrator = new ChatFlowOrchestrator(
            agentService, messageRepo, sessionRepo, toolRegistry,
            cacheManager, sessionManager, messageLoader, streamUiUpdater, settings);

        // ═══════════════════════ ViewModel ═══════════════════════

        var chatVm = new ChatViewModel(
            sessionRepo, messageRepo, toolRegistry, contextManager, agentService,
            cacheManager, streamUiUpdater, sessionManager, messageLoader,
            flowOrchestrator, settings, dirResolver);
        var sidebarVm = new SessionListViewModel(sessionRepo);

        return (chatVm, sidebarVm);
    }
}
