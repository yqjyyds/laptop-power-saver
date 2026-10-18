using PowerSaver.Models;
using PowerSaver.Services;

namespace PowerSaver.Forms;

/// <summary>
/// 首次运行配置向导。列出 GPU 与显示器供选择，校验「直连显示器的 GPU 不可禁用」，
/// 保存前必须通过「执行测试」（15 秒倒计时确认）流程。
/// </summary>
public sealed class ConfigWizardForm : Form
{
    private const int TestConfirmSeconds = 15;

    private readonly bool _isFirstRun;
    private readonly List<GpuDevice> _gpus = new();
    private readonly List<DisplayDevice> _displays = new();
    private PowerSaveManager? _testManager;

    private bool _testPassed;
    private bool _configSaved;

    private ComboBox _cboGpu = null!;
    private CheckBox _chkGpu = null!;
    private Label _lblGpuStatus = null!;

    private ComboBox _cboDisplay = null!;
    private ComboBox _cboMode = null!;
    private CheckBox _chkDisplay = null!;
    private Label _lblDisplayStatus = null!;

    private CheckBox _chkAutoExecute = null!;
    private CheckBox _chkAutoStart = null!;
    private Label _lblStatus = null!;
    private Button _btnTest = null!;

    public ConfigWizardForm(bool isFirstRun)
    {
        _isFirstRun = isFirstRun;

        // 高 DPI：先启用自动缩放，再搭建界面，保证 200% 缩放下布局同步放大。
        SuspendLayout();
        DpiSupport.Enable(this);
        BuildUi();
        ResumeLayout(false);
        PerformLayout();

        Load += (_, _) => InitializeData();
    }

    // ------------------------------------------------------------------
    // 界面构建
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        Text = _isFirstRun ? "省电配置向导（首次运行）" : "重新配置省电策略";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 620);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;

        // 策略一：GPU
        var grpGpu = new GroupBox
        {
            Text = "省电策略一：禁用独立显卡",
            Location = new Point(16, 14),
            Size = new Size(628, 180)
        };

        _chkGpu = new CheckBox
        {
            Text = "启用「禁用独立显卡」策略",
            Location = new Point(16, 24),
            AutoSize = true,
            Checked = true
        };
        _chkGpu.CheckedChanged += (_, _) =>
        {
            UpdateGpuControlsEnabled();
            ResetTestState();
            ValidateAndShow();
        };

        var lblGpu = new Label
        {
            Text = "目标显卡：",
            Location = new Point(20, 62),
            AutoSize = true
        };

        _cboGpu = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 58),
            Size = new Size(508, 26)
        };
        _cboGpu.SelectedIndexChanged += (_, _) =>
        {
            ResetTestState();
            UpdateGpuHint();
            ValidateAndShow();
        };

        _lblGpuStatus = new Label
        {
            Location = new Point(20, 92),
            Size = new Size(588, 76),
            ForeColor = Color.FromArgb(110, 110, 110)
        };

        grpGpu.Controls.Add(_chkGpu);
        grpGpu.Controls.Add(lblGpu);
        grpGpu.Controls.Add(_cboGpu);
        grpGpu.Controls.Add(_lblGpuStatus);

        // 策略二：显示规格
        var grpDisplay = new GroupBox
        {
            Text = "省电策略二：调整显示规格",
            Location = new Point(16, 202),
            Size = new Size(628, 190)
        };

        _chkDisplay = new CheckBox
        {
            Text = "启用「调整显示器分辨率/刷新率」策略",
            Location = new Point(16, 24),
            AutoSize = true,
            Checked = true
        };
        _chkDisplay.CheckedChanged += (_, _) =>
        {
            UpdateDisplayControlsEnabled();
            ResetTestState();
            ValidateAndShow();
        };

        var lblDisplay = new Label
        {
            Text = "目标显示器：",
            Location = new Point(20, 62),
            AutoSize = true
        };

        _cboDisplay = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 58),
            Size = new Size(496, 26)
        };
        _cboDisplay.SelectedIndexChanged += (_, _) =>
        {
            ReloadModes();
            ResetTestState();
            ValidateAndShow();
        };

        var lblMode = new Label
        {
            Text = "低功耗档位：",
            Location = new Point(20, 100),
            AutoSize = true
        };

        _cboMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 96),
            Size = new Size(496, 26)
        };
        _cboMode.SelectedIndexChanged += (_, _) =>
        {
            ResetTestState();
            ValidateAndShow();
        };

        _lblDisplayStatus = new Label
        {
            Location = new Point(20, 130),
            Size = new Size(588, 50),
            ForeColor = Color.FromArgb(110, 110, 110)
        };

        grpDisplay.Controls.Add(_chkDisplay);
        grpDisplay.Controls.Add(lblDisplay);
        grpDisplay.Controls.Add(_cboDisplay);
        grpDisplay.Controls.Add(lblMode);
        grpDisplay.Controls.Add(_cboMode);
        grpDisplay.Controls.Add(_lblDisplayStatus);

        // 运行选项
        var grpOptions = new GroupBox
        {
            Text = "运行选项",
            Location = new Point(16, 400),
            Size = new Size(628, 96)
        };

        _chkAutoExecute = new CheckBox
        {
            Text = "检测到切换为电池供电后，弹出通知并自动执行省电策略（无需人工点击）",
            Location = new Point(16, 26),
            Size = new Size(600, 24)
        };
        _chkAutoStart = new CheckBox
        {
            Text = "开机自动启动，随 Windows 登录在后台静默运行",
            Location = new Point(16, 56),
            Size = new Size(600, 24),
            Checked = true
        };
        grpOptions.Controls.Add(_chkAutoExecute);
        grpOptions.Controls.Add(_chkAutoStart);

        _lblStatus = new Label
        {
            Location = new Point(16, 504),
            Size = new Size(628, 44),
            ForeColor = Color.FromArgb(110, 110, 110)
        };

        _btnTest = new Button
        {
            Text = "执行测试",
            Location = new Point(400, 558),
            Size = new Size(130, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 100, 180),
            ForeColor = Color.White
        };
        _btnTest.FlatAppearance.BorderSize = 0;
        _btnTest.Click += (_, _) => RunTest();

        var btnExit = new Button
        {
            Text = "退出",
            Location = new Point(540, 558),
            Size = new Size(104, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(228, 228, 228),
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        btnExit.FlatAppearance.BorderSize = 0;
        btnExit.Click += (_, _) => Close();

        Controls.Add(grpGpu);
        Controls.Add(grpDisplay);
        Controls.Add(grpOptions);
        Controls.Add(_lblStatus);
        Controls.Add(_btnTest);
        Controls.Add(btnExit);
    }

    // ------------------------------------------------------------------
    // 数据初始化
    // ------------------------------------------------------------------

    private void InitializeData()
    {
        try
        {
            _gpus.AddRange(HardwareEnumerator.GetGpus());
            _displays.AddRange(HardwareEnumerator.GetDisplays());
        }
        catch (Exception ex)
        {
            Logger.Error("枚举硬件失败。", ex);
            MessageBox.Show(this, $"枚举硬件设备失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // 填充 GPU
        _cboGpu.Items.Clear();
        foreach (var gpu in _gpus)
        {
            _cboGpu.Items.Add(new GpuItem(gpu));
        }

        // 填充显示器
        _cboDisplay.Items.Clear();
        foreach (var display in _displays)
        {
            _cboDisplay.Items.Add(new DisplayItem(display));
        }

        // 回填已有配置（重新配置场景）
        var existing = ConfigStore.Current;
        if (!_isFirstRun && existing.Configured)
        {
            RestoreFromExisting(existing);
        }
        else if (_cboGpu.Items.Count > 0)
        {
            // 首选默认：选择第一个未直连显示器的 GPU。
            var idx = _gpus.FindIndex(g => !g.IsConnectedToDisplay);
            _cboGpu.SelectedIndex = idx >= 0 ? idx : 0;
        }

        if (_cboDisplay.Items.Count > 0 && _cboDisplay.SelectedIndex < 0)
        {
            _cboDisplay.SelectedIndex = 0;
        }

        UpdateGpuControlsEnabled();
        UpdateDisplayControlsEnabled();
        UpdateGpuHint();
        ValidateAndShow();
    }

    private void RestoreFromExisting(AppConfig existing)
    {
        _chkGpu.Checked = existing.EnableGpuDisable;
        _chkDisplay.Checked = existing.EnableDisplayChange;
        _chkAutoExecute.Checked = existing.AutoExecute;
        _chkAutoStart.Checked = existing.AutoStart;

        if (!string.IsNullOrWhiteSpace(existing.GpuInstanceId))
        {
            var idx = _gpus.FindIndex(g =>
                string.Equals(g.InstanceId, existing.GpuInstanceId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _cboGpu.SelectedIndex = idx;
            }
        }

        if (!string.IsNullOrWhiteSpace(existing.DisplayDeviceName))
        {
            var idx = _displays.FindIndex(d =>
                string.Equals(d.DeviceName, existing.DisplayDeviceName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _cboDisplay.SelectedIndex = idx;
            }
        }
    }

    private void ReloadModes()
    {
        _cboMode.Items.Clear();
        if (_cboDisplay.SelectedItem is not DisplayItem item)
        {
            return;
        }

        var modes = HardwareEnumerator.GetSupportedModes(item.Display.DeviceName).ToList();

        // 优先推荐低功耗档位：像素总数最小，其次刷新率最低。
        var recommended = modes
            .OrderBy(m => (long)m.Width * m.Height)
            .ThenBy(m => m.RefreshRate)
            .FirstOrDefault();

        foreach (var mode in modes)
        {
            _cboMode.Items.Add(mode);
        }

        var existing = ConfigStore.Current;
        var targetIndex = -1;
        if (!_isFirstRun && string.Equals(existing.DisplayDeviceName, item.Display.DeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            targetIndex = modes.FindIndex(m => m.Width == existing.Width
                                               && m.Height == existing.Height
                                               && m.RefreshRate == existing.RefreshRate);
        }

        if (targetIndex < 0 && recommended is not null)
        {
            targetIndex = modes.IndexOf(recommended);
        }

        if (targetIndex >= 0)
        {
            _cboMode.SelectedIndex = targetIndex;
        }
    }

    // ------------------------------------------------------------------
    // 校验
    // ------------------------------------------------------------------

    private void UpdateGpuControlsEnabled()
    {
        _cboGpu.Enabled = _chkGpu.Checked;
        _lblGpuStatus.ForeColor = Color.FromArgb(110, 110, 110);
    }

    private void UpdateDisplayControlsEnabled()
    {
        _cboDisplay.Enabled = _chkDisplay.Checked;
        _cboMode.Enabled = _chkDisplay.Checked;
    }

    private void UpdateGpuHint()
    {
        if (_cboGpu.SelectedItem is not GpuItem item)
        {
            _lblGpuStatus.Text = _gpus.Count == 0 ? "未检测到任何显卡设备。" : string.Empty;
            return;
        }

        var gpu = item.Gpu;
        if (gpu.IsConnectedToDisplay)
        {
            _lblGpuStatus.ForeColor = Color.FromArgb(200, 40, 40);
            _lblGpuStatus.Text = "✗ 该显卡正在驱动当前显示器，禁用它会导致画面丢失，禁止选择。请选择其它独立显卡。";
        }
        else if (!gpu.IsEnabled)
        {
            _lblGpuStatus.ForeColor = Color.FromArgb(180, 120, 30);
            _lblGpuStatus.Text = "提示：该显卡当前已处于禁用状态。";
        }
        else
        {
            _lblGpuStatus.ForeColor = Color.FromArgb(90, 150, 90);
            _lblGpuStatus.Text = "✓ 该显卡未直连显示器，可安全禁用。";
        }
    }

    /// <summary>校验当前选择是否合法，并刷新状态提示与「执行测试」按钮可用性。</summary>
    private bool ValidateAndShow()
    {
        var error = ValidateConfiguration(out var info);

        if (error is not null)
        {
            _lblStatus.ForeColor = Color.FromArgb(200, 40, 40);
            _lblStatus.Text = "✗ " + error;
            _btnTest.Enabled = false;
            return false;
        }

        _lblStatus.ForeColor = Color.FromArgb(60, 60, 60);
        _lblStatus.Text = info;
        _btnTest.Enabled = true;
        return true;
    }

    private string? ValidateConfiguration(out string info)
    {
        info = string.Empty;

        if (!_chkGpu.Checked && !_chkDisplay.Checked)
        {
            return "请至少启用一项省电策略。";
        }

        if (_chkGpu.Checked)
        {
            if (_cboGpu.SelectedItem is not GpuItem gpuItem)
            {
                return "请选择要禁用的目标显卡。";
            }

            if (gpuItem.Gpu.IsConnectedToDisplay)
            {
                return "所选显卡正在驱动显示器，禁用它会导致显示异常，请改选其它独立显卡。";
            }
        }

        if (_chkDisplay.Checked)
        {
            if (_cboDisplay.SelectedItem is not DisplayItem)
            {
                return "请选择要调整的显示器。";
            }

            if (_cboMode.SelectedItem is not DisplayModeInfo)
            {
                return "请选择低功耗显示档位。";
            }
        }

        var parts = new List<string>();
        if (_chkGpu.Checked && _cboGpu.SelectedItem is GpuItem g)
        {
            parts.Add($"禁用显卡：{g.Gpu.Name}");
        }

        if (_chkDisplay.Checked && _cboMode.SelectedItem is DisplayModeInfo m)
        {
            parts.Add($"显示档位：{m.DisplayText}");
        }

        if (parts.Count > 0)
        {
            info = "当前配置：" + string.Join("；", parts)
                   + (_testPassed ? "（测试已通过，即将保存）" : "（需先执行测试）");
        }

        return null;
    }

    private void ResetTestState()
    {
        _testPassed = false;
    }

    private AppConfig BuildConfig()
    {
        var gpuItem = _chkGpu.Checked ? _cboGpu.SelectedItem as GpuItem : null;
        var displayItem = _chkDisplay.Checked ? _cboDisplay.SelectedItem as DisplayItem : null;
        var mode = _chkDisplay.Checked ? _cboMode.SelectedItem as DisplayModeInfo : null;

        return new AppConfig
        {
            Configured = true,
            EnableGpuDisable = _chkGpu.Checked && gpuItem is not null,
            GpuInstanceId = gpuItem?.Gpu.InstanceId,
            GpuName = gpuItem?.Gpu.Name,
            EnableDisplayChange = _chkDisplay.Checked && displayItem is not null && mode is not null,
            DisplayDeviceName = displayItem?.Display.DeviceName,
            DisplayName = displayItem?.Display.FriendlyName,
            Width = mode?.Width ?? 0,
            Height = mode?.Height ?? 0,
            RefreshRate = mode?.RefreshRate ?? 0,
            BitsPerPel = mode?.BitsPerPel ?? 32,
            AutoExecute = _chkAutoExecute.Checked,
            AutoStart = _chkAutoStart.Checked
        };
    }

    // ------------------------------------------------------------------
    // 执行测试
    // ------------------------------------------------------------------

    private void RunTest()
    {
        if (!ValidateAndShow())
        {
            return;
        }

        var config = BuildConfig();

        // 释放上一轮可能残留的状态。
        _testManager = new PowerSaveManager();

        try
        {
            if (!_testManager.Apply(config, out var message))
            {
                MessageBox.Show(this, $"测试失败：{message}", "测试失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _testManager = null;
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("执行测试时异常。", ex);
            MessageBox.Show(this, $"测试过程中发生异常：{ex.Message}", "测试失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _testManager = null;
            return;
        }

        // 弹出倒计时确认框。
        using var dialog = new TestCountdownForm(TestConfirmSeconds);
        var result = dialog.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            // 测试通过：保存配置。
            _testPassed = true;
            if (!SaveConfiguration(config))
            {
                return;
            }

            // 保存成功后，询问是否将测试期间修改的分辨率恢复到修改前状态。
            PromptRestoreResolution();

            _configSaved = true;

            MessageBox.Show(this,
                "配置已生效并保存。" + Environment.NewLine +
                "程序将随 Windows 登录自动在后台运行，并在切换到电池供电时自动执行省电策略。",
                "配置完成", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            // 超时/未确认：立即自动回滚已应用的修改，配置不生效。
            var rollbackMessage = string.Empty;
            var rollbackOk = true;
            if (_testManager is not null)
            {
                rollbackOk = _testManager.Rollback(out rollbackMessage);
                _testManager = null;
            }

            ResetTestState();
            ValidateAndShow();
            Logger.Warn($"测试未通过（{(dialog.TimedOut ? "超时" : "未确认")}），已自动撤销修改。{rollbackMessage}");

            // 回滚完成后再提示用户。
            var reason = dialog.TimedOut
                ? "测试超时，未在规定时间内点击「确认」"
                : "测试未确认";
            var text = $"{reason}，本次配置未生效。\n\n已自动撤销对系统的修改："
                       + (rollbackOk ? "硬件配置已恢复原始状态。" : rollbackMessage);
            MessageBox.Show(this, text, "测试失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>保存配置并同步自启动设置。成功返回 true。</summary>
    private bool SaveConfiguration(AppConfig config)
    {
        try
        {
            ConfigStore.Save(config);

            // 同步自启动设置。
            if (config.AutoStart)
            {
                AutoStartManager.Enable();
            }
            else
            {
                AutoStartManager.Disable();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置失败。", ex);
            MessageBox.Show(this, $"保存配置失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// 测试通过并保存后，询问用户是否将测试期间修改的显示分辨率恢复到修改前状态。
    /// 选择「是」会撤销本次测试期间应用的全部改动（分辨率与禁用的显卡），
    /// 使系统回到测试前的状态，省电策略留待切换到电池供电时自动应用。
    /// </summary>
    private void PromptRestoreResolution()
    {
        if (_testManager is null || !_testManager.DisplayChangeApplied)
        {
            return;
        }

        var choice = MessageBox.Show(this,
            "测试已通过，配置已保存。" + Environment.NewLine + Environment.NewLine +
            "是否将显示器分辨率恢复为修改前的状态？" + Environment.NewLine + Environment.NewLine +
            "选择「是」：立即恢复原分辨率（并重新启用测试期间禁用的显卡），" +
            "省电策略将在切换到电池供电时自动应用。" + Environment.NewLine +
            "选择「否」：保持当前测试所应用的低功耗状态。",
            "恢复分辨率", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (choice == DialogResult.Yes)
        {
            var restored = _testManager.Rollback(out var message);
            Logger.Info($"用户选择恢复分辨率：{message}");
            if (!restored)
            {
                MessageBox.Show(this, message, "恢复提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            Logger.Info("用户选择保持当前低功耗显示状态。");
        }

        _testManager = null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 首次运行强制配置，禁止跳过：未保存配置时关闭需二次确认（退出程序）。
        if (_isFirstRun && !_configSaved && e.CloseReason == CloseReason.UserClosing)
        {
            var choice = MessageBox.Show(this,
                "尚未完成配置。配置完成前程序无法进入省电管理，确定要退出吗？",
                "未完成配置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        // 未保存的测试修改需要撤销，避免影响系统。
        if (!_configSaved && _testManager is not null)
        {
            _testManager.Rollback(out _);
            _testManager = null;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _testManager = null;
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // 下拉项包装
    // ------------------------------------------------------------------

    private sealed class GpuItem
    {
        public GpuItem(GpuDevice gpu) => Gpu = gpu;

        public GpuDevice Gpu { get; }

        public override string ToString()
        {
            var suffix = Gpu.IsConnectedToDisplay
                ? "  ——（正在驱动显示器，不可禁用）"
                : !Gpu.IsEnabled
                    ? "  ——（当前已禁用）"
                    : string.Empty;
            return Gpu.Name + suffix;
        }
    }

    private sealed class DisplayItem
    {
        public DisplayItem(DisplayDevice display) => Display = display;

        public DisplayDevice Display { get; }

        public override string ToString()
        {
            var primary = Display.IsPrimary ? "  [主显示器]" : string.Empty;
            return $"{Display.FriendlyName}  ({Display.DeviceName})  " +
                   $"当前 {Display.CurrentWidth}x{Display.CurrentHeight}@{Display.CurrentRefreshRate}Hz{primary}";
        }
    }
}
