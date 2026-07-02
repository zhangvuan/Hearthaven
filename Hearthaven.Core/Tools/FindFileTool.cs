using Hearthaven.Core.Services;
using System.Text;
using System.Text.Json;
using Hearthaven.Core.Utilities;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件查找工具 — 按文件名/通配符搜索文件（类似 find 命令）。
/// 递归搜索，自动跳过 bin、obj、.git 等无关目录。
/// </summary>
public class FindFileTool : ToolBase, ITool
{
    public string Name => "find_file";
    public string Description => "按文件名/通配符递归搜索文件（类似 find）。支持 * 和 ? 通配符。自动跳过 bin/.git/obj 等依赖目录。";

    public FindFileTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var pattern = Utilities.JsonHelper.ExtractString(argsJson, "pattern");
        return pattern != null ? $"查找文件 [{pattern}]" : "查找文件";
    }

    /// <summary>默认最大返回条数</summary>
    private const int DefaultMaxResults = 50;

    /// <summary>排除目录名称列表，跳过系统/生成/依赖目录</summary>
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".svn", ".idea",
        "bin", "obj", "node_modules", "packages", "vendor",
        "Debug", "Release", "x64", "x86", "ARM64"
    };

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "文件名通配符，如 *.cs、*ViewModel*.cs、appsettings.json"
            },
            path = new
            {
                type = "string",
                description = "搜索起始目录（相对于程序运行目录，或绝对路径），默认程序运行目录"
            },
            max_results = new
            {
                type = "integer",
                description = $"最大返回条数，默认 {DefaultMaxResults}"
            }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<FindFileArgs>(argsJson);
            if (string.IsNullOrWhiteSpace(args?.Pattern))
                return ToolOutput.Error("错误：缺少 pattern 参数");

            // 参数解析（同步，极快）
            var dirPath = string.IsNullOrWhiteSpace(args.Path)
                ? DirResolver.Resolve(null)
                : ResolvePath(args.Path.Trim());

            var fullDir = Path.GetFullPath(dirPath);
            if (!Directory.Exists(fullDir))
                return ToolOutput.Error($"错误：目录不存在 '{fullDir}'");

            var maxResults = args.MaxResults > 0 ? args.MaxResults : DefaultMaxResults;

            // 提取纯文件名模式（去掉路径前缀如 src/**/）
            var rawPattern = args.Pattern.Trim();
            var searchPattern = rawPattern;
            var separatorIndex = rawPattern.LastIndexOfAny(new[] { '/', '\\' });
            if (separatorIndex >= 0)
                searchPattern = rawPattern[(separatorIndex + 1)..];

            if (string.IsNullOrWhiteSpace(searchPattern))
                searchPattern = "*";

            // ⭐ I/O 密集型操作 offload 到线程池，不阻塞 UI
            return await Task.Run(() =>
            {
                var results = new List<string>();
                var totalScanned = 0;
                var truncated = false;

                foreach (var file in FileHelper.SafeEnumerateFiles(fullDir))
                {
                    if (results.Count >= maxResults) { truncated = true; break; }

                    var fileDir = Path.GetDirectoryName(file);
                    if (fileDir != null)
                    {
                        var segments = fileDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (segments.Any(s => ExcludedDirNames.Contains(s)))
                            continue;
                    }

                    totalScanned++;
                    var fileName = Path.GetFileName(file);

                    if (!MatchPattern(fileName, searchPattern))
                        continue;

                    var relativePath = Path.GetRelativePath(fullDir, file);
                    results.Add(relativePath);
                }

                var sb = new StringBuilder();
                if (results.Count == 0)
                {
                    sb.AppendLine($"在 '{fullDir}' 下未找到匹配 '{rawPattern}' 的文件");
                    sb.AppendLine($"已搜索 {totalScanned} 个文件");
                }
                else
                {
                    sb.AppendLine($"在 '{fullDir}' 下找到 {results.Count} 个匹配 '{rawPattern}' 的文件：");
                    sb.AppendLine();
                    foreach (var result in results)
                    {
                        sb.AppendLine($"  {result}");
                    }

                    if (truncated)
                    {
                        sb.AppendLine($"\n... 还有更多匹配文件未显示（共扫描 {totalScanned} 个文件）");
                    }
                    else if (totalScanned > results.Count)
                    {
                        sb.AppendLine($"\n... 共扫描 {totalScanned} 个文件");
                    }
                }

                return ToolOutput.Success(sb.ToString().TrimEnd());
            }, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 pattern 字符串参数");
        }
        catch (OperationCanceledException)
        {
            return ToolOutput.Warning($"警告：文件查找已被取消");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：查找文件时发生异常 — {ex.Message}");
        }
    }

    /// <summary>
    /// 通配符模式匹配 — 使用 .NET 内置的简单表达式匹配。
    /// 支持 *（任意长度字符）和 ?（单个字符）。
    /// </summary>
    private static bool MatchPattern(string fileName, string pattern)
    {
        // 空模式匹配所有
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;

        try
        {
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName);
        }
        catch
        {
            // 回退：大小写不敏感的包含匹配
            return fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }





    private class FindFileArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
    }
}
