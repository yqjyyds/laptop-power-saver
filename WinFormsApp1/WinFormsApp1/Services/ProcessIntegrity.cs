using System.Runtime.InteropServices;

namespace PowerSaver.Services;

/// <summary>
/// 读取当前进程令牌的完整性级别。
/// 这是本次自启动问题的关键线索：若程序位于带 Low 完整性标签的目录，
/// 非提权启动会得到 Low 令牌，导致无法写 ProgramData、无法注册托盘图标、无法执行特权操作。
/// </summary>
internal static class ProcessIntegrity
{
    private const int TokenIntegrityLevel = 25;
    private const uint TokenQuery = 0x0008;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>返回 Low / Medium / High / System 等名称，失败时返回 Unknown。</summary>
    public static string GetName()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return "Unknown";
            }

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var length);
            if (length <= 0)
            {
                return "Unknown";
            }

            buffer = Marshal.AllocHGlobal(length);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
            {
                return "Unknown";
            }

            // TOKEN_MANDATORY_LABEL -> SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
            var sid = Marshal.ReadIntPtr(buffer);
            var subAuthorityCount = Marshal.ReadByte(sid, 1);
            var lastRid = Marshal.ReadInt32(sid, 8 + (subAuthorityCount - 1) * 4);

            return lastRid switch
            {
                0x0000 => "Untrusted",
                0x1000 => "Low",
                0x2000 => "Medium",
                0x2100 => "MediumPlus",
                0x3000 => "High",
                0x4000 => "System",
                _ => $"Unknown(0x{lastRid:X})"
            };
        }
        catch
        {
            return "Unknown";
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }
}
