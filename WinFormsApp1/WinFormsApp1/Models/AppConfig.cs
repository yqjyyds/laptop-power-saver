using System.Text.Json.Serialization;

namespace PowerSaver.Models;

/// <summary>
/// 用户配置模型，序列化为 JSON 持久化保存。
/// </summary>
public sealed class AppConfig
{
    /// <summary>是否已完成首次配置向导。</summary>
    public bool Configured { get; set; }

    /// <summary>待禁用 GPU 的设备实例 ID（PNP 设备实例路径）。</summary>
    public string? GpuInstanceId { get; set; }

    /// <summary>待禁用 GPU 的显示名称（仅用于界面展示）。</summary>
    public string? GpuName { get; set; }

    /// <summary>是否需要执行 GPU 禁用策略。</summary>
    public bool EnableGpuDisable { get; set; } = true;

    /// <summary>目标显示器设备名，例如 \\.\DISPLAY1。</summary>
    public string? DisplayDeviceName { get; set; }

    /// <summary>目标显示器友好名称（仅用于界面展示）。</summary>
    public string? DisplayName { get; set; }

    /// <summary>是否需要执行显示参数调整策略。</summary>
    public bool EnableDisplayChange { get; set; } = true;

    /// <summary>低功耗档位：宽度。</summary>
    public int Width { get; set; }

    /// <summary>低功耗档位：高度。</summary>
    public int Height { get; set; }

    /// <summary>低功耗档位：刷新率（Hz）。</summary>
    public int RefreshRate { get; set; }

    /// <summary>低功耗档位：色深（位）。</summary>
    public int BitsPerPel { get; set; } = 32;

    /// <summary>检测到切换为电池供电后，是否在通知弹出后自动执行（无需人工点击）。</summary>
    public bool AutoExecute { get; set; }

    /// <summary>是否启用开机自启动。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>通知自动执行前的等待秒数。</summary>
    public int AutoExecuteDelaySeconds { get; set; } = 10;

    [JsonIgnore]
    public bool HasGpuTarget => EnableGpuDisable && !string.IsNullOrWhiteSpace(GpuInstanceId);

    [JsonIgnore]
    public bool HasDisplayTarget => EnableDisplayChange && !string.IsNullOrWhiteSpace(DisplayDeviceName);
}
