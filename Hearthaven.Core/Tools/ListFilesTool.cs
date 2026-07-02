using Hearthaven.Core.Services;
using System.Text.Json;
using Hearthaven.Core.Utilities;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 文件列表工具 — 列出指定目录下的文件和子目录。
/// </summary>
public class ListFilesTool : ToolBase, ITool
{
    public string Name => "list_files";
    public string Description => "列出指定目录下的文件和子目录（不递归）。path 可选，默认程序运行目录。";
    public bool IsLongRunning => false;

    public ListFilesTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var path = Utilities.JsonHelper.ExtractString(argsJson, "path");
        return path != null ? $"文件列表 [{path}]" : "文件列表";
    }

    public ToolResultViewData FormatResult(string result)
    {
        var match = System.Text.RegularExpressions.Regex.Match(result, @"共 (\d+) 项");
        return new ToolResultViewData
        {
            SummaryTag = match.Success ? $"共 {match.Groups[1].Value} 项" : null
        };
    }

    /// <summary>默认最大返回条数</summary>
    private const int DefaultMaxResults = 100;

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
            max_results = new
            {
                type = "integer",
                description = $"最大返回条数，默认 {DefaultMaxResults}"
            }
        },
        required = Array.Empty<string>()
    };

    public Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ListFilesArgs>(argsJson);
            var dirPath = string.IsNullOrWhiteSpace(args?.Path)
                ? DirResolver.Resolve(null)
                : ResolvePath(args.Path.Trim());

            var fullPath = Path.GetFullPath(dirPath);

            if (!Directory.Exists(fullPath))
                return Task.FromResult(ToolOutput.Error($"错误：目录不存在 '{fullPath}'"));

            var maxResults = args?.MaxResults > 0 ? args.MaxResults : DefaultMaxResults;

            var directories = Directory.GetDirectories(fullPath)
                .Select(d => new FileEntry
                {
                    Name = Path.GetFileName(d),
                    Type = "📁 目录"
                });

            var files = Directory.GetFiles(fullPath)
                .Select(f => new FileEntry
                {
                    Name = Path.GetFileName(f),
                    Type = "📄 文件",
                    Size = FormatHelper.FormatSize(new FileInfo(f).Length)
                });

            var allEntries = directories.Concat(files).ToList();
            var totalCount = allEntries.Count;
            var entries = allEntries.Take(maxResults).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"目录 '{fullPath}' 下的内容：");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                var line = string.IsNullOrEmpty(entry.Size)
                    ? $"  {entry.Type} {entry.Name}"
                    : $"  {entry.Type} {entry.Name}  ({entry.Size})";
                sb.AppendLine(line);
            }

            if (totalCount > maxResults)
                sb.AppendLine($"\n... 还有 {totalCount - maxResults} 项未显示（共 {totalCount} 项）");
            else
                sb.AppendLine($"\n共 {totalCount} 项");

            return Task.FromResult(ToolOutput.Success(sb.ToString().TrimEnd()));
        }
        catch (JsonException)
        {
            return Task.FromResult(ToolOutput.Error("错误：参数解析失败，需要提供 path 字符串参数"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutput.Error($"错误：列出目录时发生异常 — {ex.Message}"));
        }
    }



    private class ListFilesArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string? Path { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
    }

    /// <summary>文件/目录条目 DTO</summary>
    private class FileEntry
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Size { get; set; }
    }
}
