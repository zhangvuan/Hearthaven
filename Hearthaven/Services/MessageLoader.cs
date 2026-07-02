using Hearthaven.Core.Data;
using Hearthaven.Core.Tools;
using Hearthaven.Diagnostics;
using Hearthaven.Models;
using Hearthaven.Utilities;

namespace Hearthaven.Services;

/// <summary>
/// 消息加载器 — 负责从 DB 加载消息到 UI 列表、管理分页游标。
/// 不持有对 <see cref="ChatViewModel"/> 的任何引用，通过参数传入集合和委托。
/// </summary>
public class MessageLoader
{
    private const int InitialGroupCount = 5;
    private const int LoadMoreGroupCount = 3;

    private readonly IMessageRepository _messageRepo;
    private readonly ToolRegistry _toolRegistry;

    /// <summary>已加载消息中的最小 Id（用作按 Group 分页的游标）</summary>
    public long EarliestLoadedId { get; set; } = long.MaxValue;

    /// <summary>是否已加载全部历史</summary>
    public bool LoadedAll { get; set; }

    /// <summary>是否正在加载历史（防止重复触发）</summary>
    public bool IsLoadingHistory { get; set; }

    public MessageLoader(IMessageRepository messageRepo, ToolRegistry toolRegistry)
    {
        _messageRepo = messageRepo;
        _toolRegistry = toolRegistry;
    }

    /// <summary>给 Messages 中的所有消息设置回调（重新生成/编辑/保存）</summary>
    public void PopulateMessageCallbacks(SmartObservableCollection<MessageDisplayModel> messages,
        Func<string, Task>? regenerateAsync,
        Func<MessageDisplayModel, Task>? saveEditAsync)
    {
        foreach (var msg in messages)
        {
            if (msg.Role == "assistant" && msg.RegenerateCallback == null && regenerateAsync != null)
            {
                var groupId = msg.GroupId;
                if (!string.IsNullOrEmpty(groupId))
                {
                    var capturedGroupId = groupId;
                    msg.RegenerateCallback = async () => await regenerateAsync(capturedGroupId);
                }
            }
            if (msg.Role == "user" && msg.SaveEditCallback == null && saveEditAsync != null)
            {
                msg.SaveEditCallback = async () => await saveEditAsync(msg);
            }
        }
    }

    /// <summary>加载初始 5 轮（Group）对话</summary>
    public async Task LoadInitialMessagesAsync(
        SmartObservableCollection<MessageDisplayModel> messages,
        string sessionId,
        Action populateCallbacks)
    {
        DebugLog.WriteLine(
            $"[C002] LoadInitialMessagesAsync(sessionId={sessionId}, Messages.Count before={messages.Count})");
        var entities = await _messageRepo.GetLatestGroupsAsync(sessionId, InitialGroupCount);
        if (entities.Count == 0)
        {
            LoadedAll = true;
            return;
        }

        var built = MessageBuilder.BuildMessageViewModels(entities);
        foreach (var vm in built)
            messages.Add(vm);

        // 记录分页游标
        EarliestLoadedId = entities[0].Id;
        var loadedGroupCount = entities
            .Select(m => m.GroupId)
            .Where(id => !string.IsNullOrEmpty(id) && id != "_null_")
            .Distinct()
            .Count();
        LoadedAll = loadedGroupCount < InitialGroupCount;

        // 补充 Tool 引用
        MessageBuilder.PopulateToolReferences(messages, _toolRegistry);

        // [FIX] 给历史消息设置回调
        populateCallbacks();
    }

    /// <summary>加载更早的 3 轮（Group）历史消息（向上滚动时调用）</summary>
    public async Task LoadMoreHistoryAsync(
        SmartObservableCollection<MessageDisplayModel> messages,
        string sessionId,
        Action populateCallbacks)
    {
        if (IsLoadingHistory || LoadedAll) return;
        IsLoadingHistory = true;

        try
        {
            var beforeMaxId = EarliestLoadedId;
            var earlierGroups = await _messageRepo.GetGroupsBeforeMaxIdAsync(sessionId, beforeMaxId, LoadMoreGroupCount);

            if (earlierGroups.Count == 0)
            {
                LoadedAll = true;
                return;
            }

            var newItems = MessageBuilder.BuildMessageViewModels(earlierGroups);
            messages.InsertRange(0, newItems);

            EarliestLoadedId = earlierGroups[0].Id;
            var loadedGroupCount = earlierGroups
                .Select(m => m.GroupId)
                .Where(id => !string.IsNullOrEmpty(id) && id != "_null_")
                .Distinct()
                .Count();
            LoadedAll = loadedGroupCount < LoadMoreGroupCount;

            MessageBuilder.PopulateToolReferences(messages, _toolRegistry);
            populateCallbacks();
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    /// <summary>重置分页游标（新建/切换会话时调用）</summary>
    public void ResetCursor()
    {
        EarliestLoadedId = long.MaxValue;
        LoadedAll = false;
        IsLoadingHistory = false;
    }
}
