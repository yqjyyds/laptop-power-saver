namespace PowerSaver.Forms;

/// <summary>
/// 启动时的「是否恢复原始配置」确认窗口。
/// 倒计时结束仍未选择时，自动恢复原始配置（返回 <see cref="DialogResult.OK"/>）。
/// </summary>
public sealed class RestorePromptForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _countdownLabel;
    private int _remaining;

    /// <summary>是否因倒计时而自动恢复。</summary>
    public bool AutoRestored { get; private set; }

    public RestorePromptForm(int seconds)
    {
        _remaining = Math.Max(1, seconds);

        SuspendLayout();
        DpiSupport.Enable(this);

        Text = "恢复原始配置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(470, 232);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "检测到系统仍处于省电状态",
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 100, 180),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 20),
            Size = new Size(430, 28)
        };

        var body = new Label
        {
            Text = "上次在电池供电时应用了省电配置（低分辨率 / 禁用显卡），" +
                   "关机后未恢复。" + Environment.NewLine +
                   "是否恢复原始配置？",
            ForeColor = Color.FromArgb(60, 60, 60),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 56),
            Size = new Size(430, 52)
        };

        _countdownLabel = new Label
        {
            ForeColor = Color.FromArgb(200, 120, 30),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 116),
            Size = new Size(430, 22)
        };

        var restoreButton = new Button
        {
            Text = "立即恢复",
            Size = new Size(120, 34),
            Location = new Point(112, 160),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 100, 180),
            ForeColor = Color.White
        };
        restoreButton.FlatAppearance.BorderSize = 0;
        restoreButton.Click += (_, _) => Finish(DialogResult.OK);

        var keepButton = new Button
        {
            Text = "保持现状",
            Size = new Size(120, 34),
            Location = new Point(244, 160),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(228, 228, 228),
            ForeColor = Color.FromArgb(70, 70, 70)
        };
        keepButton.FlatAppearance.BorderSize = 0;
        keepButton.Click += (_, _) => Finish(DialogResult.Cancel);

        Controls.Add(title);
        Controls.Add(body);
        Controls.Add(_countdownLabel);
        Controls.Add(restoreButton);
        Controls.Add(keepButton);
        AcceptButton = restoreButton;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateCountdownText();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            AutoRestored = true;
            Finish(DialogResult.OK);
            return;
        }

        UpdateCountdownText();
    }

    private void UpdateCountdownText()
        => _countdownLabel.Text = $"{_remaining} 秒后自动恢复原始配置…";

    private void Finish(DialogResult result)
    {
        _timer.Stop();
        DialogResult = result;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
