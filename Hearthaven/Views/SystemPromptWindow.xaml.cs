using System.Windows;

namespace Hearthaven.Views;

/// <summary>
/// 系统提示词查看弹窗 — 只读展示当前系统提示词全文，支持选择复制。
/// </summary>
public partial class SystemPromptWindow : Window
{
    /// <summary>系统提示词文本</summary>
    public string PromptText
    {
        get => (string)GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public static readonly DependencyProperty PromptTextProperty =
        DependencyProperty.Register(nameof(PromptText), typeof(string), typeof(SystemPromptWindow),
            new PropertyMetadata(string.Empty));

    public SystemPromptWindow()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
