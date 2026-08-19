using System.IO;

namespace ToolHelper.Services;

/// <summary>
/// 简单的文件日志工具，将日志写入 logs 文件夹
/// </summary>
public static class FileLogger
{
    private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static readonly object _lock = new();

    static FileLogger()
    {
        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);
    }

    /// <summary>
    /// 写入日志到文件（按天分文件）
    /// </summary>
    public static void Write(string category, string message)
    {
        try
        {
            var fileName = $"{category}_{DateTime.Now:yyyyMMdd}.log";
            var filePath = Path.Combine(LogDir, fileName);
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\n";

            lock (_lock)
            {
                File.AppendAllText(filePath, logLine, System.Text.Encoding.UTF8);
            }
        }
        catch
        {
            // 忽略日志写入失败
        }
    }

    /// <summary>
    /// 获取日志目录路径
    /// </summary>
    public static string GetLogDirectory() => LogDir;
}
