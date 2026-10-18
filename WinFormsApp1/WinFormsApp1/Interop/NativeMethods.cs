using System.Runtime.InteropServices;

namespace PowerSaver.Interop;

/// <summary>
/// 集中存放本程序使用的系统 API 声明（SetupAPI / CfgMgr32 / User32 / Kernel32）。
/// </summary>
internal static class NativeMethods
{
    // ------------------------------------------------------------------
    // SetupAPI —— 枚举指定设备类下的设备
    // ------------------------------------------------------------------

    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_ALLCLASSES = 0x00000004;

    internal const uint SPDRP_DEVICEDESC = 0x00000000;
    internal const uint SPDRP_HARDWAREID = 0x00000001;
    internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    internal const uint SPDRP_DEVICE_DESCRIPTION = 0x00000000;

    internal const int MAX_DEVICE_ID_LEN = 200;
    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;

    internal static readonly Guid GUID_DEVCLASS_DISPLAY = new("4d36e968-e325-11ce-bfc1-08002be10318");

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        System.Text.StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ------------------------------------------------------------------
    // SetupAPI —— 使用 DIF_PROPERTYCHANGE 启用/禁用设备
    // 与「设备管理器」的启用/禁用行为一致，可互相逆转。
    // ------------------------------------------------------------------

    internal const uint DIF_PROPERTYCHANGE = 0x00000012;

    internal const uint DICS_ENABLE = 0x00000001;
    internal const uint DICS_DISABLE = 0x00000002;

    internal const uint DICS_FLAG_GLOBAL = 0x00000001;
    internal const uint DICS_FLAG_CONFIGSPECIFIC = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiCallClassInstaller(
        uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    // ------------------------------------------------------------------
    // CfgMgr32 —— 定位设备节点并查询状态
    // 注意：禁用/启用请使用上面的 SetupAPI DIF_PROPERTYCHANGE，
    // 不要使用 CM_Disable_DevNode（其语义与设备管理器不一致，
    // 会导致设备无法被重新启用）。
    // ------------------------------------------------------------------

    internal const int CR_SUCCESS = 0;

    internal const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
    internal const uint CM_LOCATE_DEVNODE_PHANTOM = 0x00000001;

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    internal static extern int CM_Get_DevNode_Status(
        out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    internal const uint DN_HAS_PROBLEM = 0x00000400;
    internal const uint DN_DRIVER_LOADED = 0x00000002;
    internal const uint CM_PROB_DISABLED = 22;

    // ------------------------------------------------------------------
    // User32 —— 枚举显示适配器 / 显示器、查询与修改显示模式
    // ------------------------------------------------------------------

    internal const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    internal const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
    internal const int DISPLAY_DEVICE_MIRRORING_DRIVER = 0x00000008;

    internal const int ENUM_CURRENT_SETTINGS = -1;
    internal const int ENUM_REGISTRY_SETTINGS = -2;

    internal const int DM_POSITION = 0x00000020;
    internal const int DM_BITSPERPEL = 0x00040000;
    internal const int DM_PELSWIDTH = 0x00080000;
    internal const int DM_PELSHEIGHT = 0x00100000;
    internal const int DM_DISPLAYFREQUENCY = 0x00400000;

    internal const int CDS_UPDATEREGISTRY = 0x00000001;
    internal const int CDS_TEST = 0x00000002;

    internal const int DISP_CHANGE_SUCCESSFUL = 0;
    internal const int DISP_CHANGE_RESTART = 1;
    internal const int DISP_CHANGE_FAILED = -1;
    internal const int DISP_CHANGE_BADMODE = -2;
    internal const int DISP_CHANGE_NOTUPDATED = -3;
    internal const int DISP_CHANGE_BADFLAGS = -4;
    internal const int DISP_CHANGE_BADPARAM = -5;
    internal const int DISP_CHANGE_BADDUALVIEW = -6;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    /// <summary>与 Windows DEVMODEW 结构体二进制布局一致。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(
        string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettings(
        string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsEx(
        string? deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

    // ------------------------------------------------------------------
    // Kernel32 —— 获取系统电源状态
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;      // 0=离线(电池) 1=在线(交流) 255=未知
        public byte BatteryFlag;       // 1=高 2=低 4=临界 8=充电 128=无电池 255=未知
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    internal const byte AC_LINE_OFFLINE = 0;
    internal const byte AC_LINE_ONLINE = 1;
    internal const byte AC_LINE_UNKNOWN = 255;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

    // ------------------------------------------------------------------
    // 窗口与托盘相关
    // ------------------------------------------------------------------

    internal const int WM_POWERBROADCAST = 0x0218;
    internal const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
    internal const int PBT_APMSUSPEND = 0x0004;

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>注册窗口消息，用于接收 Explorer 任务栏重建时的 TaskbarCreated 广播。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);
}
