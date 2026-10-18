using System.Text;

namespace PowerSaver.Services;

/// <summary>
/// 轻量级文件日志，便于排查硬件操作异常。
/// 主日志写入 ProgramData；若当前进程权限不足（非管理员时该目录只读），
/// 自动回退到当前用户的 LocalAppData，保证启动异常时始终有据可查。
/// </summary>
public static class Logger
{
    private static readonly object Sync = new();

    private static readonly string PrimaryPath =
        Path.Combine(AppPaths.DataDirectory, "app.log");

    private static readonly string FallbackPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopPowerSaver",
        "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

        lock (Sync)
        {
            if (TryAppend(PrimaryPath, line))
            {
                return;
            }

            TryAppend(FallbackPath, line);
        }
    }

    private static bool TryAppend(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, line, Encoding.UTF8);
            return true;
        }
        catch
        {
            // 日志失败不能影响主流程
            return false;
        }
    }
}

/// <summary>
/// 统一的路径管理。
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LaptopPowerSaver");

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;
}
