using System.Diagnostics;
using System.Security.Principal;
using PowerSaver.Forms;
using PowerSaver.Services;

namespace PowerSaver;

internal static class Program
{
    /// <summary>跨会话的单实例互斥体，避免多个实例同时操作显卡与显示参数。</summary>
    private const string SingleInstanceMutexName = @"Global\LaptopPowerSaver.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        var startHidden = IsTrayLaunch(args);
        var elevated = IsElevated();

        // 尽早记录启动上下文，便于排查开机自启动问题。
        Logger.Info($"===== 程序启动 | 参数=[{string.Join(" ", args)}] | 会话={Process.GetCurrentProcess().SessionId} | 管理员={elevated} | 完整性={ProcessIntegrity.GetName()} =====");

        ApplicationConfiguration.Initialize();

        // 全局异常兜底，避免后台运行期间因未处理异常导致程序崩溃。
        Application.ThreadException += (_, e) =>
            Logger.Error("未处理的界面线程异常。", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("未处理的后台线程异常。", e.ExceptionObject as Exception);

        if (!elevated)
        {
            if (startHidden)
            {
                // 自启动（托盘）场景：正常情况下任务计划程序已提供管理员令牌。
                // 走到这里说明提权失败，只记录日志后退出——绝不做模态阻塞，
                // 否则会挂死进程，让任务实例一直处于运行状态，后续登录触发被忽略。
                Logger.Error("自启动模式下未获得管理员权限，程序退出。");
                return;
            }

            // 手动启动：以管理员身份重新启动自身（会弹出一次 UAC，与原先清单行为一致）。
            Logger.Info("当前为非管理员权限，正在请求以管理员身份重新启动。");
            if (!TryRestartElevated(args))
            {
                MessageBox.Show(
                    "本程序需要管理员权限才能禁用显卡设备与调整显示参数，请右键以管理员身份运行。",
                    "需要管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return;
        }

        // 单实例检查。若已有实例在运行，本次启动快速退出（避免重复操作显卡与显示参数）。
        using var mutex = new Mutex(false, SingleInstanceMutexName);
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(3));
        }
        catch (AbandonedMutexException)
        {
            // 上一实例被强制结束，互斥体被遗弃，视为已获得。
            acquired = true;
        }

        if (!acquired)
        {
            Logger.Info("检测到已有实例正在运行，本次启动退出。");
            return;
        }

        try
        {
            ConfigStore.Load();

            // 首次运行强制弹出配置向导，禁止跳过。
            if (!ConfigStore.IsConfigured())
            {
                using var wizard = new ConfigWizardForm(isFirstRun: true);
                if (wizard.ShowDialog() != DialogResult.OK)
                {
                    // 用户未完成配置即退出。
                    return;
                }
            }

            // 启动自检：后台重新注册自启动任务，修正历史遗留的旧设置
            // （旧任务带有「电池上不启动/切电池即停止/错过不补跑/72 小时强制结束」等默认条件，会导致自启动失效）。
            if (ConfigStore.Current.AutoStart)
            {
                Task.Run(() =>
                {
                    try
                    {
                        AutoStartManager.Enable();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"自启动任务自检/修复失败：{ex.Message}");
                    }
                });
            }

            Application.Run(new MainForm(startHidden));
        }
        finally
        {
            Logger.Info("程序退出。");
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>
    /// 以管理员身份重新启动自身（手动启动且当前非提权时使用）。成功发起启动返回 true。
    /// </summary>
    private static bool TryRestartElevated(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            if (args.Length > 0)
            {
                psi.Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            }

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            // 用户在 UAC 对话框点“否”时会抛出 Win32Exception(1223)。
            Logger.Warn($"以管理员身份重新启动失败：{ex.Message}");
            return false;
        }
    }

    private static bool IsTrayLaunch(string[] args)
        => args.Any(a =>
            string.Equals(a, AutoStartManager.TrayArgument, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
