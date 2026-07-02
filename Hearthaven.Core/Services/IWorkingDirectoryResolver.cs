namespace Hearthaven.Core.Services;

/// <summary>
/// 工作目录解析接口。
/// 采用推模式（Cache-Based）：由外部（ChatViewModel）在切换会话/工作目录时
/// 调用 <see cref="SetWorkingDirectory"/> 推送缓存值，实现类只做纯内存路径拼接。
/// </summary>
public interface IWorkingDirectoryResolver
{
    /// <summary>
    /// 设置当前会话的工作目录（由 ChatViewModel 在切换会话或切换工作目录时调用）。
    /// 实现类缓存此值，后续 <see cref="Resolve"/> 和 <see cref="GetDisplayDirectory"/> 直接使用。
    /// </summary>
    void SetWorkingDirectory(string? workingDir);

    /// <summary>
    /// 解析路径。
    /// path 为 null/相对路径 → 基于缓存的工作目录拼接
    /// path 为绝对路径 → 直接返回
    /// </summary>
    string Resolve(string? path);

    /// <summary>
    /// 获取当前会话工作目录的显示名称（目录名，过长省略）
    /// </summary>
    string GetDisplayDirectory();
}
