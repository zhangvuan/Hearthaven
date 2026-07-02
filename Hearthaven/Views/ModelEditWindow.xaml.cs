using System.Windows;
using Hearthaven.ViewModels;

namespace Hearthaven.Views;

/// <summary>
/// 模型编辑弹窗 — 添加或编辑一个模型配置。
/// DataContext 为 <see cref="ModelItem"/> 实例。
/// </summary>
public partial class ModelEditWindow : Window
{
    /// <summary>
    /// 构造模型编辑窗口。
    /// </summary>
    /// <param name="item">要编辑的模型条目（新建时传入空 ModelItem）</param>
    public ModelEditWindow(ModelItem item)
    {
        InitializeComponent();
        DataContext = item;

        // 将 API Key 同步到 PasswordBox
        if (!string.IsNullOrEmpty(item.ApiKey))
            ApiKeyBox.Password = item.ApiKey;
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ModelItem item)
            item.ApiKey = ApiKeyBox.Password;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
