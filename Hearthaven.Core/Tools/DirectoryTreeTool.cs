using Hearthaven.Core.Services;
using System.Text;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 目录树工具 — 递归显示目录的树形结构。
/// 支持深度控制、智能折叠（>50 子项自动隐藏）、跳过依赖目录。
/// </summary>
public class DirectoryTreeTool : ToolBase, ITool
{
    public string Name => "directory_tree";
    public string Description => "递归显示目录的树形结构。支持深度控制（默认 3，最大 10），子项过多时自动折叠。默认跳过依赖目录，可通过 include_deps 显示。";
    public bool IsLongRunning => true;

    public DirectoryTreeTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        return path != null ? $"目录树 [{path}]" : "目录树";
    }

    public ToolResultViewData FormatResult(string result)
    {
        // 从首行提取统计信息: "目录树：xxx (X 个目录，Y 个文件)"
        var match = System.Text.RegularExpressions.Regex.Match(result,
            @"\((\d+) 个目录，(\d+) 个文件\)");
        if (match.Success)
            return new ToolResultViewData
            {
                SummaryTag = $"{match.Groups[1].Value} 目录 / {match.Groups[2].Value} 文件"
            };
        return new ToolResultViewData();
    }

    /// <summary>默认最大深度</summary>
    private const int DefaultMaxDepth = 3;

    /// <summary>智能折叠阈值——子项超过此数时自动折叠</summary>
    private const int FoldThreshold = 50;

    /// <summary>输出预算上限（256KB）</summary>
    private const long MaxOutputBytes = 256 * 1024;

    /// <summary>排除目录（默认跳过）</summary>
    private static readonly HashSet<string> DefaultExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".svn", ".hg", ".idea",
        "bin", "obj", "node_modules", "packages", "vendor",
        "Debug", "Release", "x64", "x86", "ARM64",
        ".mypy_cache", ".pytest_cache", ".cache",
        ".next", ".nuxt", ".turbo", ".vercel",
        "dist", "build", "out", "target",
        "__pycache__", ".venv", "venv", ".rpt"
    };

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "目录路径（相对于程序运行目录，或绝对路径），默认当前目录"
            },
            max_depth = new
            {
                type = "integer",
                description = $"最大递归深度（默认 {DefaultMaxDepth}，0 表示只显示当前层，最大 10）"
            },
            include_deps = new
            {
                type = "boolean",
                description = "是否显示 bin、obj、.git 等依赖/生成目录（默认 false）"
            }
        },
        required = Array.Empty<string>()
    };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<DirectoryTreeArgs>(argsJson);

            var dirPath = string.IsNullOrWhiteSpace(args?.Path)
                ? DirResolver.Resolve(null)
                : ResolvePath(args.Path.Trim());

            var fullPath = Path.GetFullPath(dirPath);
            if (!Directory.Exists(fullPath))
                return Task.FromResult(ToolOutput.Error($"错误：目录不存在 '{fullPath}'"));

            var maxDepth = args?.MaxDepth >= 0 ? Math.Min(args.MaxDepth, 10) : DefaultMaxDepth;
            var includeDeps = args?.IncludeDeps ?? false;

            // 纯内存操作，在调用线程执行
            var sb = new StringBuilder();
            int dirCount = 0, fileCount = 0;

            sb.AppendLine($"目录树：{fullPath}");

            try
            {
                BuildTree(fullPath, string.Empty, maxDepth, includeDeps, sb, ref dirCount, ref fileCount, ct);
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine();
                sb.AppendLine("[已取消]");
            }

            // 修正第一行（追加统计）
            var summary = $" ({dirCount} 个目录，{fileCount} 个文件)";
            sb.Insert(sb.ToString().IndexOf('\n') + 1, summary);

            return Task.FromResult(ToolOutput.Success(sb.ToString().TrimEnd()));
        }
        catch (JsonException)
        {
            return Task.FromResult(ToolOutput.Error("错误：参数解析失败"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：生成目录树时发生异常 — {ex.Message}"));
        }
    }

    private static void BuildTree(string dir, string prefix, int maxDepth, bool includeDeps,
        StringBuilder sb, ref int dirCount, ref int fileCount, CancellationToken ct)
    {
        if (sb.Length >= MaxOutputBytes) return;
        ct.ThrowIfCancellationRequested();

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dir);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (DirectoryNotFoundException) { return; }

        var items = entries
            .Select(e => new { Path = e, IsDir = Directory.Exists(e) })
            .OrderByDescending(e => e.IsDir)  // 目录在前
            .ThenBy(e => Path.GetFileName(e.Path))
            .ToList();

        // 非 include_deps 模式 → 过滤掉排除目录
        if (!includeDeps)
        {
            items = items.Where(e =>
            {
                if (!e.IsDir) return true;
                var name = Path.GetFileName(e.Path);
                return !DefaultExcludedDirs.Contains(name);
            }).ToList();
        }

        var totalItems = items.Count;

        // 智能折叠：超过阈值时只展开第一个，其余折叠
        bool folded = totalItems > FoldThreshold;
        var displayItems = folded ? items.Take(1).ToList() : items;

        for (int i = 0; i < displayItems.Count; i++)
        {
            var item = displayItems[i];
            var name = Path.GetFileName(item.Path);
            var isLast = i == displayItems.Count - 1 && !folded;
            var connector = isLast ? "└── " : "├── ";
            var newPrefix = isLast ? "    " : "│   ";

            sb.AppendLine($"{prefix}{connector}{name}{(item.IsDir ? "/" : "")}");

            if (sb.Length >= MaxOutputBytes) return;

            if (item.IsDir && maxDepth > 0)
            {
                dirCount++;
                BuildTree(item.Path, prefix + newPrefix, maxDepth - 1, includeDeps,
                    sb, ref dirCount, ref fileCount, ct);
            }
            else if (!item.IsDir)
            {
                fileCount++;
            }
        }

        // 折叠提示
        if (folded)
        {
            var hidden = totalItems - 1;
            sb.AppendLine($"{prefix}└── [{hidden} 项已隐藏 — 使用 list_directory 或缩小路径查看]");
        }
    }



    private class DirectoryTreeArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_depth")]
        public int MaxDepth { get; set; } = DefaultMaxDepth;

        [System.Text.Json.Serialization.JsonPropertyName("include_deps")]
        public bool IncludeDeps { get; set; }
    }
}
