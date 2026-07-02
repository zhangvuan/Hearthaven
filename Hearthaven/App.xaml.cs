using System.Windows;
using Hearthaven.Core.Settings;
using Hearthaven.Data.Database;
using Hearthaven.Diagnostics;
using Hearthaven.Services;
using Microsoft.EntityFrameworkCore;

namespace Hearthaven;

public partial class App : Application
{
    /// <summary>
    /// 应用级配置实例 — 启动时加载，供各组件访问。
    /// </summary>
    public static HearthavenSettings? CurrentSettings { get; private set; }

    /// <summary>用户数据目录（%APPDATA%\Hearthaven\），供子组件计算路径时使用</summary>
    public static string? ProfileDirectory { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 确保用户数据目录存在（首次运行自动创建默认文件）
        var profileDir = UserProfileManager.EnsureDirectory();

        // 2. 从用户目录加载配置
        var settings = HearthavenSettings.LoadFromDirectory(profileDir);

        CurrentSettings = settings;
        ProfileDirectory = profileDir;

        // 3. 调试日志：写入用户目录下的 debug_log.txt
        var debugLogPath = System.IO.Path.Combine(profileDir, "debug_log.txt");
        DebugLog.Initialize(debugLogPath, settings.EnableDebugLog);

        // 4. 应用启动时自动执行数据库 Migration
        var options = new DbContextOptionsBuilder<HearthavenDbContext>()
            .UseSqlite($"Data Source={settings.DbPath}")
            .Options;

        using var db = new HearthavenDbContext(options);
        try
        {
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"数据库初始化失败：{ex.Message}\n\n尝试重建数据库可能解决此问题。",
                "数据库错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // 5. 应用保存的配色主题（兼容旧版 "light"/"dark" 值）
        var themeName = ThemeService.NormalizeThemeName(settings.UI?.Theme ?? "");
        ThemeService.ApplyTheme(themeName);

        // 6. 创建主窗口
        var mainWindow = new MainWindow(settings);
        mainWindow.Show();
    }
}
