using System.Drawing.Drawing2D;

namespace PowerSaver.Forms;

/// <summary>
/// 「执行测试」阶段的确认对话框：15 秒倒计时结束前需点击「确认」，
/// 否则视为测试失败返回 <see cref="DialogResult.Cancel"/>。
/// </summary>
public sealed class TestCountdownForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _totalSeconds;
    private int _remaining;
    private readonly Label _countdownLabel;

    /// <summary>是否因倒计时结束而超时（未点击「确认」）。</summary>
    public bool TimedOut { get; private set; }

    public TestCountdownForm(int seconds)
    {
        _totalSeconds = Math.Max(1, seconds);
        _remaining = _totalSeconds;

        // 高 DPI：先启用自动缩放，再搭建界面，保证 200% 缩放下布局同步放大。
        SuspendLayout();
        DpiSupport.Enable(this);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;

        Text = "省电配置测试";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(420, 220);
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "正在应用省电配置，请确认显示与系统是否正常",
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 100, 180),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 24),
            Size = new Size(380, 30)
        };

        _countdownLabel = new Label
        {
            Text = _remaining.ToString(),
            Font = new Font("Segoe UI", 40F, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 90, 60),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 62),
            Size = new Size(380, 70)
        };

        var hint = new Label
        {
            Text = "若在倒计时结束前未点击「确认」，将自动撤销本次修改。",
            ForeColor = Color.FromArgb(110, 110, 110),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 136),
            Size = new Size(380, 20)
        };

        var confirmButton = new Button
        {
            Text = "确认",
            Size = new Size(110, 34),
            Location = new Point(160, 168),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 100, 180),
            ForeColor = Color.White
        };
        confirmButton.FlatAppearance.BorderSize = 0;
        confirmButton.Click += (_, _) =>
        {
            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(title);
        Controls.Add(_countdownLabel);
        Controls.Add(hint);
        Controls.Add(confirmButton);
        AcceptButton = confirmButton;

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            // 超时：立即关闭对话框，由调用方自动回滚已应用的修改。
            // 此处不做任何阻塞式提示，避免延迟回滚。
            _timer.Stop();
            _countdownLabel.Text = "0";
            TimedOut = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        _countdownLabel.Text = _remaining.ToString();
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
