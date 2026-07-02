using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Hearthaven.Utilities;

/// <summary>
/// 智能可观察集合 — 在标准 ObservableCollection 基础上提供 InsertRange / AddRange 批量操作，
/// 只触发一次 CollectionChanged 通知，大幅减少 WPF 布局重算次数。
/// </summary>
public class SmartObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// 在指定索引处批量插入多个元素，每次插入触发精确的 Add 通知。
    /// 相比 Reset 通知，WPF CollectionView 无需重建已有容器，长列表下性能更优。
    /// </summary>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        var list = items.ToList();
        if (list.Count == 0) return;

        int insertIndex = index;
        foreach (var item in list)
        {
            Items.Insert(insertIndex, item);
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, insertIndex));
            insertIndex++;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// 批量添加多个元素到末尾，每次添加触发精确的 Add 通知。
    /// 相比 Reset 通知，WPF CollectionView 无需重建已有容器，长列表下性能更优。
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        var list = items.ToList();
        if (list.Count == 0) return;

        foreach (var item in list)
        {
            var startIndex = Items.Count;
            Items.Add(item);
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, startIndex));
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
