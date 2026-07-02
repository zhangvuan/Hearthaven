namespace Hearthaven.Models;

/// <summary>
/// [A8] 追加消息块 — 表示用户在 AI 生成期间发送的追加消息。
/// 插入到当前 AI 消息的 TimelineItems 中，显示在工具执行/文本回复之间。
/// </summary>
public class FollowUpBlock : ITimelineItem
{
    /// <summary>追加消息的文本内容</summary>
    public string Content { get; init; } = "";
}
