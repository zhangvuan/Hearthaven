namespace Hearthaven.Core.Utilities;

/// <summary>
/// 通用格式化工具类 — 集中存放各种格式化/转换方法，避免散落在各 ViewModel 中。
/// </summary>
public static class FormatHelper
{
    /// <summary>
    /// 将 Token 数格式化为以 K/M 为单位的数值字符串（四舍五入取整）。
    /// 例如：0 → "0"，1500 → "2K"，65536 → "66K"，1048576 → "1M"
    /// </summary>
    public static string FormatTokenSize(int tokens) => tokens switch
    {
        < 1000 => tokens.ToString("N0"),
        < 1_000_000 => $"{(tokens + 500) / 1000}K",
        _ => $"{(tokens + 500_000) / 1_000_000}M"
    };

    /// <summary>
    /// 将字节数换算为可读大小字符串。
    /// 例如：0 → "0 B"，1500 → "1.5 KB"，1048576 → "1.0 MB"
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
