namespace PowerSaver.Models;

/// <summary>
/// 系统中的一块显卡设备（Display 设备类）。
/// </summary>
public sealed class GpuDevice
{
    /// <summary>设备实例 ID（SetupAPI 返回，用于 CfgMgr32 定位设备节点）。</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>硬件 ID，例如 PCI\VEN_8086&amp;DEV_...；用于与显示适配器比对。</summary>
    public string HardwareId { get; init; } = string.Empty;

    /// <summary>友好名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>厂商描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>当前是否正在驱动一个已连接到桌面的显示器（直连 GPU）。</summary>
    public bool IsConnectedToDisplay { get; init; }

    /// <summary>设备当前是否处于启用状态。</summary>
    public bool IsEnabled { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// 已连接的显示器。
/// </summary>
public sealed class DisplayDevice
{
    /// <summary>设备名，例如 \\.\DISPLAY1。</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>友好名称。</summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>是否为主显示器。</summary>
    public bool IsPrimary { get; init; }

    public int CurrentWidth { get; init; }
    public int CurrentHeight { get; init; }
    public int CurrentRefreshRate { get; init; }
    public int CurrentBitsPerPel { get; init; }

    public override string ToString() => FriendlyName;
}

/// <summary>
/// 某显示器支持的一种显示模式（分辨率 + 刷新率）。
/// </summary>
public sealed class DisplayModeInfo : IEquatable<DisplayModeInfo>
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int RefreshRate { get; init; }
    public int BitsPerPel { get; init; }

    public string DisplayText => $"{Width} x {Height}  @ {RefreshRate} Hz";

    public bool Equals(DisplayModeInfo? other)
        => other is not null
           && Width == other.Width
           && Height == other.Height
           && RefreshRate == other.RefreshRate
           && BitsPerPel == other.BitsPerPel;

    public override bool Equals(object? obj) => Equals(obj as DisplayModeInfo);

    public override int GetHashCode() => HashCode.Combine(Width, Height, RefreshRate, BitsPerPel);

    public override string ToString() => DisplayText;
}
