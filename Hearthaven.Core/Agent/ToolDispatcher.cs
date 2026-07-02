using System.Text.Json;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Settings;
using Hearthaven.Core.Tools;

namespace Hearthaven.Core.Agent;

/// <summary>
/// 工具执行结果
/// </summary>
/// <param name="ToolCallId">工具调用 ID</param>
/// <param name="ToolName">工具名称</param>
/// <param name="Content">执行结果内容</param>
/// <param name="IsError">是否执行出错（红色 ❗）</param>
/// <param name="IsWarning">是否执行警告（黄色 ⚠️）</param>
public sealed record ToolResult(string ToolCallId, string ToolName, string Content, bool IsError, bool IsWarning);

/// <summary>
/// 工具调度器 — 负责工具查找、执行、批量调度。
/// 将从 AI 的 ToolCallData 转换为实际的工具执行。
/// </summary>
public class ToolDispatcher
{
    private readonly ToolRegistry _registry;
    private List<ToolDefinition>? _cachedDefinitions;

    public ToolDispatcher(ToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// 执行单个工具调用。
    /// 结果状态（Success/Warning/Error）由工具内部通过 ToolOutput.Kind 自行定义。
    /// </summary>
    /// <param name="call">工具调用数据</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="progressCallback">进度回调（可选），工具执行期间报告当前状态</param>
    public async Task<ToolResult> ExecuteAsync(ToolCallData call, CancellationToken ct = default, Action<string>? progressCallback = null)
    {
        var tool = _registry.FindByName(call.FunctionName);
        if (tool == null)
        {
            return new ToolResult(call.Id, call.FunctionName,
                $"错误：未找到工具 '{call.FunctionName}'", true, false);
        }

        // 设置进度回调（仅 ToolBase 子类支持）
        if (progressCallback != null && tool is ToolBase toolBase)
            toolBase.ProgressCallback = progressCallback;

        try
        {
            var output = await tool.ExecuteAsync(call.Arguments, ct).ConfigureAwait(false);
            return new ToolResult(call.Id, call.FunctionName, output.Content,
                output.Kind == ToolResultKind.Error,
                output.Kind == ToolResultKind.Warning);
        }
        catch (Exception ex)
        {
            return new ToolResult(call.Id, call.FunctionName,
                $"错误：工具 '{call.FunctionName}' 执行异常 — {ex.Message}", true, false);
        }
        finally
        {
            // 清理进度回调，避免泄漏到下一次调用
            if (tool is ToolBase tb)
                tb.ProgressCallback = null;
        }
    }

    /// <summary>
    /// 批量执行工具调用（互不依赖的工具并行执行，用预分配数组保持结果顺序）。
    /// 所有工具当前无共享可变状态，因此可以安全并行。
    /// </summary>
    /// <param name="calls">工具调用列表</param>
    /// <param name="perToolTokens">每个工具对应的独立取消令牌（可选），长度应与 calls 一致。
    /// 【所有权约定】调用方负责创建和 Dispose 这些 CancellationTokenSource。
    /// ToolDispatcher 仅从中读取 Token，不会 Dispose 传入的 CTS 实例。</param>
    /// <param name="globalCt">全局取消令牌（如用户点击 ⏹ 停止按钮），与 perToolTokens 同时生效</param>
    /// <param name="progressCallback">进度回调（可选），透传给每个工具用于报告执行状态</param>
    public async Task<List<ToolResult>> ExecuteBatchAsync(List<ToolCallData> calls, IList<CancellationToken>? perToolTokens = null, CancellationToken globalCt = default, Action<string>? progressCallback = null)
    {
        // 预分配数组，保证结果顺序与输入一致
        var results = new ToolResult[calls.Count];
        var tasks = calls.Select(async (call, i) =>
        {
            var perCt = perToolTokens != null && i < perToolTokens.Count
                ? perToolTokens[i]
                : CancellationToken.None;

            // 合并全局 token 和 per-tool token，让 ⏹ 按钮也能中断正在执行的工具
            if (globalCt != CancellationToken.None && perCt != CancellationToken.None)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(globalCt, perCt);
                results[i] = await ExecuteAsync(call, linkedCts.Token, progressCallback).ConfigureAwait(false);
            }
            else if (globalCt != CancellationToken.None)
            {
                results[i] = await ExecuteAsync(call, globalCt, progressCallback).ConfigureAwait(false);
            }
            else
            {
                results[i] = await ExecuteAsync(call, perCt, progressCallback).ConfigureAwait(false);
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    /// 从注册表构建发给 API 的 ToolDefinition 列表。
    /// 结果在首次构建后缓存（工具列表在运行时不会变化），
    /// 后续直接返回缓存，避免每次请求重复序列化开销。
    /// </summary>
    /// <param name="toolSettings">工具开关配置（可选）。传入后按配置过滤禁用工具。</param>
    public List<ToolDefinition> BuildDefinitions(ToolSettings? toolSettings = null)
    {
        var allDefinitions = GetAllDefinitions();

        // 没有开关配置 → 返回全部
        if (toolSettings == null)
            return allDefinitions;

        // 按开关配置过滤
        var enabledWildcard = toolSettings.Enabled.Contains("*");
        return allDefinitions.Where(def =>
        {
            if (enabledWildcard)
                return !toolSettings.Disabled.Contains(def.Name, StringComparer.OrdinalIgnoreCase);
            else
                return toolSettings.Enabled.Contains(def.Name, StringComparer.OrdinalIgnoreCase);
        }).ToList();
    }

    /// <summary>获取全量 ToolDefinition 列表（内部缓存）</summary>
    private List<ToolDefinition> GetAllDefinitions()
    {
        if (_cachedDefinitions != null)
            return _cachedDefinitions;

        _cachedDefinitions = _registry.GetAll().Select(tool =>
        {
            var schemaJson = JsonSerializer.Serialize(tool.GetParametersSchema());
            var schemaElement = JsonSerializer.Deserialize<JsonElement>(schemaJson);

            return new ToolDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = schemaElement
            };
        }).ToList();

        return _cachedDefinitions;
    }
}
