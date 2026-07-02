using System.Windows;

namespace Hearthaven.Views;

/// <summary>
/// 设置窗口 — 非模态，可边对话边修改配置。
/// 通过 DataContext 绑定 SettingsViewModel。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
