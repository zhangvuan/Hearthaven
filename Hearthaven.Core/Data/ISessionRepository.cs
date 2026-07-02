using Hearthaven.Core.Data;

namespace Hearthaven.Core.Data;

/// <summary>
/// 会话摘要（含预览）
/// </summary>
public record SessionWithPreview(string Id, string Title, DateTime UpdatedAt, string? Preview);

/// <summary>
/// 会话仓储接口 — 定义会话的持久化操作。
/// </summary>
public interface ISessionRepository
{
    /// <summary>根据 ID 获取会话</summary>
    Task<SessionEntity?> GetByIdAsync(string id);

    /// <summary>获取所有会话（按创建时间倒序）</summary>
    Task<List<SessionEntity>> GetAllOrderByCreatedAsync();

    /// <summary>获取所有会话及其最新消息的预览内容（一步 JOIN 查询，替代 N+1）</summary>
    Task<List<SessionWithPreview>> GetAllWithPreviewAsync();

    /// <summary>获取最新创建的会话（只查一条，代替全量加载）</summary>
    Task<SessionEntity?> GetLatestAsync();

    /// <summary>创建新会话，返回会话 ID</summary>
    Task<string> CreateAsync(string title = "新对话");

    /// <summary>更新会话标题</summary>
    Task UpdateTitleAsync(string id, string title);

    /// <summary>更新会话时间戳（标记为最近使用）</summary>
    Task TouchAsync(string id);

    /// <summary>批量更新会话属性（只更新非 null/非空值）</summary>
    Task UpdatePropertiesAsync(string id, string? workingDirectory = null, string? modelName = null, string? mode = null);

    /// <summary>删除会话（数据库级联删除关联消息）</summary>
    Task DeleteAsync(string id);
}
