using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PowerSaver.Services;

/// <summary>
/// 管理开机自启动。通过任务计划程序注册「随登录以管理员权限静默启动」的计划任务，
/// 并显式关闭电池条件限制；若注册失败则回退到注册表 Run 键。
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "LaptopPowerSaver";
    private const string RunValueName = "LaptopPowerSaver";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>启动参数：以托盘方式后台静默运行。</summary>
    public const string TrayArgument = "--tray";

    public static bool IsEnabled()
    {
        try
        {
            if (TaskExists())
            {
                return true;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(RunValueName) is string;
        }
        catch (Exception ex)
        {
            Logger.Warn($"查询自启动状态失败：{ex.Message}");
            return false;
        }
    }

    public static void Enable()
    {
        var exe = AppPaths.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            throw new InvalidOperationException("无法获取可执行文件路径。");
        }

        if (TryCreateScheduledTask(exe, out var output))
        {
            Logger.Info("已注册计划任务自启动项（随登录以管理员权限静默启动，已关闭电池条件限制）。");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Logger.Info("任务设置：" + Environment.NewLine + output.Trim());
            }

            // 清理可能残留的注册表 Run 项，避免重复启动。
            RemoveRunValue();
            return;
        }

        Logger.Warn($"注册计划任务失败，回退到注册表 Run 键。原因：{output}");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
                        ?? throw new InvalidOperationException("无法写入注册表 Run 键。");
        key.SetValue(RunValueName, $"\"{exe}\" {TrayArgument}");
        Logger.Info("已通过注册表 Run 键创建自启动项。");
    }

    public static void Disable()
    {
        TryRunSchtasks($"/Delete /TN \"{TaskName}\" /F", out _);
        RemoveRunValue();
        Logger.Info("已移除自启动项。");
    }

    /// <summary>
    /// 通过 PowerShell 的 Register-ScheduledTask 注册任务。
    /// 之所以不用 schtasks 命令行：它的参数无法设置电池条件、启动补跑与执行超时等，
    /// 这些默认值（登录时若在用电池则跳过、错过不补跑、72 小时强制结束）正是自启动失效的原因。
    /// </summary>
    private static bool TryCreateScheduledTask(string exe, out string output)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "LaptopPowerSaver.autostart.ps1");
        try
        {
            File.WriteAllText(scriptPath, BuildPowerShellScript(exe), new UTF8Encoding(true));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                output = "无法启动 powershell.exe";
                return false;
            }

            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 忽略
                }

                output = "powershell 执行超时";
                return false;
            }

            output = (stdOut + stdErr).Trim();
            if (process.ExitCode != 0)
            {
                return false;
            }

            if (!TaskExists())
            {
                output = "命令执行成功但未找到任务。" + output;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>生成注册任务并回读设置的 PowerShell 脚本。</summary>
    private static string BuildPowerShellScript(string exe)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
        var exeArg = exe.Replace("'", "''");
        var sidArg = sid.Replace("'", "''");

        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$action = New-ScheduledTaskAction -Execute '{exeArg}' -Argument '{TrayArgument}'",
            "$trigger = New-ScheduledTaskTrigger -AtLogOn",
            // 关键：允许在电池上启动、切到电池不停止、错过后补跑、无执行时长上限。
            // MultipleInstances 用 Parallel：即使某个实例异常挂起，也不会像 IgnoreNew 那样
            // 让后续所有登录触发被忽略；重复实例由程序内的单实例互斥体立即退出。
            "$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries " +
            "-StartWhenAvailable -MultipleInstances Parallel " +
            "-ExecutionTimeLimit (New-TimeSpan -Seconds 0)",
            $"$principal = New-ScheduledTaskPrincipal -UserId '{sidArg}' -LogonType Interactive -RunLevel Highest",
            $"Register-ScheduledTask -TaskName '{TaskName}' -Action $action -Trigger $trigger " +
            "-Settings $settings -Principal $principal -Force | Out-Null",
            $"$t = Get-ScheduledTask -TaskName '{TaskName}'",
            "$t.Settings | Format-List DisallowStartIfOnBatteries,StopIfGoingOnBatteries,StartWhenAvailable," +
            "ExecutionTimeLimit,MultipleInstances,AllowHardTerminate | Out-String"
        });
    }

    private static bool TaskExists()
        => TryRunSchtasks($"/Query /TN \"{TaskName}\"", out _);

    private static void RemoveRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key?.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"清理注册表自启动项失败：{ex.Message}");
        }
    }

    private static bool TryRunSchtasks(string arguments, out string output)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                output = "无法启动 schtasks.exe";
                return false;
            }

            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(8000))
            {
                output = "schtasks 执行超时";
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 忽略
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }
}
