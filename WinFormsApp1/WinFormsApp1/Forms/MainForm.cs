using System.Drawing.Drawing2D;
using PowerSaver.Models;
using PowerSaver.Services;

namespace PowerSaver.Forms;

/// <summary>
/// 主窗口：系统托盘常驻、后台静默运行，监听电源事件并触发省电策略。
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>
    /// Explorer 重建任务栏（重启/崩溃后恢复）时广播的消息。
    /// 托盘图标注册在当前 Explorer 实例上，Explorer 重启后必须重新注册，否则图标永久消失。
    /// </summary>
    private static readonly uint WmTaskbarCreated = Interop.NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly bool _startHidden;
    private readonly PowerStatusMonitor _powerMonitor = new();
    private readonly PowerSaveManager _powerSave = new();
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayIconImage;

    private bool _reallyExit;
    private bool _hidingToTray;
    private System.Windows.Forms.Timer? _trayRetryTimer;

    private Label _lblPowerStatus = null!;
    private Label _lblStrategyStatus = null!;
    private Label _lblRuntimeStatus = null!;
    private ToolStripMenuItem _menuRestore = null!;
    private ToolStripMenuItem _menuApply = null!;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;

        // 高 DPI：先启用自动缩放，再搭建界面，保证 200% 缩放下布局同步放大。
        SuspendLayout();
        DpiSupport.Enable(this);
        BuildUi();
        ResumeLayout(false);
        PerformLayout();

        _trayIconImage = CreateTrayIcon();
        _trayIcon = new NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "笔记本省电助手",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        Load += OnFormLoad;
        Resize += OnFormResize;
    }

    // ------------------------------------------------------------------
    // 界面
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        Text = "笔记本省电助手";
        ClientSize = new Size(560, 340);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;
        ShowInTaskbar = !_startHidden;

        var title = new Label
        {
            Text = "笔记本省电助手",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 100, 180),
            Location = new Point(24, 20),
            AutoSize = true
        };

        var info = new Label
        {
            Text = "程序在后台静默运行，检测到切换为电池供电时会自动触发省电策略。",
            ForeColor = Color.FromArgb(120, 120, 120),
            Location = new Point(24, 56),
            Size = new Size(512, 20)
        };

        _lblPowerStatus = CreateStatusLabel(24, 92, "电源状态：正在检测…");
        _lblStrategyStatus = CreateStatusLabel(24, 132, "省电配置：加载中…");
        _lblRuntimeStatus = CreateStatusLabel(24, 172, "运行状态：正常");

        var btnApply = new Button
        {
            Text = "立即开启省电模式",
            Location = new Point(24, 232),
            Size = new Size(160, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 100, 180),
            ForeColor = Color.White
        };
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.Click += (_, _) => ApplyPowerSave(manual: true);

        var btnRestore = new Button
        {
            Text = "恢复原始配置",
            Location = new Point(196, 232),
            Size = new Size(140, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(228, 228, 228),
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        btnRestore.FlatAppearance.BorderSize = 0;
        btnRestore.Click += (_, _) => RestorePowerSave(manual: true);

        var btnReconfigure = new Button
        {
            Text = "重新配置",
            Location = new Point(348, 232),
            Size = new Size(110, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(228, 228, 228),
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        btnReconfigure.FlatAppearance.BorderSize = 0;
        btnReconfigure.Click += (_, _) => Reconfigure();

        var hint = new Label
        {
            Text = "关闭窗口将最小化到系统托盘继续后台运行。",
            ForeColor = Color.FromArgb(150, 150, 150),
            Location = new Point(24, 288),
            Size = new Size(512, 20)
        };

        Controls.Add(title);
        Controls.Add(info);
        Controls.Add(_lblPowerStatus);
        Controls.Add(_lblStrategyStatus);
        Controls.Add(_lblRuntimeStatus);
        Controls.Add(btnApply);
        Controls.Add(btnRestore);
        Controls.Add(btnReconfigure);
        Controls.Add(hint);
    }

    private static Label CreateStatusLabel(int x, int y, string text) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(512, 30),
        Font = new Font("Microsoft YaHei UI", 9.5F),
        ForeColor = Color.FromArgb(60, 60, 60)
    };

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        var show = new ToolStripMenuItem("显示主界面");
        show.Click += (_, _) => RestoreFromTray();

        _menuApply = new ToolStripMenuItem("立即开启省电模式");
        _menuApply.Click += (_, _) => ApplyPowerSave(manual: true);

        _menuRestore = new ToolStripMenuItem("恢复原始配置");
        _menuRestore.Click += (_, _) => RestorePowerSave(manual: true);

        var reconfigure = new ToolStripMenuItem("重新配置…");
        reconfigure.Click += (_, _) => Reconfigure();

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();

        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_menuApply);
        menu.Items.Add(_menuRestore);
        menu.Items.Add(reconfigure);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    private void OnFormLoad(object? sender, EventArgs e)
    {
        _powerMonitor.SwitchedToBattery += (_, _) => BeginInvoke(new Action(() => OnSwitchedToBattery()));
        _powerMonitor.SwitchedToAc += (_, _) => BeginInvoke(new Action(OnSwitchedToAc));
        _powerMonitor.Start();

        UpdateConfigSummary();
        UpdatePowerStatusLabel();

        // 交流电源下不应保留省电状态。启动时检查上次会话是否遗留了省电状态
        // （例如关机时在电池供电、开机时已插电），询问用户是否恢复原始配置。
        if (_powerMonitor.IsOnAcPower)
        {
            BeginInvoke(new Action(CheckLeftoverPowerSaveState));
        }

        if (_startHidden)
        {
            BeginInvoke(new Action(HideToTray));
        }

        // 启动时机可能早于 Explorer 托盘区就绪（登录触发尤其常见），此时图标会静默注册失败。
        // 延迟再注册一次作为兜底；Explorer 重启的场景由 WndProc 的 TaskbarCreated 处理。
        _trayRetryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _trayRetryTimer.Tick += (_, _) =>
        {
            _trayRetryTimer?.Stop();
            _trayRetryTimer?.Dispose();
            _trayRetryTimer = null;
            ReRegisterTrayIcon("启动后延迟校验");
        };
        _trayRetryTimer.Start();

        // 若启动时已处于电池供电，同样触发省电提示。
        if (_powerMonitor.IsOnBattery)
        {
            BeginInvoke(new Action(() => OnSwitchedToBattery()));
        }
    }

    /// <summary>重新注册托盘图标（先移除再添加），解决 Explorer 重启后图标丢失。</summary>
    private void ReRegisterTrayIcon(string reason)
    {
        try
        {
            _trayIcon.Visible = false;
            _trayIcon.Visible = true;
            Logger.Info($"{reason}：已重新注册托盘图标。");
        }
        catch (Exception ex)
        {
            Logger.Warn($"重新注册托盘图标失败：{ex.Message}");
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // Explorer 重启/崩溃恢复后会广播 TaskbarCreated，此时需要重新注册图标。
        if (m.Msg == (int)WmTaskbarCreated)
        {
            ReRegisterTrayIcon("检测到任务栏重建");
        }
    }

    /// <summary>
    /// 启动（或接入交流电源）时检查上次会话遗留的省电状态。
    /// 若系统当前仍处于省电状态，弹出确认窗口询问是否恢复原始配置；
    /// 30 秒倒计时结束仍未选择时自动恢复。
    /// </summary>
    private void CheckLeftoverPowerSaveState()
    {
        var state = PowerSaveStateStore.Load();
        if (state is null || !state.Applied)
        {
            return;
        }

        // 判断系统当前是否仍处于上次应用的状态，避免用户已手工恢复后重复提示。
        if (!IsStateStillApplied(state))
        {
            Logger.Info("检测到上次的省电状态已不存在（可能已手工恢复），清除记录。");
            PowerSaveStateStore.Clear();
            return;
        }

        Logger.Info("检测到上次遗留的省电状态，询问用户是否恢复原始配置。");
        UpdateRuntimeLabel("检测到系统仍处于省电状态，请确认是否恢复原始配置。", Color.FromArgb(180, 120, 30));

        using var prompt = new RestorePromptForm(30);
        var result = prompt.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            Logger.Info(prompt.AutoRestored ? "倒计时结束，自动恢复原始配置。" : "用户选择立即恢复原始配置。");
            PerformStartupRestore(state);
        }
        else
        {
            Logger.Info("用户选择保持当前省电状态。");
            PowerSaveStateStore.Clear();
            UpdateRuntimeLabel("已保持当前状态。", Color.FromArgb(60, 60, 60));
        }
    }

    /// <summary>判断持久化记录的省电状态在当前系统上是否仍然生效。</summary>
    private static bool IsStateStillApplied(PowerSaveState state)
    {
        var applied = false;

        if (!string.IsNullOrEmpty(state.DisplayDeviceName)
            && state.AppliedWidth > 0
            && DisplayManager.TryGetCurrentMode(state.DisplayDeviceName, out var current))
        {
            applied = current.Width == state.AppliedWidth
                      && current.Height == state.AppliedHeight
                      && current.RefreshRate == state.AppliedRefreshRate;
        }

        if (!string.IsNullOrEmpty(state.GpuInstanceId))
        {
            applied |= !GpuManager.IsDeviceEnabled(state.GpuInstanceId);
        }

        return applied;
    }

    /// <summary>按持久化的原始参数恢复显示与 GPU。</summary>
    private void PerformStartupRestore(PowerSaveState state)
    {
        var ok = true;

        // 先恢复显示，保证有可用画面。
        if (!string.IsNullOrEmpty(state.DisplayDeviceName) && state.OriginalWidth > 0)
        {
            ok &= DisplayManager.Restore(state.DisplayDeviceName, new DisplayModeInfo
            {
                Width = state.OriginalWidth,
                Height = state.OriginalHeight,
                RefreshRate = state.OriginalRefreshRate,
                BitsPerPel = state.OriginalBitsPerPel
            });
        }

        if (!string.IsNullOrEmpty(state.GpuInstanceId))
        {
            ok &= GpuManager.Enable(state.GpuInstanceId);
        }

        PowerSaveStateStore.Clear();

        if (ok)
        {
            Logger.Info("已在启动时恢复原始配置。");
            UpdateRuntimeLabel("已恢复原始配置。", Color.FromArgb(60, 140, 60));
        }
        else
        {
            Logger.Warn("启动时恢复原始配置未完全成功，请查看日志。");
            UpdateRuntimeLabel("恢复原始配置未完全成功，请查看日志。", Color.FromArgb(200, 40, 40));
        }
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        if (_hidingToTray)
        {
            return;
        }

        _hidingToTray = true;
        try
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(1500, "笔记本省电助手",
                "程序已最小化到托盘，正在后台运行。", ToolTipIcon.Info);
        }
        finally
        {
            _hidingToTray = false;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 非显式退出时，关闭窗口仅最小化到托盘。
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _powerMonitor.Dispose();
        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayRetryTimer?.Dispose();
            _trayRetryTimer = null;
            _trayIcon.Dispose();
            _trayIconImage.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // 电源事件处理
    // ------------------------------------------------------------------

    private void OnSwitchedToBattery()
    {
        UpdatePowerStatusLabel();
        Logger.Info("检测到切换为电池供电，触发省电策略确认流程。");

        var config = ConfigStore.Current;
        if (!config.HasGpuTarget && !config.HasDisplayTarget)
        {
            Logger.Warn("未配置任何省电目标，忽略本次触发。");
            return;
        }

        if (_powerSave.IsApplied)
        {
            Logger.Info("省电策略已处于生效状态，跳过。");
            return;
        }

        // 上次会话遗留且仍然生效（例如开机时就在电池上）：无需再次询问，
        // 否则会重复应用并覆盖掉已保存的原始分辨率记录。
        var leftover = PowerSaveStateStore.Load();
        if (leftover is { Applied: true } && IsStateStillApplied(leftover))
        {
            Logger.Info("系统已处于上次遗留的省电状态，跳过本次提示。");
            UpdateRuntimeLabel("当前已处于省电状态。", Color.FromArgb(60, 140, 60));
            return;
        }

        // 屏幕右下角弹出通知确认窗口（支持自动执行）。
        using var notify = new PowerSaveNotifyForm(
            "检测到已切换为电池供电，是否立即开启省电模式？",
            config.AutoExecute,
            config.AutoExecuteDelaySeconds);

        var result = notify.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            ApplyPowerSave(manual: false);
        }
        else
        {
            Logger.Info("用户忽略了省电提示。");
            UpdateRuntimeLabel("已忽略本次省电提示。", Color.FromArgb(180, 120, 30));
        }
    }

    private void OnSwitchedToAc()
    {
        UpdatePowerStatusLabel();

        if (_powerSave.IsApplied)
        {
            Logger.Info("检测到恢复交流电源，自动恢复原始配置。");
            RestorePowerSave(manual: false);
            return;
        }

        // 本次会话未应用过，但上次会话可能遗留了省电状态（例如开机时在电池、随后插电）。
        CheckLeftoverPowerSaveState();
    }

    // ------------------------------------------------------------------
    // 省电策略执行 / 恢复
    // ------------------------------------------------------------------

    private void ApplyPowerSave(bool manual)
    {
        var config = ConfigStore.Current;
        if (!config.HasGpuTarget && !config.HasDisplayTarget)
        {
            MessageBox.Show(this, "尚未配置省电策略，请先执行「重新配置」。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool ok;
        string message;
        try
        {
            ok = _powerSave.Apply(config, out message);
        }
        catch (Exception ex)
        {
            Logger.Error("执行省电策略异常。", ex);
            ok = false;
            message = ex.Message;
        }

        if (ok)
        {
            UpdateRuntimeLabel("省电模式已开启：" + message, Color.FromArgb(60, 140, 60));
            if (manual)
            {
                _trayIcon.ShowBalloonTip(1500, "省电模式已开启", message, ToolTipIcon.Info);
            }
        }
        else
        {
            UpdateRuntimeLabel("省电模式开启失败：" + message, Color.FromArgb(200, 40, 40));
            if (manual)
            {
                MessageBox.Show(this, "开启省电模式失败：" + message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        UpdateMenuState();
    }

    private void RestorePowerSave(bool manual)
    {
        bool ok;
        string message;
        try
        {
            ok = _powerSave.Rollback(out message);
        }
        catch (Exception ex)
        {
            Logger.Error("恢复原始配置异常。", ex);
            ok = false;
            message = ex.Message;
        }

        if (ok)
        {
            UpdateRuntimeLabel("已恢复原始硬件配置。", Color.FromArgb(60, 60, 60));
        }
        else
        {
            UpdateRuntimeLabel("恢复过程中出现问题：" + message, Color.FromArgb(200, 40, 40));
            if (manual)
            {
                MessageBox.Show(this, message, "恢复提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        UpdateMenuState();
    }

    private void Reconfigure()
    {
        using var wizard = new ConfigWizardForm(isFirstRun: false);
        var result = wizard.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            UpdateConfigSummary();
            UpdateMenuState();
        }
    }

    // ------------------------------------------------------------------
    // 状态刷新
    // ------------------------------------------------------------------

    private void UpdatePowerStatusLabel()
    {
        var onBattery = _powerMonitor.IsOnBattery;
        var text = _powerMonitor.IsOnAcPower ? "交流电源（已接入充电器）"
            : onBattery ? "电池供电（未接入充电器）"
            : "未知";
        _lblPowerStatus.Text = "电源状态：" + text;
        _lblPowerStatus.ForeColor = onBattery ? Color.FromArgb(200, 120, 30) : Color.FromArgb(60, 60, 60);
    }

    private void UpdateConfigSummary()
    {
        var config = ConfigStore.Current;
        var parts = new List<string>();
        if (config.HasGpuTarget)
        {
            parts.Add($"禁用显卡：{config.GpuName}");
        }

        if (config.HasDisplayTarget)
        {
            parts.Add($"显示档位：{config.Width}x{config.Height}@{config.RefreshRate}Hz");
        }

        _lblStrategyStatus.Text = parts.Count == 0
            ? "省电配置：未配置"
            : "省电配置：" + string.Join("；", parts)
              + (config.AutoExecute ? "（检测到电池后自动执行）" : "（检测到电池后弹窗确认）");
    }

    private void UpdateRuntimeLabel(string text, Color color)
    {
        _lblRuntimeStatus.Text = "运行状态：" + text;
        _lblRuntimeStatus.ForeColor = color;
    }

    private void UpdateMenuState()
    {
        var applied = _powerSave.IsApplied;
        _menuApply.Enabled = !applied;
        _menuRestore.Enabled = applied;
    }

    // ------------------------------------------------------------------
    // 托盘图标
    // ------------------------------------------------------------------

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var body = new SolidBrush(Color.FromArgb(38, 100, 180));
        g.FillEllipse(body, 1, 1, 30, 30);

        using var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // 简易闪电符号。
        g.DrawLines(pen, new[]
        {
            new PointF(18, 6),
            new PointF(11, 17),
            new PointF(16, 17),
            new PointF(13, 26)
        });

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            Interop.NativeMethods.DestroyIcon(hIcon);
        }
    }
}
