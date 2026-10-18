using System.Runtime.InteropServices;
using System.Text;
using PowerSaver.Interop;
using PowerSaver.Models;
using static PowerSaver.Interop.NativeMethods;

namespace PowerSaver.Services;

/// <summary>
/// 枚举系统中的 GPU 设备、已连接显示器与其支持的显示模式。
/// </summary>
public static class HardwareEnumerator
{
    /// <summary>
    /// 枚举 Display 设备类下的所有显卡，并标记哪些正驱动着显示器（直连 GPU）。
    /// </summary>
    public static IReadOnlyList<GpuDevice> GetGpus()
    {
        var result = new List<GpuDevice>();
        var connectedIds = GetAdaptersDrivingDisplay();

        var classGuid = GUID_DEVCLASS_DISPLAY;
        var hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
        {
            Logger.Warn("SetupDiGetClassDevs 获取显示设备类失败。");
            return result;
        }

        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref data); i++)
            {
                var instanceId = GetInstanceId(hDevInfo, ref data);
                if (string.IsNullOrEmpty(instanceId))
                {
                    continue;
                }

                var hardwareIds = GetMultiStringProperty(hDevInfo, ref data, SPDRP_HARDWAREID);
                var primaryHardwareId = hardwareIds.FirstOrDefault() ?? string.Empty;
                var friendly = GetStringProperty(hDevInfo, ref data, SPDRP_FRIENDLYNAME);
                var desc = GetStringProperty(hDevInfo, ref data, SPDRP_DEVICEDESC);

                var isConnected = connectedIds.Any(cid => IdMatches(instanceId, cid));
                var isEnabled = IsDeviceEnabled(instanceId);

                result.Add(new GpuDevice
                {
                    InstanceId = instanceId,
                    HardwareId = primaryHardwareId,
                    Name = string.IsNullOrWhiteSpace(friendly) ? desc : friendly,
                    Description = desc,
                    IsConnectedToDisplay = isConnected,
                    IsEnabled = isEnabled
                });
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return result;
    }

    /// <summary>
    /// 枚举当前已连接到桌面的显示器。
    /// </summary>
    public static IReadOnlyList<DisplayDevice> GetDisplays()
    {
        var list = new List<DisplayDevice>();

        foreach (var screen in Screen.AllScreens)
        {
            var deviceName = screen.DeviceName;
            var friendly = GetMonitorFriendlyName(deviceName);
            var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            var ok = EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref current);

            list.Add(new DisplayDevice
            {
                DeviceName = deviceName,
                FriendlyName = string.IsNullOrWhiteSpace(friendly) ? deviceName : friendly,
                IsPrimary = screen.Primary,
                CurrentWidth = ok ? (int)current.dmPelsWidth : screen.Bounds.Width,
                CurrentHeight = ok ? (int)current.dmPelsHeight : screen.Bounds.Height,
                CurrentRefreshRate = ok ? (int)current.dmDisplayFrequency : 0,
                CurrentBitsPerPel = ok ? (int)current.dmBitsPerPel : 32
            });
        }

        return list;
    }

    /// <summary>
    /// 枚举指定显示器支持的全部显示模式（去重并按分辨率/刷新率排序）。
    /// </summary>
    public static IReadOnlyList<DisplayModeInfo> GetSupportedModes(string deviceName)
    {
        var modes = new List<DisplayModeInfo>();
        var seen = new HashSet<DisplayModeInfo>();

        var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        for (var i = 0; EnumDisplaySettings(deviceName, i, ref devMode); i++)
        {
            // 过滤掉非法的 0 尺寸模式（部分驱动会返回）。
            if (devMode.dmPelsWidth == 0 || devMode.dmPelsHeight == 0)
            {
                continue;
            }

            var mode = new DisplayModeInfo
            {
                Width = (int)devMode.dmPelsWidth,
                Height = (int)devMode.dmPelsHeight,
                RefreshRate = (int)devMode.dmDisplayFrequency,
                BitsPerPel = (int)devMode.dmBitsPerPel
            };

            if (seen.Add(mode))
            {
                modes.Add(mode);
            }
        }

        return modes
            .OrderBy(m => m.Width)
            .ThenBy(m => m.Height)
            .ThenBy(m => m.RefreshRate)
            .ToList();
    }

    /// <summary>
    /// 获取显示器友好名称（通过 EnumDisplayDevices 二级枚举）。
    /// </summary>
    private static string GetMonitorFriendlyName(string deviceName)
    {
        var dd = CreateDisplayDevice();
        if (EnumDisplayDevices(deviceName, 0, ref dd, 0))
        {
            return dd.DeviceString;
        }

        return string.Empty;
    }

    /// <summary>
    /// 返回当前正驱动着活动显示器的显示适配器硬件 ID 列表。
    /// </summary>
    private static List<string> GetAdaptersDrivingDisplay()
    {
        var ids = new List<string>();
        var dd = CreateDisplayDevice();

        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            // 跳过镜像驱动（虚拟显示适配器）。
            if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0)
            {
                continue;
            }

            // 仅统计已连接到桌面的适配器。
            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(dd.DeviceID))
            {
                ids.Add(dd.DeviceID);
            }
        }

        return ids;
    }

    private static DISPLAY_DEVICE CreateDisplayDevice()
        => new()
        {
            cb = Marshal.SizeOf<DISPLAY_DEVICE>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };

    /// <summary>
    /// 比较设备实例 ID 与显示适配器硬件 ID 是否指向同一块 GPU。
    /// 设备实例 ID 形如 PCI\VEN_8086&amp;DEV_3E9B&amp;SUBSYS_xxx&amp;REV_00\4&amp;2C5C...，
    /// 硬件 ID 形如 PCI\VEN_8086&amp;DEV_3E9B&amp;SUBSYS_xxx&amp;REV_00。
    /// </summary>
    private static bool IdMatches(string instanceId, string hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return false;
        }

        return instanceId.StartsWith(hardwareId, StringComparison.OrdinalIgnoreCase)
               || hardwareId.StartsWith(instanceId, StringComparison.OrdinalIgnoreCase)
               || instanceId.Contains(hardwareId, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDeviceEnabled(string instanceId)
    {
        var cr = CM_Locate_DevNodeW(out var devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL);
        if (cr != CR_SUCCESS)
        {
            return true;
        }

        cr = CM_Get_DevNode_Status(out var status, out var problem, devInst, 0);
        if (cr != CR_SUCCESS)
        {
            return true;
        }

        // 有 DI_ 问题码且为“已禁用”即视为禁用。
        return (status & DN_HAS_PROBLEM) == 0 || problem != CM_PROB_DISABLED;
    }

    private static string GetInstanceId(IntPtr hDevInfo, ref SP_DEVINFO_DATA data)
    {
        var sb = new StringBuilder(MAX_DEVICE_ID_LEN);
        return SetupDiGetDeviceInstanceId(hDevInfo, ref data, sb, sb.Capacity, out _)
            ? sb.ToString()
            : string.Empty;
    }

    private static string GetStringProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA data, uint property)
    {
        var buffer = new byte[2048];
        if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref data, property, out _, buffer, (uint)buffer.Length, out var size)
            && size > 2)
        {
            return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        }

        return string.Empty;
    }

    private static List<string> GetMultiStringProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA data, uint property)
    {
        var buffer = new byte[4096];
        if (!SetupDiGetDeviceRegistryProperty(hDevInfo, ref data, property, out _, buffer, (uint)buffer.Length, out var size)
            || size <= 2)
        {
            return new List<string>();
        }

        var raw = Encoding.Unicode.GetString(buffer, 0, (int)size);
        return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
