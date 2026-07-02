using Hearthaven.Core.Services;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 命令执行工具 — 执行命令行命令（如 dotnet build、git status 等）。
/// 支持配置工作目录和超时时间。
/// </summary>
public class ExecCommandTool : ToolBase, ITool
{
    public string Name => "exec_command";
    public string Description => "执行命令行命令。通过 cwd 参数指定工作目录，不支持 cd 命令切换目录。支持超时设置。";

    public ExecCommandTool(IWorkingDirectoryResolver dirResolver) : base(dirResolver) { }

    public string GetDisplayTitle(string argsJson)
    {
        var cmd = Utilities.JsonHelper.ExtractString(argsJson, "command");
        if (cmd == null) return "执行命令";
        return cmd.Length <= 40 ? $"执行 [{cmd}]" : $"执行 [{cmd[..37]}...]";
    }

    /// <summary>默认超时时间（秒）</summary>
    private const int DefaultTimeoutSeconds = 30;

    /// <summary>最大输出字节数（100KB）</summary>
    private const int MaxOutputBytes = 100 * 1024;

    /// <summary>最大输出行数</summary>
    private const int MaxOutputLines = 2000;

    public object GetParametersSchema() => new
    {
        type = "object",
        properties = new
        {
            command = new
            {
                type = "string",
                description = "要执行的命令，如 \"dotnet build\"、\"git status\"、\"dir\""
            },
            cwd = new
            {
                type = "string",
                description = "工作目录（绝对路径，或相对于程序运行目录），默认当前程序目录"
            },
            timeout = new
            {
                type = "integer",
                description = $"超时时间（秒），默认 {DefaultTimeoutSeconds}，最大 300"
            }
        },
        required = new[] { "command" }
    };

    public async Task<ToolOutput> ExecuteAsync(string argsJson, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ExecCommandArgs>(argsJson);
            if (string.IsNullOrWhiteSpace(args?.Command))
                return ToolOutput.Error("错误：缺少 command 参数");

            var cwd = string.IsNullOrWhiteSpace(args.Cwd)
                ? DirResolver.Resolve(null)
                : ResolvePath(args.Cwd.Trim());

            if (!Directory.Exists(cwd))
                return ToolOutput.Error($"错误：工作目录不存在 '{cwd}'");

            var timeoutSeconds = args.Timeout > 0 ? Math.Min(args.Timeout, 300) : DefaultTimeoutSeconds;

            return await RunProcessAsync(args.Command.Trim(), cwd, timeoutSeconds, ct);
        }
        catch (JsonException)
        {
            return ToolOutput.Error("错误：参数解析失败，需要提供 command 字符串参数");
        }
        catch (Exception ex)
        {
            return ToolOutput.Error($"错误：执行命令时发生异常 — {ex.Message}");
        }
    }

    private static async Task<ToolOutput> RunProcessAsync(string command, string cwd, int timeoutSeconds, CancellationToken ct = default)
    {
        // 判断操作系统，选择 shell 包装
        var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 使用 Arguments 而非 ArgumentList：
        // ArgumentList 会自动对参数中的引号做二次转义，导致带引号的路径解析失败。
        // 对 cmd.exe /c 而言，整个 command 需要原样传递给 shell，Arguments 更合适。
        psi.Arguments = $"{(OperatingSystem.IsWindows() ? "/c" : "-c")} {command}";

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var outputLineCount = 0;
        var errorLineCount = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (outputBuilder)
            {
                if (outputBuilder.Length < MaxOutputBytes && outputLineCount < MaxOutputLines)
                {
                    outputBuilder.AppendLine(e.Data);
                    outputLineCount++;
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (errorBuilder)
            {
                if (errorBuilder.Length < MaxOutputBytes && errorLineCount < MaxOutputLines)
                {
                    errorBuilder.AppendLine(e.Data);
                    errorLineCount++;
                }
            }
        };

        var startTime = DateTime.UtcNow;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 链接外部取消令牌（用户点击"停止生成"）和内部超时令牌
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 kill 异常 */ }

            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            var partialOutput = outputBuilder.Length > 0
                ? $"\n\n已获取的部分输出：\n{outputBuilder}"
                : "";

            if (ct.IsCancellationRequested)
                return ToolOutput.Error($"⚠️ 工具执行已被用户取消{partialOutput}");

            return ToolOutput.Error($"错误：命令执行超时（{timeoutSeconds} 秒）{partialOutput}");
        }

        // 等待异步事件处理完成（进程已退出，此调用接近瞬时完成）
        process.WaitForExit();

        var stdout = outputBuilder.ToString().TrimEnd();
        var stderr = errorBuilder.ToString().TrimEnd();

        var sb = new StringBuilder();

        // 输出截断标记
        var stdoutTruncated = outputLineCount >= MaxOutputLines || outputBuilder.Length >= MaxOutputBytes;
        var stderrTruncated = errorLineCount >= MaxOutputLines || errorBuilder.Length >= MaxOutputBytes;

        if (!string.IsNullOrEmpty(stdout))
        {
            sb.AppendLine("【标准输出】");
            sb.AppendLine(stdout);
            if (stdoutTruncated)
                sb.AppendLine("[输出已截断：已达到最大限制]");
        }

        if (!string.IsNullOrEmpty(stderr))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("【错误输出】");
            sb.AppendLine(stderr);
            if (stderrTruncated)
                sb.AppendLine("[输出已截断：已达到最大限制]");
        }

        var exitCode = process.ExitCode;

        sb.AppendLine();
        sb.Append(exitCode == 0
            ? $"✅ 命令执行成功（退出码：{exitCode}）"
            : $"❌ 命令执行失败（退出码：{exitCode}）");

        if (string.IsNullOrEmpty(stdout) && string.IsNullOrEmpty(stderr))
        {
            sb.AppendLine();
            sb.Append("（命令无输出）");
        }

        var content = sb.ToString().TrimEnd();

        // 根据退出码判断：零 → Success，非零 → Error
        return exitCode == 0
            ? ToolOutput.Success(content)
            : ToolOutput.Error(content);
    }



    private class ExecCommandArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("command")]
        public string? Command { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("timeout")]
        public int Timeout { get; set; }
    }
}
