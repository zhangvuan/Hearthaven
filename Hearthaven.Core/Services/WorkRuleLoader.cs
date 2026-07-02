namespace Hearthaven.Core.Services;

/// <summary>
/// 工作规则加载器 — 扫描用户数据目录和会话工作目录下的 CLAUDE.md，
/// 合并后返回规则文本，供 SystemPromptBuilder 注入到 system prompt。
/// </summary>
public class WorkRuleLoader
{
    private readonly IWorkingDirectoryResolver _dirResolver;
    private readonly string _userDataDir;

    // CLAUDE.md 文件名（支持大小写变体）
    private static readonly string[] FileNames = ["CLAUDE.md", "claude.md", "Claude.md"];

    public WorkRuleLoader(IWorkingDirectoryResolver dirResolver, string userDataDir)
    {
        _dirResolver = dirResolver;
        _userDataDir = userDataDir;
    }

    /// <summary>
    /// 加载工作规则文本。
    /// 扫描用户数据目录 + 会话工作目录下的 CLAUDE.md，有则读取并拼接。
    /// 两个位置都有时，用户数据目录的规则在上，工作目录的规则在下。
    /// 都没有时返回 null。
    /// </summary>
    public string? LoadWorkRules()
    {
        var parts = new List<string>();

        // 1. 用户数据目录的 CLAUDE.md（全局规则）
        var globalRule = ReadFirstFound(_userDataDir);
        if (globalRule != null)
            parts.Add(globalRule);

        // 2. 会话工作目录的 CLAUDE.md（项目规则）
        var workingDir = _dirResolver.Resolve(null); // 获取工作目录完整路径
        if (!string.IsNullOrEmpty(workingDir) &&
            !string.Equals(workingDir, _userDataDir, StringComparison.OrdinalIgnoreCase))
        {
            var projectRule = ReadFirstFound(workingDir);
            if (projectRule != null)
                parts.Add(projectRule);
        }

        if (parts.Count == 0) return null;
        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>在指定目录下查找第一个存在的 CLAUDE.md 文件并读取</summary>
    private static string? ReadFirstFound(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        foreach (var name in FileNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return null;
    }
}
