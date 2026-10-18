using System.Text.Json;

namespace PowerSaver.Services;

/// <summary>
/// 上一次应用省电策略时留下的状态，持久化到磁盘，供重启后判断与恢复。
/// </summary>
public sealed class PowerSaveState
{
    public bool Applied { get; set; }

    /// <summary>被禁用的 GPU 设备实例 ID（未启用该策略时为空）。</summary>
    public string? GpuInstanceId { get; set; }

    /// <summary>被调整的显示器设备名（未启用该策略时为空）。</summary>
    public string? DisplayDeviceName { get; set; }

    /// <summary>应用后的低功耗显示参数。</summary>
    public int AppliedWidth { get; set; }
    public int AppliedHeight { get; set; }
    public int AppliedRefreshRate { get; set; }
    public int AppliedBitsPerPel { get; set; }

    /// <summary>应用前的原始显示参数（跨重启恢复用）。</summary>
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int OriginalRefreshRate { get; set; }
    public int OriginalBitsPerPel { get; set; }

    public DateTime AppliedAt { get; set; }
}

/// <summary>
/// 省电状态的持久化存储（state.json）。
/// 重启后原分辨率等内存信息会丢失，必须落盘才能在下次启动时恢复。
/// </summary>
public static class PowerSaveStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly string StateFile =
        Path.Combine(AppPaths.DataDirectory, "state.json");

    public static void Save(PowerSaveState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state, Options));
            Logger.Info(
                $"已记录省电状态：GPU={state.GpuInstanceId ?? "-"}；" +
                $"显示={state.DisplayDeviceName ?? "-"}；" +
                $"低功耗={state.AppliedWidth}x{state.AppliedHeight}@{state.AppliedRefreshRate}Hz；" +
                $"原始={state.OriginalWidth}x{state.OriginalHeight}@{state.OriginalRefreshRate}Hz");
        }
        catch (Exception ex)
        {
            Logger.Warn($"保存省电状态失败：{ex.Message}");
        }
    }

    public static PowerSaveState? Load()
    {
        try
        {
            if (!File.Exists(StateFile))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PowerSaveState>(File.ReadAllText(StateFile), Options);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取省电状态失败：{ex.Message}");
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                File.Delete(StateFile);
                Logger.Info("已清除省电状态记录。");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"清除省电状态失败：{ex.Message}");
        }
    }
}
