using System.Windows;
using System.Windows.Controls;
using Hearthaven.Diagnostics;
using Hearthaven.ViewModels;

namespace Hearthaven.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    public SessionListViewModel? ViewModel => DataContext as SessionListViewModel;

    private void OnSettingsClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var parentWindow = System.Windows.Window.GetWindow(this);
        if (parentWindow == null) return;

        var settings = App.CurrentSettings
            ?? Hearthaven.Core.Settings.HearthavenSettings.LoadFromDirectory(
                App.ProfileDirectory ?? Hearthaven.Core.Settings.UserProfileManager.GetDirectory());

        var settingsWindow = new Hearthaven.Views.SettingsWindow
        {
            Owner = parentWindow,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            DataContext = new ViewModels.SettingsViewModel(settings, Hearthaven.CompositionRoot.ToolRegistry)
        };
        settingsWindow.Show();
    }

    /// <summary>
    /// 选中变化时触发 — IsSelected 通过 TwoWay 绑定自动同步到 ViewModel，
    /// 此处只需驱动会话切换事件即可。
    /// </summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SessionItemViewModel item)
        {
            DebugLog.WriteLine(
                $"[C002] SidebarView.OnSelectionChanged: item.Id={item.Id}, Title={item.Title}, IsSelected={item.IsSelected}");

            // 防递归：SelectSession 中已先更新 CurrentSessionId，双向绑定触发的
            // SelectionChanged 会被这个 guard 拦住
            if (item.Id == ViewModel?.CurrentSessionId) return;

            ViewModel?.SelectSessionCommand.Execute(item);
        }
    }
}
