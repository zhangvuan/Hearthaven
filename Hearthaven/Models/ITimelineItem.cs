namespace Hearthaven.Models;

/// <summary>
/// 时间线条目接口 — TimelineItems 集合的强类型标记接口。
/// 实现该接口的类型可被添加到 MessageDisplayModel.TimelineItems 集合中，
/// 替代原有的 <see cref="object"/> 弱类型集合。
/// </summary>
public interface ITimelineItem
{
}
