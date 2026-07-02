using System.IO;
using Hearthaven.Core.Services;

namespace Hearthaven.Services;

/// <summary>
/// 工作目录解析器 — UI 层实现。
/// 
/// 采用推模式（Push-Based Cache）：不依赖数据库也不使用异步方法。
/// 外部（ChatViewModel）在切换会话或切换工作目录时，通过 <see cref="SetWorkingDirectory"/>
/// 将工作目录值推送进来。后续 <see cref="Resolve"/> 和 <see cref="GetDisplayDirectory"/>
/// 均直接从缓存读取，纯内存操作，零阻塞。
/// </summary>
public class WorkingDirectoryResolver : IWorkingDirectoryResolver
{
    private readonly string _defaultBaseDir;
    private string? _cachedWorkingDir;

    public WorkingDirectoryResolver(string defaultBaseDir)
    {
        _defaultBaseDir = defaultBaseDir;
    }

    public void SetWorkingDirectory(string? workingDir)
    {
        _cachedWorkingDir = workingDir;
    }

    public string Resolve(string? path)
    {
        // 绝对路径 → 直接返回
        if (!string.IsNullOrEmpty(path) && Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        // 相对路径或 null → 基于缓存的工作目录拼接
        var baseDir = _cachedWorkingDir ?? _defaultBaseDir;
        if (string.IsNullOrEmpty(path))
            return baseDir;

        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    public string GetDisplayDirectory()
    {
        if (_cachedWorkingDir == null)
            return "Hearthaven"; // 默认用户数据目录的友好名称

        var dirName = Path.GetFileName(_cachedWorkingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return dirName.Length > 30 ? dirName[..27] + "…" : dirName;
    }
}
