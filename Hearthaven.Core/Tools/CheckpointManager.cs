using System.Text.Json;

namespace Hearthaven.Core.Tools;

/// <summary>
/// 检查点管理器 — 编辑/写入文件前自动备份，支持列表查看和恢复。
/// 检查点存储在 %TEMP%/Hearthaven/checkpoints/，自动清理最旧的。
/// </summary>
public static class CheckpointManager
{
    private static readonly string CheckpointDir =
        Path.Combine(Path.GetTempPath(), "Hearthaven", "checkpoints");

    private static int _sequence;

    /// <summary>最大保留检查点数量</summary>
    public const int MaxCheckpoints = 30;

    /// <summary>
    /// 备份指定文件。如果文件不存在（新建场景）则跳过。
    /// 返回检查点目录名，失败时返回 null。
    /// </summary>
    public static async Task<string?> BackupAsync(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null; // 新建文件无需备份

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var seq = Interlocked.Increment(ref _sequence);
            var dirName = $"{timestamp}_{seq:D3}";
            var checkpointPath = Path.Combine(CheckpointDir, dirName);
            Directory.CreateDirectory(checkpointPath);

            // 保存 manifest（记录原始路径和备份时间）
            var manifest = new CheckpointManifest
            {
                OriginalPath = fullPath,
                CreatedAt = DateTime.Now
            };
            await File.WriteAllTextAsync(
                Path.Combine(checkpointPath, "manifest.json"),
                JsonSerializer.Serialize(manifest));

            // 复制文件内容
            var destPath = Path.Combine(checkpointPath, "content.bak");
            await CopyFileAsync(fullPath, destPath);

            // 清理超出上限的旧检查点
            CleanupOldCheckpoints();

            return dirName;
        }
        catch
        {
            return null; // 备份失败不影响主流程
        }
    }

    /// <summary>获取所有检查点列表（按时间倒序）</summary>
    public static List<CheckpointInfo> ListCheckpoints(string? fileFilter = null)
    {
        if (!Directory.Exists(CheckpointDir))
            return [];

        var list = new List<CheckpointInfo>();
        foreach (var dir in Directory.GetDirectories(CheckpointDir))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            var bakPath = Path.Combine(dir, "content.bak");
            if (!File.Exists(manifestPath) || !File.Exists(bakPath))
                continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<CheckpointManifest>(
                    File.ReadAllText(manifestPath));
                if (manifest == null) continue;

                // 如果指定了文件过滤，只保留匹配的
                if (fileFilter != null &&
                    !manifest.OriginalPath.Equals(fileFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dirName = Path.GetFileName(dir);
                var fileInfo = new FileInfo(bakPath);
                list.Add(new CheckpointInfo
                {
                    Id = dirName,
                    OriginalPath = manifest.OriginalPath,
                    CreatedAt = manifest.CreatedAt,
                    FileSize = fileInfo.Length,
                    FileName = Path.GetFileName(manifest.OriginalPath)
                });
            }
            catch { continue; }
        }

        return list.OrderByDescending(c => c.CreatedAt).ToList();
    }

    /// <summary>恢复指定检查点</summary>
    public static (bool success, string message) Restore(string checkpointId)
    {
        var checkpointPath = Path.Combine(CheckpointDir, checkpointId);
        var manifestPath = Path.Combine(checkpointPath, "manifest.json");
        var bakPath = Path.Combine(checkpointPath, "content.bak");

        if (!File.Exists(manifestPath) || !File.Exists(bakPath))
            return (false, $"错误：检查点 '{checkpointId}' 不存在或已损坏");

        try
        {
            var manifest = JsonSerializer.Deserialize<CheckpointManifest>(
                File.ReadAllText(manifestPath));
            if (manifest == null)
                return (false, "错误：检查点 manifest 损坏");

            // 确保目标目录存在
            var parentDir = Path.GetDirectoryName(manifest.OriginalPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            // 从备份恢复
            File.Copy(bakPath, manifest.OriginalPath, overwrite: true);
            return (true, $"已恢复 {manifest.OriginalPath}（{manifest.CreatedAt:yyyy-MM-dd HH:mm:ss} 的版本）");
        }
        catch (Exception ex)
        {
            return (false, $"错误：恢复失败 — {ex.Message}");
        }
    }

    /// <summary>清理超出上限的旧检查点（保留最近 MaxCheckpoints 个）</summary>
    private static void CleanupOldCheckpoints()
    {
        if (!Directory.Exists(CheckpointDir)) return;

        var dirs = Directory.GetDirectories(CheckpointDir)
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .OrderByDescending(d => d.Name)
            .ToList();

        while (dirs.Count > MaxCheckpoints)
        {
            var oldest = dirs[^1];
            try { Directory.Delete(oldest.Path, recursive: true); }
            catch { /* 忽略删除失败 */ }
            dirs.RemoveAt(dirs.Count - 1);
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        // 使用流式拷贝避免大文件内存占用
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        await source.CopyToAsync(dest);
        await dest.FlushAsync();
    }

    // ──────────────── 公共类型 ────────────────

    public class CheckpointInfo
    {
        public string Id { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; } = "";
    }

    private class CheckpointManifest
    {
        public string OriginalPath { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
