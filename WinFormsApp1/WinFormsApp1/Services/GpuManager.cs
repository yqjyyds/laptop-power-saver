using System.Runtime.InteropServices;
using System.Text;
using PowerSaver.Interop;
using static PowerSaver.Interop.NativeMethods;

namespace PowerSaver.Services;

/// <summary>
/// 控制显卡设备的启用/禁用。
/// 采用 SetupAPI 的 DIF_PROPERTYCHANGE（DICS_DISABLE / DICS_ENABLE），
/// 与「设备管理器」的启用/禁用行为一致，因此可以互相逆转。
/// 所有调用均需要管理员权限。
/// </summary>
public static class GpuManager
{
    /// <summary>禁用指定显卡。成功返回 true。</summary>
    public static bool Disable(string instanceId) => SetDeviceEnabled(instanceId, enable: false);

    /// <summary>启用指定显卡（用于回滚）。成功返回 true。</summary>
    public static bool Enable(string instanceId) => SetDeviceEnabled(instanceId, enable: true);

    public static bool IsDeviceEnabled(string instanceId) => HardwareEnumerator.IsDeviceEnabled(instanceId);

    private static bool SetDeviceEnabled(string instanceId, bool enable)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        IntPtr hDevInfo = IntPtr.Zero;
        try
        {
            var classGuid = GUID_DEVCLASS_DISPLAY;
            hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
            {
                Logger.Error($"获取显示设备类失败，错误码 {Marshal.GetLastWin32Error()}");
                return false;
            }

            if (!TryFindDevice(hDevInfo, instanceId, out var data))
            {
                Logger.Error($"未在显示设备类中找到目标设备：{instanceId}");
                return false;
            }

            // 已是目标状态则无需操作（禁用/启用均幂等）。
            var currentlyEnabled = IsDeviceEnabled(instanceId);
            if (currentlyEnabled == enable)
            {
                Logger.Info($"设备已处于{(enable ? "启用" : "禁用")}状态，跳过：{instanceId}");
                return true;
            }

            // 先按「全局」范围执行，与设备管理器一致。
            if (!ApplyPropertyChange(hDevInfo, ref data, enable, DICS_FLAG_GLOBAL))
            {
                return false;
            }

            // 启用时还需按「当前配置」范围再执行一次，才能被完全启用。
            if (enable)
            {
                ApplyPropertyChange(hDevInfo, ref data, true, DICS_FLAG_CONFIGSPECIFIC);
            }

            // 校验实际结果（属性变更可能稍有延迟）。
            if (!VerifyState(instanceId, enable))
            {
                Logger.Error($"{(enable ? "启用" : "禁用")}设备后状态校验未通过：{instanceId}");
                return false;
            }

            Logger.Info($"{(enable ? "已启用" : "已禁用")}设备：{instanceId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{(enable ? "启用" : "禁用")}设备时发生异常：{instanceId}", ex);
            return false;
        }
        finally
        {
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }
    }

    private static bool ApplyPropertyChange(IntPtr hDevInfo, ref SP_DEVINFO_DATA data, bool enable, uint scope)
    {
        var pcp = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                InstallFunction = DIF_PROPERTYCHANGE
            },
            StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
            Scope = scope,
            HwProfile = 0
        };

        var size = Marshal.SizeOf<SP_PROPCHANGE_PARAMS>();
        if (!SetupDiSetClassInstallParams(hDevInfo, ref data, ref pcp, size))
        {
            Logger.Error($"SetupDiSetClassInstallParams 失败，错误码 {Marshal.GetLastWin32Error()}");
            return false;
        }

        if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, hDevInfo, ref data))
        {
            Logger.Error($"SetupDiCallClassInstaller(DIF_PROPERTYCHANGE) 失败，错误码 {Marshal.GetLastWin32Error()}");
            return false;
        }

        return true;
    }

    private static bool TryFindDevice(IntPtr hDevInfo, string instanceId, out SP_DEVINFO_DATA data)
    {
        data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };

        for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref data); i++)
        {
            var sb = new StringBuilder(MAX_DEVICE_ID_LEN);
            if (SetupDiGetDeviceInstanceId(hDevInfo, ref data, sb, sb.Capacity, out _)
                && string.Equals(sb.ToString(), instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
        }

        return false;
    }

    /// <summary>设备属性变更可能稍有延迟，重试若干次后再判定结果。</summary>
    private static bool VerifyState(string instanceId, bool expectEnabled)
    {
        for (var i = 0; i < 8; i++)
        {
            if (IsDeviceEnabled(instanceId) == expectEnabled)
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return IsDeviceEnabled(instanceId) == expectEnabled;
    }
}
