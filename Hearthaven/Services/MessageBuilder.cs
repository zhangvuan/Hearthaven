using System.Text.Json;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Data;
using Hearthaven.Core.Tools;
using Hearthaven.Models;

namespace Hearthaven.Services;

/// <summary>
/// 消息构建器 — 纯静态工具类，负责数据实体（MessageEntity）与显示模型（MessageDisplayModel）之间的双向转换。
/// 无状态、不依赖 WPF 类型，可独立单元测试。
/// </summary>
public static class MessageBuilder
{
    /// <summary>
    /// 统一入口：将一组消息实体转换为显示模型列表。
    /// 自动按 GroupId 分组并处理工具调用轮次 / 普通轮次。
    /// </summary>
    public static List<MessageDisplayModel> BuildMessageViewModels(List<MessageEntity> messages)
    {
        var result = new List<MessageDisplayModel>();
        var groups = GroupMessages(messages);

        // 跟踪最近添加的助手显示模型（用于插入追加消息到其 TimelineItems）
        MessageDisplayModel? lastAssistantVm = null;

        foreach (var (key, groupMsgs, isToolGroup) in groups)
        {
            // 分离组内的追加消息和正常消息
            var followUpMsgs = groupMsgs.Where(m => m.IsFollowUp).ToList();
            var normalMsgs = groupMsgs.Where(m => !m.IsFollowUp).ToList();

            // 旧数据兼容：如果组内只有一条 user 消息且没有任何其他角色消息，
            // 可能是旧版格式保存的追加消息（独立 GroupId，无 IsFollowUp 标记）
            if (!isToolGroup && normalMsgs.Count == 1
                && normalMsgs[0].Role == "user" && lastAssistantVm != null)
            {
                followUpMsgs.Add(normalMsgs[0]);
                normalMsgs.Clear();
            }

            if (isToolGroup)
            {
                // 合并追加消息到正常消息列表（按 Id 排序），BuildAssistantFromMessages 内部会按顺序插入 FollowUpBlock
                var merged = normalMsgs.Concat(followUpMsgs).OrderBy(m => m.Id).ToList();
                followUpMsgs.Clear();

                var userMsg = merged.FirstOrDefault(m => m.Role == "user" && !m.IsFollowUp);
                if (userMsg != null)
                    result.Add(BuildUserMessage(userMsg));
                var assistantVm = BuildAssistantFromMessages(merged);
                result.Add(assistantVm);
                lastAssistantVm = assistantVm;
            }
            else
            {
                foreach (var msg in normalMsgs)
                {
                    if (msg.Role == "user")
                        result.Add(BuildUserMessage(msg));
                    else if (msg.Role == "assistant")
                    {
                        var vm = BuildPlainAssistant(msg);
                        result.Add(vm);
                        lastAssistantVm = vm;
                    }
                }
            }

            // 追加消息插入到最后一个助手消息的 TimelineItems 中
            // 注意：对于 isToolGroup，追加消息已合并到 normalMsgs 由 BuildAssistantFromMessages 内部按 Id 排序处理
            // 此处仅处理非工具组（简单对话）的旧格式追加消息：统一插入到末尾
            if (followUpMsgs.Count > 0 && lastAssistantVm != null)
            {
                int insertIndex = lastAssistantVm.TimelineItems.Count;

                foreach (var fup in followUpMsgs)
                {
                    lastAssistantVm.TimelineItems.Insert(insertIndex++, new FollowUpBlock
                    {
                        Content = fup.Content
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 从一组消息实体中重建 assistant 气泡的完整时间线（思考块 + 工具调用块 + 内容）。
    /// 处理数据库中的原始消息记录，还原出与实时对话一致的 TimelineItems。
    /// </summary>
    public static MessageDisplayModel BuildAssistantFromMessages(List<MessageEntity> groupMsgs)
    {
        var sorted = groupMsgs.OrderBy(m => m.Id).ToList();
        var vm = new MessageDisplayModel("assistant", "")
        {
            GroupId = sorted[0].GroupId,
            Timestamp = sorted[0].Timestamp
        };
        var toolCallBlocks = new Dictionary<string, ToolCallBlock>();

        foreach (var msg in sorted)
        {
            if (msg.Role == "user")
            {
                // 追加消息 → 在当前位置插入 FollowUpBlock（按时间顺序排列）
                if (msg.IsFollowUp)
                {
                    vm.TimelineItems.Add(new FollowUpBlock { Content = msg.Content });
                }
                continue;
            }

            // 思考内容 → 创建新轮次
            if (!string.IsNullOrEmpty(msg.ReasoningContent) && msg.Role == "assistant")
            {
                var round = new RoundBlock
                {
                    Reasoning = new ReasoningBlock { Content = msg.ReasoningContent, IsThinking = false }
                };
                vm.TimelineItems.Add(round);
            }

            if (msg.Role == "assistant" && !string.IsNullOrEmpty(msg.ToolCallsJson))
            {
                // 有工具调用的助手消息 → 创建轮次（或复用刚创建的思考轮次）
                RoundBlock? round = vm.TimelineItems.LastOrDefault() as RoundBlock;
                if (round == null)
                {
                    round = new RoundBlock();
                    vm.TimelineItems.Add(round);
                }

                // 文本内容（工具调用前的 AI 回复）
                if (!string.IsNullOrEmpty(msg.Content))
                    round.Content = msg.Content.Trim();

                // 解析工具调用（提取 id + function.name + function.arguments）
                try
                {
                    var toolCalls = JsonSerializer.Deserialize<List<JsonElement>>(
                        msg.ToolCallsJson, SseJsonContext.Options);
                    if (toolCalls != null)
                    {
                        foreach (var tc in toolCalls)
                        {
                            var callId = tc.TryGetProperty("id", out var idProp)
                                ? idProp.GetString() : null;
                            if (string.IsNullOrEmpty(callId)) continue;

                            if (tc.TryGetProperty("function", out var func) &&
                                func.TryGetProperty("name", out var nameProp))
                            {
                                var argsJson = "";
                                if (func.TryGetProperty("arguments", out var argsProp))
                                    argsJson = argsProp.GetString() ?? "";

                                var block = new ToolCallBlock
                                {
                                    ToolCallId = callId,
                                    ToolName = nameProp.GetString() ?? "",
                                    ArgumentsJson = argsJson,
                                    IsExecuting = false
                                };
                                round.ToolCalls.Add(block);
                                toolCallBlocks[callId] = block;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MessageBuilder] 工具调用 JSON 解析失败: {ex.Message}");
                }
            }
            else if (msg.Role == "tool")
            {
                // 使用 ToolCallId 精确匹配对应的 ToolCallBlock
                if (!string.IsNullOrEmpty(msg.ToolCallId) &&
                    toolCallBlocks.TryGetValue(msg.ToolCallId, out var block))
                {
                    block.ApplyResult(msg.Content, isError: false);
                }
            }
            else if (msg.Role == "assistant" && string.IsNullOrEmpty(msg.ToolCallsJson))
            {
                // 最终回复（无工具调用）→ 创建新轮次
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    vm.TimelineItems.Add(new RoundBlock
                    {
                        Content = msg.Content.Trim()
                    });
                }
            }
        }

        vm.Content = "";
        return vm;
    }

    /// <summary>
    /// 从消息实体创建一个简单的用户气泡。
    /// </summary>
    public static MessageDisplayModel BuildUserMessage(MessageEntity msg)
    {
        return new MessageDisplayModel("user", msg.Content)
        {
            Timestamp = msg.Timestamp,
            GroupId = msg.GroupId,
            MessageId = msg.Id
        };
    }

    /// <summary>
    /// 从消息实体创建一个简单的助手气泡（无工具调用，可能有思考内容）。
    /// 始终将内容包装在 RoundBlock 中，确保 UI 正确渲染。
    /// </summary>
    public static MessageDisplayModel BuildPlainAssistant(MessageEntity msg)
    {
        var vm = new MessageDisplayModel("assistant", "")
        {
            Timestamp = msg.Timestamp,
            GroupId = msg.GroupId
        };

        var round = new RoundBlock
        {
            Content = msg.Content?.Trim() ?? ""
        };
        if (!string.IsNullOrEmpty(msg.ReasoningContent))
        {
            round.Reasoning = new ReasoningBlock { Content = msg.ReasoningContent, IsThinking = false };
        }
        vm.TimelineItems.Add(round);

        return vm;
    }

    /// <summary>
    /// 按 GroupId 对消息列表分组，判断每组是否为工具调用轮次。
    /// </summary>
    public static List<(string Key, List<MessageEntity> Messages, bool IsToolGroup)>
        GroupMessages(List<MessageEntity> messages)
    {
        return messages
            .GroupBy(m => m.GroupId ?? "_null_")
            .Select(g =>
            {
                // 按 Id 排序（自增主键），编辑不会改变 Id，确保顺序始终正确
                var groupMsgs = g.OrderBy(m => m.Id).ToList();
                var isToolGroup = groupMsgs.Any(m =>
                    m.Role == "tool" || !string.IsNullOrEmpty(m.ToolCallsJson));
                return (g.Key, groupMsgs, isToolGroup);
            })
            .ToList();
    }

    /// <summary>
    /// 遍历所有助手消息的 ToolCallBlock，补充 Tool 引用。
    /// 历史消息从 DB 加载时 ToolCallBlock 没有 ITool 实例，
    /// 导致 DisplayTitle 回退到 ToolName，无法显示友好描述。
    /// </summary>
    public static void PopulateToolReferences(IEnumerable<MessageDisplayModel> messages, ToolRegistry registry)
    {
        foreach (var msg in messages)
        {
            if (msg.Role != "assistant") continue;

            foreach (var item in msg.TimelineItems)
            {
                if (item is RoundBlock round)
                {
                    foreach (var tc in round.ToolCalls)
                    {
                        tc.Tool ??= registry.FindByName(tc.ToolName);
                    }
                }
            }
        }
    }
}
