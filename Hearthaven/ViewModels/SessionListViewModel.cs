using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hearthaven.Core.Data;
using Hearthaven.Diagnostics;

namespace Hearthaven.ViewModels;

/// <summary>
/// 会话列表 ViewModel — 管理侧边栏的会话加载、新建、删除、切换、重命名。
/// </summary>
public partial class SessionListViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepo;

    /// <summary>会话列表</summary>
    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    /// <summary>当前选中的会话 ID</summary>
    public string? CurrentSessionId { get; private set; }

    /// <summary>会话切换时触发，参数为新的会话 ID</summary>
    public event Action<string>? SessionSelected;

    /// <summary>请求新建会话时触发</summary>
    public event Action? NewSessionRequested;

    /// <summary>会话被删除时触发，参数为被删除的会话 ID</summary>
    public event Action<string>? SessionDeleted;

    public SessionListViewModel(ISessionRepository sessionRepo)
    {
        _sessionRepo = sessionRepo;
    }

    /// <summary>从数据库加载所有会话（一步查询，带预览内容）</summary>
    public async Task LoadSessionsAsync()
    {
        var sessions = await _sessionRepo.GetAllWithPreviewAsync();

        Sessions.Clear();

        foreach (var s in sessions)
        {
            var preview = s.Preview;
            if (preview?.Length > 60)
                preview = preview[..60] + "...";

            Sessions.Add(new SessionItemViewModel
            {
                Id = s.Id,
                Title = s.Title,
                Preview = preview,
                UpdatedAt = s.UpdatedAt
            });
        }
    }

    /// <summary>选中并切换会话</summary>
    [RelayCommand]
    private void SelectSession(SessionItemViewModel? item)
    {
        DebugLog.WriteLine(
            $"[C002] SessionListViewModel.SelectSession(item.Id={(item?.Id ?? "null")}, " +
            $"CurrentSessionId={CurrentSessionId})");
        if (item == null || item.Id == CurrentSessionId) return;

        // ★ 先更新 CurrentSessionId，再操作 IsSelected，防双向绑定递归
        CurrentSessionId = item.Id;

        foreach (var s in Sessions)
            s.IsSelected = false;

        item.IsSelected = true;
        DebugLog.WriteLine(
            $"[C002] SessionListViewModel.SelectSession: PROCEED -> SessionSelected?.Invoke({item.Id})");
        SessionSelected?.Invoke(item.Id);
    }

    /// <summary>请求新建会话（由 ChatViewModel 实际执行）</summary>
    [RelayCommand]
    private void NewSession()
    {
        NewSessionRequested?.Invoke();
    }

    /// <summary>删除会话</summary>
    [RelayCommand]
    private async Task DeleteSession(SessionItemViewModel? item)
    {
        if (item == null) return;

        var deletedId = item.Id;
        await _sessionRepo.DeleteAsync(deletedId);
        Sessions.Remove(item);

        // 通知 ChatViewModel 清理该会话的缓存
        SessionDeleted?.Invoke(deletedId);

        // 如果删除的是当前会话，自动切换到最近会话
        // ★ 不提前置 null CurrentSessionId — 让事件处理方在切换完成后更新
        if (item.Id == CurrentSessionId)
        {
            var next = Sessions.FirstOrDefault();
            if (next != null)
            {
                SessionSelected?.Invoke(next.Id);
            }
            else
            {
                NewSessionRequested?.Invoke();
            }
        }
    }

    /// <summary>在列表顶部插入一个新会话并选中</summary>
    public void InsertNewSession(string id, string title)
    {
        DebugLog.WriteLine(
            $"[C002] SessionListViewModel.InsertNewSession(id={id}, title={title}, Sessions.Count={Sessions.Count})");
        var item = new SessionItemViewModel
        {
            Id = id,
            Title = title,
            UpdatedAt = DateTime.UtcNow
        };

        // ★ 先更新 CurrentSessionId，再操作 IsSelected，防双向绑定递归
        CurrentSessionId = id;

        foreach (var s in Sessions)
            s.IsSelected = false;

        item.IsSelected = true;
        Sessions.Insert(0, item);
    }

    /// <summary>更新指定会话的标题</summary>
    public void UpdateSessionTitle(string id, string title)
    {
        var item = Sessions.FirstOrDefault(s => s.Id == id);
        if (item != null)
            item.Title = title;
    }

    /// <summary>更新指定会话的预览</summary>
    public void UpdateSessionPreview(string id, string? preview)
    {
        var item = Sessions.FirstOrDefault(s => s.Id == id);
        if (item != null)
        {
            item.Preview = preview?.Length > 60 ? preview[..60] + "..." : preview;
        }
    }

    /// <summary>切换到指定 ID 的会话并选中</summary>
    public void SelectSessionById(string id)
    {
        DebugLog.WriteLine(
            $"[C002] SessionListViewModel.SelectSessionById(id={id}, CurrentSessionId={CurrentSessionId}, Sessions.Count={Sessions.Count})");
        var existing = Sessions.FirstOrDefault(s => s.Id == id);
        if (existing == null)
        {
            InsertNewSession(id, "新对话");
            return;
        }

        // ★ 先更新 CurrentSessionId，再操作 IsSelected，防双向绑定递归
        CurrentSessionId = id;

        foreach (var s in Sessions)
        {
            var oldVal = s.IsSelected;
            var newVal = s.Id == id;
            s.IsSelected = newVal;
            if (oldVal != newVal)
            {
                DebugLog.WriteLine(
                    $"[C002] SelectSessionById.foreach: item.Id={s.Id}, IsSelected {oldVal}→{newVal}, " +
                    $"CurrentSessionId={CurrentSessionId}, targetId={id}");
            }
        }
    }

    /// <summary>取消所有会话的选中状态（新会话模式）</summary>
    public void DeselectAll()
    {
        foreach (var s in Sessions)
            s.IsSelected = false;
        CurrentSessionId = null;
    }

    /// <summary>移除侧边栏中 ID 为空的占位项</summary>
    public void RemoveEmptySession()
    {
        var empty = Sessions.FirstOrDefault(s => string.IsNullOrEmpty(s.Id));
        if (empty != null)
            Sessions.Remove(empty);
    }
}
