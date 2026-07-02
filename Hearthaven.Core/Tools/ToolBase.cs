using Hearthaven.Core.Services;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 工具基类 — 统一注入 <see cref="IWorkingDirectoryResolver"/>，
/// 提供路径解析能力。所有需要路径解析的文件工具继承此类后再实现 <see cref="ITool"/>。
/// </summary>
public abstract class ToolBase
{
    /// <summary>工作目录解析器</summary>
    protected readonly IWorkingDirectoryResolver DirResolver;

    /// <summary>
    /// 进度回调 — 工具执行期间报告当前进度状态（如 "正在备份文件 xxx.cs…"）。
    /// 由 ToolDispatcher 在调用 ExecuteAsync 前设置，执行完毕后自动清理。
    /// 工具子类在关键步骤（备份、写入等）可调用此回调通知 UI 层更新状态。
    /// </summary>
    public Action<string>? ProgressCallback { get; set; }

    protected ToolBase(IWorkingDirectoryResolver dirResolver)
    {
        DirResolver = dirResolver ?? throw new ArgumentNullException(nameof(dirResolver));
    }

    /// <summary>
    /// 解析路径：相对路径 → 基于会话工作目录拼接；绝对路径 → 直接返回。
    /// </summary>
    protected string ResolvePath(string? path)
    {
        return DirResolver.Resolve(path);
    }
}
