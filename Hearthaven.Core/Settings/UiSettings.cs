namespace Hearthaven.Core.Settings;

/// <summary>UI 界面配置</summary>
public class UiSettings
{
    /// <summary>配色主题名，如 "Blue"（极简蓝白）/ "Warm"（暖橙米白）/ "Dark"（暗夜深色）</summary>
    public string Theme { get; set; } = "Blue";

    /// <summary>正文字体大小</summary>
    public int FontSize { get; set; } = 13;
}
