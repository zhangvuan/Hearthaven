using CommunityToolkit.WinUI.Notifications;
using Windows.Data.Xml.Dom;

namespace Hearthaven.Services;

/// <summary>
/// 通知服务 — 封装 Windows 原生 Toast 通知。
/// 独立于任何 ViewModel，可在应用各处复用（如定时任务、后台进程等）。
/// </summary>
public static class NotificationService
{
    /// <summary>
    /// 发送 Windows 原生 Toast 通知。
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="body">通知正文（建议不超过 100 字）</param>
    public static void Show(string title, string body)
    {
        var xml = $@"<toast><visual><binding template='ToastGeneric'><text>{EscapeXml(title)}</text><text>{EscapeXml(body)}</text></binding></visual></toast>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var toast = new Windows.UI.Notifications.ToastNotification(doc);
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
