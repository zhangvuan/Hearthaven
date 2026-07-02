using System.Windows;

namespace Hearthaven.Services;

/// <summary>
/// 主题服务 — 运行时切换配色主题（ResourceDictionary 热替换）。
/// </summary>
public static class ThemeService
{
    /// <summary>当前主题名称（如 "Blue"、"Warm"）</summary>
    public static string CurrentTheme { get; private set; } = "Blue";

    /// <summary>主题切换后触发，方便其他组件做额外刷新</summary>
    public static event Action<string>? ThemeChanged;

    /// <summary>
    /// 规范化主题名：旧版 "light" 统一映射为 "Blue"。
    /// 注意："Dark" 和 "Warm" 现在是正式主题名，不在此映射。
    /// </summary>
    public static string NormalizeThemeName(string themeName)
    {
        return (themeName ?? "").ToLowerInvariant() switch
        {
            "light" or "" => "Blue",
            _ => themeName ?? "Blue"
        };
    }

    /// <summary>
    /// 应用指定名称的配色主题。
    /// 从 Styles/Themes/Theme.{themeName}.xaml 加载并替换运行时字典。
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        // 第一步：规范化主题名（兼容旧版 "light"）
        var normalized = NormalizeThemeName(themeName);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        // 包 URI（兼容运行时路径）
        var uri = new Uri($"pack://application:,,,/Styles/Themes/Theme.{normalized}.xaml",
                          UriKind.Absolute);

        try
        {
            var newDict = new ResourceDictionary { Source = uri };

            // 查找已有的主题字典（Source 含 "/Theme." 的字典）
            var existing = Application.Current?.Resources.MergedDictionaries
                .FirstOrDefault(d =>
                    d.Source?.OriginalString?.Contains("/Theme.") == true);

            if (existing != null)
            {
                var idx = Application.Current!.Resources.MergedDictionaries.IndexOf(existing);
                Application.Current.Resources.MergedDictionaries.RemoveAt(idx);
                Application.Current.Resources.MergedDictionaries.Insert(idx, newDict);
            }
            else
            {
                // 没有旧主题字典 → 添加到末尾
                Application.Current?.Resources.MergedDictionaries.Add(newDict);
            }
        }
        catch (Exception ex)
        {
            // 主题文件不存在或加载失败 → 不崩溃，回退到当前主题
            System.Diagnostics.Debug.WriteLine(
                $"[Theme] 加载主题 '{normalized}' 失败: {ex.Message}");
            return;
        }

        CurrentTheme = normalized;

        // 通知监听者
        ThemeChanged?.Invoke(normalized);
    }
}
