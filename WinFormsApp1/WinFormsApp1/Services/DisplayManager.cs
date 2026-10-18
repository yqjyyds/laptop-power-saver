using System.Runtime.InteropServices;
using PowerSaver.Interop;
using PowerSaver.Models;
using static PowerSaver.Interop.NativeMethods;

namespace PowerSaver.Services;

/// <summary>
/// 显示器的分辨率与刷新率查询/调整，基于 ChangeDisplaySettingsEx。
/// </summary>
public static class DisplayManager
{
    /// <summary>读取显示器当前生效的显示模式。</summary>
    public static bool TryGetCurrentMode(string deviceName, out DisplayModeInfo mode)
    {
        mode = new DisplayModeInfo();
        var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref devMode))
        {
            return false;
        }

        mode = new DisplayModeInfo
        {
            Width = (int)devMode.dmPelsWidth,
            Height = (int)devMode.dmPelsHeight,
            RefreshRate = (int)devMode.dmDisplayFrequency,
            BitsPerPel = (int)devMode.dmBitsPerPel
        };
        return true;
    }

    /// <summary>
    /// 将显示器切换到指定模式。成功时通过 <paramref name="previous"/> 返回切换前的模式，供回滚使用。
    /// </summary>
    public static bool ApplyMode(string deviceName, DisplayModeInfo target, out DisplayModeInfo? previous)
    {
        previous = null;

        // 1. 记录当前模式，以便回滚。
        var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref current))
        {
            Logger.Error($"读取当前显示模式失败：{deviceName}");
            return false;
        }

        previous = new DisplayModeInfo
        {
            Width = (int)current.dmPelsWidth,
            Height = (int)current.dmPelsHeight,
            RefreshRate = (int)current.dmDisplayFrequency,
            BitsPerPel = (int)current.dmBitsPerPel
        };

        // 2. 构造目标模式，保留原位置信息（多显示器场景必需）。
        var targetMode = current;
        targetMode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        targetMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT
                              | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;
        targetMode.dmPelsWidth = (uint)target.Width;
        targetMode.dmPelsHeight = (uint)target.Height;
        targetMode.dmDisplayFrequency = (uint)target.RefreshRate;
        targetMode.dmBitsPerPel = (uint)target.BitsPerPel;

        // 3. 先用 CDS_TEST 校验模式是否被驱动接受，避免直接切换导致黑屏。
        var testResult = ChangeDisplaySettingsEx(deviceName, ref targetMode, IntPtr.Zero,
            CDS_TEST, IntPtr.Zero);
        if (testResult != DISP_CHANGE_SUCCESSFUL)
        {
            Logger.Warn($"显示模式校验失败（{deviceName}）：{DescribeResult(testResult)}");
            return false;
        }

        // 4. 正式应用并写入注册表，使设置在重启后仍然生效。
        var result = ChangeDisplaySettingsEx(deviceName, ref targetMode, IntPtr.Zero,
            CDS_UPDATEREGISTRY, IntPtr.Zero);

        if (result != DISP_CHANGE_SUCCESSFUL)
        {
            Logger.Error($"应用显示模式失败（{deviceName}）：{DescribeResult(result)}");
            return false;
        }

        Logger.Info($"显示模式已切换（{deviceName}）：{target.DisplayText}");
        return true;
    }

    /// <summary>回滚到指定显示模式。</summary>
    public static bool Restore(string deviceName, DisplayModeInfo previous)
    {
        var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref devMode))
        {
            devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        }

        devMode.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT
                           | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;
        devMode.dmPelsWidth = (uint)previous.Width;
        devMode.dmPelsHeight = (uint)previous.Height;
        devMode.dmDisplayFrequency = (uint)previous.RefreshRate;
        devMode.dmBitsPerPel = (uint)previous.BitsPerPel;

        var result = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero,
            CDS_UPDATEREGISTRY, IntPtr.Zero);

        if (result != DISP_CHANGE_SUCCESSFUL)
        {
            Logger.Error($"回滚显示模式失败（{deviceName}）：{DescribeResult(result)}");
            return false;
        }

        Logger.Info($"显示模式已回滚（{deviceName}）：{previous.DisplayText}");
        return true;
    }

    private static string DescribeResult(int code) => code switch
    {
        DISP_CHANGE_SUCCESSFUL => "成功",
        DISP_CHANGE_RESTART => "需要重启",
        DISP_CHANGE_FAILED => "驱动切换失败",
        DISP_CHANGE_BADMODE => "模式不被支持",
        DISP_CHANGE_NOTUPDATED => "无法写入注册表",
        DISP_CHANGE_BADFLAGS => "参数标志无效",
        DISP_CHANGE_BADPARAM => "参数错误",
        DISP_CHANGE_BADDUALVIEW => "设备不支持双屏模式",
        _ => $"未知错误({code})"
    };
}
