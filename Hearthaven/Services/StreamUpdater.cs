using Hearthaven.Core.Agent;
using Hearthaven.Core.Chat;
using Hearthaven.Core.Tools;
using Hearthaven.Models;
using System.Collections.Concurrent;

namespace Hearthaven.Services;

/// <summary>
/// 流式 UI 更新器 — 专门构建 <see cref="AgentEvents"/> 回调闭包，
/// 管理流式输出期间的工具调用块、轮次状态和追加消息队列。
/// 不持有对 <see cref="ChatViewModel"/> 的任何引用，完全通过委托解耦。
/// </summary>
public class StreamUpdater
{
    private readonly ToolRegistry _toolRegistry;

    /// <summary>[A8] 待处理的追加消息队列（生成期间用户发送的追加消息）</summary>
    private readonly ConcurrentQueue<ChatMessage> _pendingFollowUps = new();

    public StreamUpdater(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    /// <summary>追加消息入队</summary>
    public void EnqueueFollowUp(ChatMessage msg) => _pendingFollowUps.Enqueue(msg);

    /// <summary>耗尽所有未被 AI 消费的追加消息，返回文本内容列表</summary>
    public List<string> DrainPendingFollowUps()
    {
        var results = new List<string>();
        while (_pendingFollowUps.TryDequeue(out var msg))
        {
            if (!string.IsNullOrEmpty(msg.Content))
                results.Add(msg.Content);
        }
        return results;
    }

    // ──────────────── AgentEvents 闭包构造 ────────────────

    /// <summary>
    /// 构造 AgentEvents 回调闭包，用于更新指定助手消息的 UI 状态。
    /// 调用方通过 <paramref name="onStatusChange"/> 和 <paramref name="onContextReady"/>
    /// 将状态变更传递回 ViewModel。
    /// 
    /// 文本和思考 chunk 经 15ms 定时缓冲区批量派发到 UI 线程，避免高频流式输出时 UI 过载。
    /// </summary>
    /// <param name="assistantMsg">当前正在生成的助手消息显示模型</param>
    /// <param name="onStatusChange">状态文字更新回调（如 "正在调用工具..."）</param>
    /// <param name="onContextReady">上下文 Token 统计更新回调</param>
    /// <returns>(events, finalize) — events 注入 AgentService，finalize 在流式完成后调用</returns>
    public (AgentEvents events, Action finalize) BuildAgentEvents(
        MessageDisplayModel assistantMsg,
        Action<string> onStatusChange,
        Action<int, double> onContextReady)
    {
        var toolCallBlocks = new Dictionary<int, ToolCallBlock>();
        RoundBlock? currentRound = null;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var isFinalized = false;  // finalize 后禁止旧 flush 回调继续修改状态

        // ── Chunk 缓冲区：积攒 15ms 后批量派发到 UI 线程 ──
        var textBuffer = new ConcurrentQueue<string>();
        var thinkingBuffer = new ConcurrentQueue<string>();
        var flushPending = 0; // 0=idle, 1=flush scheduled

        void ScheduleFlush()
        {
            if (Interlocked.CompareExchange(ref flushPending, 1, 0) != 0)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(15).ConfigureAwait(false);

                    // 收集缓冲区中的所有内容
                    var textSb = new System.Text.StringBuilder();
                    while (textBuffer.TryDequeue(out var c)) textSb.Append(c);

                    var thinkSb = new System.Text.StringBuilder();
                    while (thinkingBuffer.TryDequeue(out var c)) thinkSb.Append(c);

                    if (textSb.Length > 0 || thinkSb.Length > 0)
                    {
                        await dispatcher.InvokeAsync(() =>
                        {
                            // finalize 已完成，不再接受旧 flush 的更新
                            if (isFinalized) return;

                            // 思考内容先处理（确保它在文本之前展示）
                            if (thinkSb.Length > 0)
                            {
                                if (currentRound == null)
                                {
                                    currentRound = new RoundBlock();
                                    assistantMsg.TimelineItems.Add(currentRound);
                                }
                                if (currentRound.Reasoning == null)
                                    currentRound.Reasoning = new ReasoningBlock();
                                currentRound.Reasoning.Content += thinkSb.ToString();
                            }

                            // 文本内容
                            if (textSb.Length > 0)
                            {
                                if (currentRound == null)
                                {
                                    currentRound = new RoundBlock { IsStreaming = true };
                                    assistantMsg.TimelineItems.Add(currentRound);
                                }
                                else if (!currentRound.IsStreaming)
                                {
                                    currentRound.IsStreaming = true;
                                }

                                if (currentRound.Reasoning?.IsThinking == true)
                                    currentRound.Reasoning.IsThinking = false;

                                currentRound.Content += textSb.ToString();
                            }
                        });
                    }
                }
                catch
                {
                    // 异常静默捕获，防止冲刷永久卡死
                }
                finally
                {
                    // 释放冲刷标志，如果有待处理数据则立即调度下一轮
                    // 注意：不能在此处先 CAS 再调 ScheduleFlush，否则 flushPending 已被占用，
                    // ScheduleFlush 内部的 CAS 会失败 → 冲刷链永久断裂
                    Volatile.Write(ref flushPending, 0);
                    if (!textBuffer.IsEmpty || !thinkingBuffer.IsEmpty)
                        ScheduleFlush();
                }
            });
        }

        /// <summary>确保有 RoundBlock，关闭流式态，创建并注册 ToolCallBlock</summary>
        ToolCallBlock EnsureToolCallBlock(int idx, string name, string argumentsJson, CancellationTokenSource? cts)
        {
            // 确保有 RoundBlock
            if (currentRound == null)
            {
                currentRound = new RoundBlock();
                assistantMsg.TimelineItems.Add(currentRound);
            }

            // 思考完成 → 标记"已思考"
            if (currentRound.Reasoning?.IsThinking == true)
                currentRound.Reasoning.IsThinking = false;

            // 文本流已结束，关闭流式态
            currentRound.IsStreaming = false;

            var tool = _toolRegistry.FindByName(name);
            var isLongRunning = tool?.IsLongRunning ?? true;

            var block = new ToolCallBlock
            {
                ToolName = name,
                ArgumentsJson = argumentsJson,
                IsExecuting = isLongRunning,
                Tool = tool,
                Cts = cts
            };
            toolCallBlocks[idx] = block;
            currentRound.ToolCalls.Add(block);
            return block;
        }

        var events = new AgentEvents
        {
            OnTextChunk = chunk =>
            {
                textBuffer.Enqueue(chunk);
                ScheduleFlush();
            },
            OnThinkingChunk = chunk =>
            {
                thinkingBuffer.Enqueue(chunk);
                ScheduleFlush();
            },
            OnToolCallChunkStarted = (idx, name) => dispatcher.InvokeAsync(() =>
            {
                // 已由之前的 chunk 创建过工具块 → 跳过
                if (toolCallBlocks.ContainsKey(idx))
                    return;

                // 提前创建工具块（参数和 CTS 由后续 OnToolCallStart 补充）
                EnsureToolCallBlock(idx, name, "", null);
            }),
            OnToolCallStart = (idx, name, arguments, cts) => dispatcher.InvokeAsync(() =>
            {
                // 工具块可能已被 OnToolCallChunkStarted 提前创建
                if (toolCallBlocks.TryGetValue(idx, out var existingBlock))
                {
                    // 更新参数和 CTS，IsExecuting 已设置无需变更
                    existingBlock.ArgumentsJson = arguments;
                    existingBlock.Cts = cts;
                }
                else
                {
                    // 兜底：没有提前创建（如旧版 API 无流式 tool_call chunk）
                    EnsureToolCallBlock(idx, name, arguments, cts);
                }
            }),
            OnToolCallEnd = (idx, name, result, isError, isWarning) => dispatcher.InvokeAsync(() =>
            {
                if (toolCallBlocks.TryGetValue(idx, out var block))
                {
                    block.IsExecuting = false;
                    block.ApplyResult(result, isError, isWarning);
                }
            }),
            OnContextReady = (count, ratio) => dispatcher.InvokeAsync(() =>
            {
                onContextReady(count, ratio);
            }),
            OnStatusChange = msg => dispatcher.InvokeAsync(() =>
            {
                onStatusChange(msg);
            }),
            OnRoundComplete = () => dispatcher.InvokeAsync(() =>
            {
                // 本轮 Agent Loop 完成（工具调用执行完毕），重置状态
                // 下一轮将创建新的 RoundBlock，实现按轮次分组显示
                currentRound = null;
                toolCallBlocks.Clear();
            }),
            // [A8] 追加消息检查回调 — 每轮工具执行完毕后调用
            OnCheckPendingFollowUp = () =>
            {
                var followUps = new List<ChatMessage>();
                while (_pendingFollowUps.TryDequeue(out var followUp))
                {
                    followUps.Add(followUp);
                }
                return Task.FromResult(followUps);
            }
        };

        return (events, () =>
        {
            // 标记已收官，后续旧 flush 回调跳过
            isFinalized = true;

            // 切换到 UI 线程冲洗缓冲区剩余内容 + 关闭流式状态
            dispatcher.Invoke(() =>
            {
                var textSb = new System.Text.StringBuilder();
                while (textBuffer.TryDequeue(out var c)) textSb.Append(c);
                var thinkSb = new System.Text.StringBuilder();
                while (thinkingBuffer.TryDequeue(out var c)) thinkSb.Append(c);

                if (thinkSb.Length > 0)
                {
                    if (currentRound == null)
                    {
                        currentRound = new RoundBlock();
                        assistantMsg.TimelineItems.Add(currentRound);
                    }
                    if (currentRound.Reasoning == null)
                        currentRound.Reasoning = new ReasoningBlock();
                    currentRound.Reasoning.Content += thinkSb.ToString();
                }

                if (textSb.Length > 0)
                {
                    if (currentRound == null)
                    {
                        currentRound = new RoundBlock { IsStreaming = true };
                        assistantMsg.TimelineItems.Add(currentRound);
                    }
                    else if (!currentRound.IsStreaming)
                        currentRound.IsStreaming = true;

                    if (currentRound.Reasoning?.IsThinking == true)
                        currentRound.Reasoning.IsThinking = false;

                    currentRound.Content += textSb.ToString();
                }

                // 关闭最后一个轮次的流式状态 → 触发 Markdown 渲染
                if (currentRound != null)
                    currentRound.IsStreaming = false;

                assistantMsg.Content = "";
                assistantMsg.IsStreaming = false;

                // 重置状态栏文字为"就绪"，清除工具执行期间残留的进度状态
                onStatusChange("就绪 💬");
            });
        });
    }
}
