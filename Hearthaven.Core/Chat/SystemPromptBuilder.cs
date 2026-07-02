using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;
using System.Text;

namespace Hearthaven.Core.Chat;

/// <summary>
/// 系统提示词构建器 — 生成发给 AI 的系统提示词。
/// 包括炉心应用的身份说明和已注册工具的用法描述。
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>
    /// 构建完整的系统提示词。
    /// </summary>
    /// <param name="registry">已注册的工具列表（仅 full 模式追加）</param>
    /// <param name="profile">Agent 人格配置（名称/性格），null 时使用默认值</param>
    /// <param name="workRules">工作规则文本（CLAUDE.md），仅 full 模式注入</param>
    /// <param name="mode">对话模式："chat"（纯聊天）或 "full"（完整功能），默认 "chat"</param>
    /// <param name="workingDirectory">当前工作目录完整路径（仅 full 模式注入）</param>
    public static string Build(ToolRegistry? registry = null, AgentProfile? profile = null,
        string? workRules = null, string? mode = null, string? workingDirectory = null)
    {
        var isFull = string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        // ── 通用模块：所有模式共有 ──

        // Agent 名称（可配置）
        var agentName = profile?.Name ?? "心心";
        sb.AppendLine($"你是 {agentName}，你正在使用 Hearthaven，它是一个运行在 Windows 桌面上的智能体助手。");

        // 身份与称呼（从 agent.json 读取）
        AppendIdentityAndCallAs(sb, profile);

        // 性格设定（从 SOUL.md 读取）
        if (profile != null && !string.IsNullOrEmpty(profile.Personality))
        {
            sb.AppendLine();
            sb.AppendLine("# SOUL.md");
            sb.AppendLine(profile.Personality);
        }

        // 自定义 Prompt 后缀（从 AGENT.md 读取）
        if (profile != null && !string.IsNullOrEmpty(profile.CustomPromptSuffix))
        {
            sb.AppendLine();
            sb.AppendLine("# AGENT.md");
            sb.AppendLine(profile.CustomPromptSuffix);
        }

        // ── Full 模式独有模块 ──
        if (isFull)
        {
            // 工作目录 + 工作规则（工作上下文，放一起）
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                sb.AppendLine();
                sb.AppendLine("# WORKING DIRECTORY");
                sb.AppendLine($"用户当前的工作目录为：`{workingDirectory}`");
                sb.AppendLine("文件操作工具将基于此目录解析相对路径。");
            }

            if (!string.IsNullOrEmpty(workRules))
            {
                sb.AppendLine();
                sb.AppendLine("# WORK RULES");
                sb.AppendLine(workRules);
            }

            // 动态追加已注册的工具描述
            var tools = registry?.GetAll();
            if (tools is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("# TOOLS");
                foreach (var tool in tools)
                {
                    sb.AppendLine($"- `{tool.Name}`：{tool.Description}");
                }
                sb.AppendLine();
                sb.AppendLine("主动选择合适的工具并调用，支持多轮连续调用配合完成任务。");
                sb.AppendLine("基于工具返回的结果给用户完整回答。");
            }

            // 客户端扩展渲染能力
            sb.AppendLine();
            sb.AppendLine("# RENDERING");
            sb.AppendLine("客户端支持以下扩展渲染：");
            sb.AppendLine("- **图片**：`![描述](路径)` — 支持本地文件和网络URL地址。");
            sb.AppendLine("- **音频**：`[描述](路径)` — Markdown 链接语法，链接地址指向音频文件时自动渲染为可播放控件，支持本地文件和网络URL地址。");
        }

        return sb.ToString().Trim();
    }

    private static void AppendIdentityAndCallAs(StringBuilder sb, AgentProfile? profile)
    {
        if (profile == null) return;

        var identity = profile.Identity;
        var callAs = profile.CallAs;

        string? message = (string.IsNullOrEmpty(identity), string.IsNullOrEmpty(callAs)) switch
        {
            (false, false) => $"你是用户的{identity}，你称呼用户为{callAs}。",
            (false, true) => $"你是用户的{identity}。",
            (true, false) => $"你称呼用户为{callAs}。",
            (true, true) => null
        };

        if (message != null) sb.AppendLine(message);
    }

}
