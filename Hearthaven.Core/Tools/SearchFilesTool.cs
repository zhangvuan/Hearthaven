using Hearthaven.Core.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hearthaven.Core.Utilities;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件搜索工具 — 在文件中搜索文本内容（类似 grep）。
/// </summary>
public class SearchFilesTool : ToolBase, ITool
{
    public string Name => "search_files";
    public string Description => "在文件中搜索文本内容（类似 grep）。支持纯文本和正则搜索，支持 glob 通配符过滤。自动跳过 bin/.git/obj 等依赖目录。支持 context_lines 显示上下文行。";

    public SearchFilesTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var pattern = Utilities.JsonHelper.ExtractString(argsJson, "pattern");
        return pattern != null ? $"搜索文件 [{pattern}]" : "搜索文件";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var match = System.Text.RegularExpressions.Regex.Match(result, @"找到 (\d+) 处匹配");
        return new ToolResultViewData
        {
            SummaryTag = match.Success ? $"找到 {match.Groups[1].Value} 处" : null
        };
    }

    /// <summary>默认最大返回结果数</summary>
    private const int DefaultMaxResults = 50;

    /// <summary>单次读取文件最大字节数（避免搜索超大文件）</summary>
    private const int MaxSearchFileSize = 10 * 1024 * 1024; // 10MB

    /// <summary>正则缓存，避免同一 pattern 重复编译，最大 100 条（超过上限时整体替换）</summary>
    private static System.Collections.Concurrent.ConcurrentDictionary<string, Regex>
        RegexCache = new(System.StringComparer.OrdinalIgnoreCase);

    private const int RegexCacheMaxSize = 100;

    /// <summary>排除目录名称列表（大小写不敏感），跳过系统/生成/依赖目录</summary>
    private static readonly HashSet<string> ExcludedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".svn", ".idea",
        "bin", "obj", "node_modules", "packages", "vendor",
        "Debug", "Release", "x64", "x86", "ARM64"
    };

    /// <summary>最多连续跳过多少个不可访问的目录（防止日志刷屏），传给 FileHelper.SafeEnumerateFiles</summary>
    private const int MaxSkippedDirsLogged = 20;

    /// <summary>正则元字符集合 — 用于检测 pattern 是否需要走正则引擎</summary>
    private static readonly HashSet<char> RegexMetaChars = new(
        ['.', '^', '$', '*', '+', '?', '{', '}', '[', ']', '\\', '|', '(', ')']);

    /// <summary>
    /// 判断 pattern 是否为纯文本（不含正则元字符），
    /// 是则可用 string.Contains 快路径代替 Regex。
    /// </summary>
    private static bool IsPlainText(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        // 不含任何正则元字符 → 纯文本
        return !pattern.Any(c => RegexMetaChars.Contains(c));
    }

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "搜索关键词（支持正则表达式，也支持纯文本），如 \"class\\s+\\w+\" 或 \"TODO\""
            },
            path = new
            {
                type = "string",
                description = "搜索目录路径（相对于程序运行目录，或绝对路径），默认当前目录"
            },
            glob = new
            {
                type = "string",
                description = "文件通配符过滤，如 *.cs、*.md、*.json（可选，默认搜索所有文件）"
            },
            context_lines = new
            {
                type = "integer",
                description = "匹配行前后显示的上下文行数，默认 0（只显示匹配行）。传 2 则每处匹配前后各显示 2 行上下文"
            },
            max_results = new
            {
                type = "integer",
                description = $"最大返回结果条数，默认 {DefaultMaxResults}"
            }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<SearchFilesArgs>(argsJson);
            if (string.IsNullOrWhiteSpace(args?.Pattern))
                return ToolOutput.Error("错误：缺少 pattern 参数");

            var searchPattern = args.Pattern;
            var dirPath = string.IsNullOrWhiteSpace(args.Path)
                ? DirResolver.Resolve(null)
                : ResolvePath(args.Path.Trim());

            var fullDir = Path.GetFullPath(dirPath);
            if (!Directory.Exists(fullDir))
                return ToolOutput.Error($"错误：目录不存在 '{fullDir}'");

            var maxResults = args.MaxResults > 0 ? args.MaxResults : DefaultMaxResults;
            var contextLines = args.ContextLines > 0 ? args.ContextLines : 0;
            var glob = args.Glob?.Trim();
            var fileSearchPattern = string.IsNullOrWhiteSpace(glob) ? "*" : glob;

            // 判断 pattern 是否为纯文本（快路径）
            var isPlainText = IsPlainText(searchPattern);

            // 非纯文本时才编译正则（纯文本走 Contains 快路径，跳过正则编译）
            Regex? regex = null;
            if (!isPlainText)
            {
                // 超过上限时整体替换（ConcurrentDictionary.Keys 不保证顺序，无法正确 LRU）
                if (RegexCache.Count >= RegexCacheMaxSize)
                {
                    RegexCache = new(System.StringComparer.OrdinalIgnoreCase);
                }
                regex = RegexCache.GetOrAdd(searchPattern, pat =>
                    new Regex(pat, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)));
            }

            // ⭐ I/O 密集型操作 offload 到线程池，不阻塞 UI
            return await Task.Run(() =>
            {
                var results = new List<string>();
                var totalMatches = 0;
                var totalScanned = 0;

                foreach (var file in FileHelper.SafeEnumerateFiles(fullDir, fileSearchPattern, MaxSkippedDirsLogged))
                {
                    if (results.Count >= maxResults) break;

                    var fileDir = Path.GetDirectoryName(file);
                    if (fileDir != null)
                    {
                        var segments = fileDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (segments.Any(s => ExcludedDirNames.Contains(s)))
                            continue;
                    }

                    // 跳过超大文件
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > MaxSearchFileSize) continue;
                    }
                    catch { continue; }

                    totalScanned++;
                    var relativePath = Path.GetRelativePath(fullDir, file);

                    try
                    {
                        // 有上下文行需求 → 全量读入后按窗口输出
                        if (contextLines > 0)
                        {
                            var allLines = File.ReadAllLines(file, System.Text.Encoding.UTF8);

                            // 找出所有匹配行索引
                            var matchIndices = new List<int>();
                            for (int i = 0; i < allLines.Length; i++)
                            {
                                bool matched = isPlainText
                                    ? allLines[i].Contains(searchPattern, StringComparison.OrdinalIgnoreCase)
                                    : regex!.IsMatch(allLines[i]);
                                if (matched)
                                    matchIndices.Add(i);
                            }

                            if (matchIndices.Count == 0) continue;
                            totalMatches += matchIndices.Count;

                            // 构建上下文窗口（合并重叠）
                            var windows = new List<(int start, int end)>();
                            foreach (var idx in matchIndices)
                            {
                                var s = Math.Max(0, idx - contextLines);
                                var e = Math.Min(allLines.Length - 1, idx + contextLines);
                                if (windows.Count > 0 && s <= windows[^1].end + 1)
                                    windows[^1] = (windows[^1].start, e);
                                else
                                    windows.Add((s, e));
                            }

                            // 输出窗口
                            foreach (var (ws, we) in windows)
                            {
                                for (int i = ws; i <= we; i++)
                                {
                                    if (results.Count >= maxResults) break;
                                    var marker = matchIndices.Contains(i) ? ":" : "-";
                                    results.Add($"{relativePath}:{i + 1}{marker}{allLines[i]}");
                                }
                                if (results.Count >= maxResults) break;
                                if (windows.Count > 1) results.Add("---");
                            }
                            // 去掉末尾多余的 ---
                            if (results.Count > 0 && results[^1] == "---")
                                results.RemoveAt(results.Count - 1);
                        }
                        else
                        {
                            // 无上下文行 → 原有逐行匹配模式
                            foreach (var line in File.ReadLines(file, System.Text.Encoding.UTF8))
                            {
                                if (results.Count >= maxResults) break;
                                bool matched = isPlainText
                                    ? line.Contains(searchPattern, StringComparison.OrdinalIgnoreCase)
                                    : regex!.IsMatch(line);

                                if (matched)
                                {
                                    totalMatches++;
                                    results.Add($"{relativePath}:{totalMatches}:{line.Trim()}");
                                }
                            }
                        }
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        continue;
                    }
                    catch
                    {
                        continue;
                    }
                }

                var sb = new System.Text.StringBuilder();
                if (results.Count == 0)
                {
                    sb.AppendLine($"在 '{fullDir}' 下未找到包含 '{searchPattern}' 的内容");
                    sb.AppendLine($"搜索范围：{fileSearchPattern}，已扫描 {totalScanned} 个文件");
                }
                else
                {
                    sb.AppendLine($"在 '{fullDir}' 下搜索 \"{searchPattern}\"，找到 {totalMatches} 处匹配（显示前 {results.Count} 条）：");
                    sb.AppendLine();
                    foreach (var result in results)
                    {
                        sb.AppendLine(result);
                    }

                    // 匹配结果被截断 → 提示还有更多
                    if (totalMatches > results.Count)
                    {
                        sb.AppendLine($"\n... 还有 {totalMatches - results.Count} 处匹配未显示（共 {totalMatches} 处，扫描 {totalScanned} 个文件）");
                    }
                    else if (totalScanned > maxResults)
                    {
                        sb.AppendLine($"\n... 共扫描 {totalScanned} 个文件");
                    }
                }

                return ToolOutput.Success(sb.ToString().TrimEnd());
            }, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 pattern 参数");
        }
        catch (OperationCanceledException)
        {
            return ToolOutput.Warning("警告：文件搜索已被取消");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：搜索文件时发生异常 — {ex.Message}");
        }
    }



    private class SearchFilesArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("glob")]
        public string? Glob { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("context_lines")]
        public int ContextLines { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
    }


}
