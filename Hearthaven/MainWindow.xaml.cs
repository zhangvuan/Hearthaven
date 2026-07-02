using Hearthaven.Core.Settings;
using Hearthaven.Diagnostics;
using Hearthaven.ViewModels;
using System.Windows;

namespace Hearthaven;

public partial class MainWindow : Window
{
    private ChatViewModel ViewModel => (ChatViewModel)DataContext;
    private readonly SessionListViewModel _sidebarVm;
    private readonly HearthavenSettings _settings;
    private bool _isSwitchingSession;

    public MainWindow(HearthavenSettings settings)
    {
        InitializeComponent();

        _settings = settings;

        // 通过 CompositionRoot 组装所有依赖，创建 ViewModel
        var (chatVm, sidebarVm) = CompositionRoot.Create(settings);
        DataContext = chatVm;
        Sidebar.DataContext = sidebarVm;
        _sidebarVm = sidebarVm;

        // 订阅侧边栏事件
        _sidebarVm.SessionSelected += OnSessionSelected;
        _sidebarVm.NewSessionRequested += OnNewSessionRequested;
        _sidebarVm.SessionDeleted += OnSessionDeleted;
        ViewModel.SessionChanged += OnCurrentSessionChanged;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();

        await _sidebarVm.LoadSessionsAsync();

        if (!string.IsNullOrEmpty(ViewModel.CurrentSessionId))
            _sidebarVm.SelectSessionById(ViewModel.CurrentSessionId);
    }

    /// <summary>侧边栏选中会话 → 切换当前对话</summary>
    private async void OnSessionSelected(string sessionId)
    {
        DebugLog.WriteLine(
            $"[C002] MainWindow.OnSessionSelected(sessionId={sessionId}, " +
            $"_isSwitchingSession={_isSwitchingSession}, ViewModel.CurrentSessionId={ViewModel.CurrentSessionId})");
        if (_isSwitchingSession) return;
        _isSwitchingSession = true;
        try
        {
            await ViewModel.SwitchSessionAsync(sessionId);
        }
        finally
        {
            _isSwitchingSession = false;
        }
    }

    /// <summary>侧边栏新建会话 → 创建新会话并切换</summary>
    private async void OnNewSessionRequested()
    {
        await ViewModel.CreateNewSessionAsync();
    }

    /// <summary>对话区切换会话 / 后台流完成 → 同步侧边栏</summary>
    private void OnCurrentSessionChanged(string sessionId)
    {
        DebugLog.WriteLine(
            $"[C002] MainWindow.OnCurrentSessionChanged(sessionId={sessionId}, " +
            $"CurrentSessionId={ViewModel.CurrentSessionId}, Messages.Count={ViewModel.Messages.Count})");

        _sidebarVm.SelectSessionById(sessionId);

        if (sessionId == ViewModel.CurrentSessionId)
        {
            _sidebarVm.UpdateSessionTitle(sessionId, ViewModel.CurrentTitle);
        }

        var preview = ViewModel.GetCachedPreview(sessionId);
        _sidebarVm.UpdateSessionPreview(sessionId, preview);
    }

    /// <summary>会话被删除 → 清理 ChatViewModel 中的缓存</summary>
    private void OnSessionDeleted(string sessionId)
    {
        ViewModel.CleanupSessionCache(sessionId);
    }

    // ═══════════════════════ 系统托盘 ═══════════════════════

    private bool _firstClose = true;

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;

        if (_firstClose)
        {
            _firstClose = false;
            TrayIcon.ShowBalloonTip("💡 炉心还在运行", "已最小化到系统托盘，右键图标可选择「退出」关闭应用", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }

        Hide();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e) => RestoreWindow();

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e) => RestoreWindow();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            DataContext = new ViewModels.SettingsViewModel(_settings, CompositionRoot.ToolRegistry)
        };
        settingsWindow.Show();
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        UnsubscribeEvents();
        TrayIcon?.Dispose();
        CompositionRoot.Shutdown();
        Application.Current.Shutdown();
    }

    private void UnsubscribeEvents()
    {
        _sidebarVm.SessionSelected -= OnSessionSelected;
        _sidebarVm.NewSessionRequested -= OnNewSessionRequested;
        _sidebarVm.SessionDeleted -= OnSessionDeleted;
        ViewModel.SessionChanged -= OnCurrentSessionChanged;
        ViewModel.UnsubscribeMessagesCollectionChanged();
    }
}
