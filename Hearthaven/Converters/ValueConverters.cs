using System.Globalization;
using System.Windows;

namespace Hearthaven.Converters;

/// <summary>用户消息靠右，助手消息靠左</summary>
public class RoleToAlignConverter : BaseValueConverter<RoleToAlignConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}

/// <summary>角色背景色：用户浅蓝，助手浅灰</summary>
public class RoleToBgConverter : BaseValueConverter<RoleToBgConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == "user"
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0xF5, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xF9, 0xFA));
    }
}

/// <summary>角色标签：user → "你"，assistant → "助手"</summary>
public class RoleToLabelConverter : BaseValueConverter<RoleToLabelConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "user" => "🧑 你",
            "assistant" => "🤖 助手",
            _ => value?.ToString() ?? ""
        };
    }
}

/// <summary>bool → Visibility</summary>
public class BoolToVisibilityConverter : BaseValueConverter<BoolToVisibilityConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>!bool → Visibility</summary>
public class InverseBoolToVisibilityConverter : BaseValueConverter<InverseBoolToVisibilityConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>!bool</summary>
public class InverseBoolConverter : BaseValueConverter<InverseBoolConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is false;
    }
}

/// <summary>bool → "【查看】" / "【隐藏】"（用于工具调用块的折叠按钮）</summary>
public class ExpandLabelConverter : BaseValueConverter<ExpandLabelConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "【隐藏】" : "【查看】";
    }
}

/// <summary>角色 → 编辑按钮可见性：仅 user 角色显示</summary>
public class RoleToEditVisibilityConverter : BaseValueConverter<RoleToEditVisibilityConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == "user" ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>基础单例转换器（避免每次绑定都 new）</summary>
public abstract class BaseValueConverter<T> : System.Windows.Data.IValueConverter where T : new()
{
    public static T Instance { get; } = new();

    public abstract object Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public virtual object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 百分比转换器：将输入值乘以百分比系数。
/// ConverterParameter 传入小数（如 0.8 表示 80%）。
/// 用于 MaxWidth 绑定，如 MaxWidth="{Binding ActualWidth, Converter={...}, ConverterParameter=0.8}"。
/// </summary>
public class PercentageConverter : BaseValueConverter<PercentageConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double raw || parameter is not string paramStr)
            return value ?? 0d;

        if (!double.TryParse(paramStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
            return raw;

        return raw * factor;
    }
}
