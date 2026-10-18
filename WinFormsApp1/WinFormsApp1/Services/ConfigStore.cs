using System.Text.Json;
using System.Text.Json.Serialization;
using PowerSaver.Models;

namespace PowerSaver.Services;

/// <summary>
/// 配置的读写与内存单例持有。
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>当前生效的配置（进程内单例）。</summary>
    public static AppConfig Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                Current = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            }
            else
            {
                Current = new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取配置文件失败，使用默认配置。", ex);
            Current = new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(AppPaths.ConfigFile, json);
            Current = config;
            Logger.Info("配置已保存。");
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置文件失败。", ex);
            throw;
        }
    }

    public static bool IsConfigured()
    {
        try
        {
            return File.Exists(AppPaths.ConfigFile) && Current.Configured;
        }
        catch
        {
            return false;
        }
    }
}
