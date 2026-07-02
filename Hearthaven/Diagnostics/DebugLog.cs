using System.IO;

namespace Hearthaven.Diagnostics;

/// <summary>
/// 调试日志工具 — 由 appsettings.json 中的 EnableDebugLog 控制开关。
/// 启用后诊断日志自动写入程序目录下的 debug_log.txt，方便排查问题。
/// 默认关闭，不影响性能。
/// </summary>
public static class DebugLog
{
    private static string? _filePath;
    private static bool _isEnabled;

    /// <summary>日志是否已启用</summary>
    public static bool IsEnabled => _isEnabled;

    /// <summary>
    /// 初始化调试日志（由 App.OnStartup 调用）。
    /// 当 enabled 为 true 时，日志将写入指定文件路径。
    /// 当 enabled 为 false 时，所有 WriteLine 调用直接返回，无性能开销。
    /// </summary>
    public static void Initialize(string filePath, bool enabled)
    {
        _isEnabled = enabled;
        if (!enabled) return;

        _filePath = filePath;
        WriteLine("--- [DebugLog] 日志开始 ---");
    }

    /// <summary>
    /// 写入一行日志。仅当 IsEnabled 为 true 时才会写入文件。
    /// </summary>
    public static void WriteLine(string message)
    {
        if (!_isEnabled || _filePath == null) return;

        try
        {
            File.AppendAllText(_filePath, message + "\n");
        }
        catch
        {
            // 文件写入失败不影响主流程
        }
    }

    /// <summary>在日志中写入分隔标记</summary>
    public static void Separator()
    {
        WriteLine("--- [DebugLog] ---");
    }
}
