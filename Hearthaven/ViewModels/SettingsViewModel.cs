using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;
using Hearthaven.Services;
using Hearthaven.Views;

namespace Hearthaven.ViewModels;

/// <summary>
/// 设置 ViewModel — 管理 5 个 Tab 的所有配置项。
/// 从 HearthavenSettings 实例读取，保存时写回 appsettings.json 并更新运行中实例。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly HearthavenSettings _settings;
    private readonly ToolRegistry? _toolRegistry;

    public SettingsViewModel(HearthavenSettings settings, ToolRegistry? toolRegistry = null)
    {
        _settings = settings;
        _toolRegistry = toolRegistry;
        LoadFromSettings();
    }

    // ═══════════════════════════════════════════
    // Tab 1: 📡 服务商
    // ═══════════════════════════════════════════

    /// <summary>模型列表</summary>
    public ObservableCollection<ModelItem> Models { get; } = [];

    /// <summary>列表中选中的模型</summary>
    [ObservableProperty]
    private ModelItem? _selectedModel;

    /// <summary>默认模型的名称（用于下拉选择）</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteModelCommand))]
    private string? _defaultModelName;

    /// <summary>添加新模型</summary>
    [RelayCommand]
    private void AddModel()
    {
        var item = new ModelItem
        {
            Provider = "OpenAI Compatible",
            Endpoint = "https://api.deepseek.com"
        };
        var window = new ModelEditWindow(item);
        window.Owner = System.Windows.Application.Current.Windows
            .OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
        if (window.ShowDialog() == true)
        {
            // 第一个添加的模型自动设为默认
            if (Models.Count == 0)
            {
                item.IsDefault = true;
                DefaultModelName = item.ModelId;
            }
            Models.Add(item);
        }
    }

    /// <summary>编辑选中的模型</summary>
    [RelayCommand]
    private void EditModel(ModelItem? item)
    {
        if (item == null) return;
        var clone = item.Clone();
        var window = new ModelEditWindow(clone);
        window.Owner = System.Windows.Application.Current.Windows
            .OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
        if (window.ShowDialog() == true)
        {
            var idx = Models.IndexOf(item);
            clone.IsDefault = item.IsDefault;
            Models[idx] = clone;

            // 如果修改的是默认模型，同步 DefaultModelName
            if (clone.IsDefault)
                DefaultModelName = clone.ModelId;
        }
    }

    /// <summary>删除选中的模型</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteModel))]
    private void DeleteModel(ModelItem? item)
    {
        if (item == null) return;
        var wasDefault = item.IsDefault;
        Models.Remove(item);

        // 删除了默认模型 → 选第一个为默认
        if (wasDefault && Models.Count > 0)
        {
            Models[0].IsDefault = true;
            DefaultModelName = Models[0].ModelId;
        }
        else if (Models.Count == 0)
        {
            DefaultModelName = null;
        }
    }

    private bool CanDeleteModel(ModelItem? item) => item != null && Models.Count > 1;

    /// <summary>设为默认模型</summary>
    [RelayCommand]
    private void SetDefaultModel(ModelItem? item)
    {
        if (item == null || item.IsDefault) return;
        foreach (var m in Models)
            m.IsDefault = false;
        item.IsDefault = true;
        DefaultModelName = item.ModelId;
    }

    // ═══════════════════════════════════════════
    // Tab 2: 🧑 Agent
    // ═══════════════════════════════════════════

    [ObservableProperty]
    private string _agentName = "炉心";

    [ObservableProperty]
    private string _identity = "";

    [ObservableProperty]
    private string _callAs = "";

    [ObservableProperty]
    private string? _personality;

    [ObservableProperty]
    private double _temperature = 0.7;

    [ObservableProperty]
    private int _maxResponseTokens = 2000;

    [ObservableProperty]
    private int _timeoutSeconds = 300;

    /// <summary>头像预览路径</summary>
    public string? AvatarPath => UserProfileManager.GetAvatarPath();

    /// <summary>打开用户数据目录（资源管理器）</summary>
    [RelayCommand]
    private void OpenProfileDirectory()
    {
        var dir = UserProfileManager.GetDirectory();
        if (System.IO.Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    // ═══════════════════════════════════════════
    // Tab 3: 🛠️ 工具
    // ═══════════════════════════════════════════

    /// <summary>工具开关列表</summary>
    public ObservableCollection<ToolSwitchItem> Tools { get; } = [];

    [ObservableProperty]
    private double _maxToolTokenRatio = 0.85;

    // ═══════════════════════════════════════════
    // Tab 4: 🎨 界面
    // ═══════════════════════════════════════════

    /// <summary>主题选项列表</summary>
    public ThemeOption[] ThemeOptions => [
        new("极简蓝白", "Blue"),
        new("暖米柔白", "Warm"),
        new("暗夜深色", "Dark")
    ];

    [ObservableProperty]
    private string _theme = "Blue";

    /// <summary>主题选择变化时实时切换（用户立刻看到效果）</summary>
    partial void OnThemeChanged(string value)
    {
        ThemeService.ApplyTheme(value);
    }

    [ObservableProperty]
    private int _fontSize = 13;

    // ═══════════════════════════════════════════
    // Tab 5: ℹ️ 关于
    // ═══════════════════════════════════════════

    [ObservableProperty]
    private bool _enableDebugLog;

    /// <summary>应用版本</summary>
    public string AppVersion => "1.0.0";

    // ═══════════════════════════════════════════
    // 保存
    // ═══════════════════════════════════════════

    [RelayCommand]
    private void Save()
    {
        // ── Tab 1: 服务商 ──
        _settings.Models = Models.Select(m => m.ToConfig()).ToList();
        _settings.DefaultModel = Models.FirstOrDefault(m => m.IsDefault)?.ModelId
                                  ?? Models.FirstOrDefault()?.ModelId
                                  ?? _settings.DefaultModel;

        // ── Tab 2: Agent → 写入 agent.json ──
        UserProfileManager.WriteAgentJson(new AgentProfileData
        {
            Name = AgentName,
            Identity = Identity,
            CallAs = CallAs,
            Avatar = "avatar.png"
        });
        _settings.Temperature = Temperature;
        _settings.MaxResponseTokens = MaxResponseTokens;
        _settings.TimeoutSeconds = TimeoutSeconds;

        // ── Tab 3: 工具 ──
        _settings.MaxToolTokenRatio = MaxToolTokenRatio;
        var disabledTools = Tools.Where(t => !t.IsEnabled).Select(t => t.ToolName).ToList();
        _settings.Tools = new ToolSettings
        {
            Enabled = ["*"],
            Disabled = disabledTools
        };

        // ── Tab 4: 界面 ──
        _settings.UI ??= new UiSettings();
        _settings.UI.Theme = Theme;
        _settings.UI.FontSize = FontSize;

        // 实时切换主题（保存前即可让用户看到效果）
        ThemeService.ApplyTheme(Theme);

        // ── Tab 5: 关于 ──
        _settings.EnableDebugLog = EnableDebugLog;

        // 持久化到文件
        try
        {
            _settings.SaveToDirectory(UserProfileManager.GetDirectory());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 保存配置文件失败: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"保存配置文件失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        System.Windows.MessageBox.Show(
            "设置已保存，部分配置将在下次对话时生效。", "炉心",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 关闭设置窗口
        foreach (var window in System.Windows.Application.Current.Windows)
        {
            if (window is Views.SettingsWindow sw)
            {
                sw.Close();
                break;
            }
        }
    }

    // ═══════════════════════════════════════════
    // 加载
    // ═══════════════════════════════════════════

    private void LoadFromSettings()
    {
        // ── Tab 1: 服务商 ──
        var defaultModelName = _settings.DefaultModel;
        foreach (var config in _settings.Models)
        {
            var isDefault = string.Equals(config.Name, defaultModelName, StringComparison.OrdinalIgnoreCase);
            Models.Add(ModelItem.FromConfig(config, isDefault));
        }
        DefaultModelName = defaultModelName;

        // ── Tab 2: Agent → 从 agent.json 读取 ──
        var agentData = UserProfileManager.ReadAgentJson();
        AgentName = agentData.Name;
        Identity = agentData.Identity;
        CallAs = agentData.CallAs;
        Temperature = _settings.Temperature;
        MaxResponseTokens = _settings.MaxResponseTokens;
        TimeoutSeconds = _settings.TimeoutSeconds;

        // ── Tab 3: 工具 ──
        MaxToolTokenRatio = _settings.MaxToolTokenRatio;
        LoadToolsFromRegistry();

        // ── Tab 4: 界面 ──
        if (_settings.UI != null)
        {
            Theme = ThemeService.NormalizeThemeName(_settings.UI.Theme);
            FontSize = _settings.UI.FontSize;
        }

        // ── Tab 5: 关于 ──
        EnableDebugLog = _settings.EnableDebugLog;
    }

    /// <summary>从 ToolRegistry 加载工具开关列表，合并配置中的禁用状态</summary>
    private void LoadToolsFromRegistry()
    {
        Tools.Clear();

        if (_toolRegistry == null) return;

        // 收集配置中的禁用列表
        var disabledSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_settings.Tools != null)
        {
            var enabledWildcard = _settings.Tools.Enabled.Contains("*");
            if (enabledWildcard)
            {
                foreach (var d in _settings.Tools.Disabled)
                    disabledSet.Add(d);
            }
            else
            {
                // 白名单模式：未在白名单中的视为禁用
                var enabledSet = new HashSet<string>(_settings.Tools.Enabled, StringComparer.OrdinalIgnoreCase);
                foreach (var tool in _toolRegistry.GetAll())
                {
                    if (!enabledSet.Contains(tool.Name))
                        disabledSet.Add(tool.Name);
                }
            }
        }

        foreach (var tool in _toolRegistry.GetAll())
        {
            Tools.Add(new ToolSwitchItem
            {
                ToolName = tool.Name,
                DisplayName = tool.Name,
                Description = tool.Description,
                IsEnabled = !disabledSet.Contains(tool.Name)
            });
        }
    }
}

/// <summary>主题选项（标签 + 值）</summary>
public record ThemeOption(string Label, string Value);
