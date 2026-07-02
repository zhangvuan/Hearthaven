namespace Hearthaven.Core.Data;

/// <summary>
/// 会话表
/// </summary>
public class SessionEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "新对话";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>工作目录（会话级），null=使用用户数据目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>模型名称（会话级），null=使用 DefaultModel</summary>
    public string? ModelName { get; set; }

    /// <summary>对话模式（会话级），"normal"/"code"/"creative"，默认 "normal"</summary>
    public string Mode { get; set; } = "normal";

    // 导航属性
    public List<MessageEntity> Messages { get; set; } = [];
}
