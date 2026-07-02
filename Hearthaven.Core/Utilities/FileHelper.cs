namespace Hearthaven.Core.Utilities;

/// <summary>
/// 文件系统通用工具方法。
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// 安全地迭代枚举文件，捕获并跳过无权限访问的目录（如 System Volume Information）。
    /// 使用显式栈实现迭代式 DFS，避免递归导致的 StackOverflowException。
    /// </summary>
    /// <param name="dirPath">起始目录</param>
    /// <param name="searchPattern">文件通配符，默认 "*" 表示所有文件</param>
    /// <param name="maxSkippedDirs">最多连续跳过多少个不可访问的目录后停止，null 表示不限制</param>
    public static IEnumerable<string> SafeEnumerateFiles(
        string dirPath,
        string searchPattern = "*",
        int? maxSkippedDirs = null)
    {
        var stack = new Stack<string>();
        stack.Push(dirPath);
        var skippedCount = 0;

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();

            if (maxSkippedDirs.HasValue && skippedCount > maxSkippedDirs.Value)
                yield break;

            // 枚举当前目录的文件
            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                skippedCount++;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            // 枚举子目录（压栈供下一轮处理）
            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                skippedCount++;
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subDir in subDirs)
                stack.Push(subDir);
        }
    }
}
