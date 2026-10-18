using System.Drawing.Drawing2D;

namespace PowerSaver.Forms;

/// <summary>
/// 省电通知结果。
/// </summary>
public enum NotifyResult
{
    /// <summary>用户点击立即开启（或自动执行）。</summary>
    Execute,

    /// <summary>用户忽略/关闭。</summary>
    Dismiss
}

/// <summary>
/// 屏幕右下角弹出的省电确认通知窗口。支持自动执行倒计时。
/// </summary>
public sealed class PowerSaveNotifyForm : Form
{
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly bool _autoExecute;
    private readonly int _delaySeconds;
    private readonly Label _countdownLabel;
    private int _remainingSeconds;

    /// <summary>用户的选择结果。</summary>
    public NotifyResult Result { get; private set; } = NotifyResult.Dismiss;

    public PowerSaveNotifyForm(string message, bool autoExecute, int delaySeconds)
    {
        _autoExecute = autoExecute;
        _delaySeconds = Math.Max(1, delaySeconds);
        _remainingSeconds = _delaySeconds;

        // 高 DPI：先启用自动缩放，再搭建界面，保证 200% 缩放下布局同步放大。
        SuspendLayout();
        DpiSupport.Enable(this);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(380, 150);
        BackColor = Color.FromArgb(250, 250, 250);
        Padding = new Padding(1);
        DoubleBuffered = true;

        var title = new Label
        {
            Text = "笔记本省电助手",
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 100, 180),
            AutoSize = true,
            Location = new Point(20, 16)
        };

        var body = new Label
        {
            Text = message,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(60, 60, 60),
            Location = new Point(20, 46),
            Size = new Size(340, 44)
        };

        _countdownLabel = new Label
        {
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = Color.FromArgb(120, 120, 120),
            Location = new Point(20, 92),
            AutoSize = true,
            Text = _autoExecute ? $"{_remainingSeconds} 秒后自动开启省电模式…" : string.Empty
        };

        var executeButton = new Button
        {
            Text = "立即开启",
            Size = new Size(96, 30),
            Location = new Point(164, 104),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 100, 180),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        executeButton.FlatAppearance.BorderSize = 0;
        executeButton.Click += (_, _) => Finish(NotifyResult.Execute);

        var dismissButton = new Button
        {
            Text = "忽略",
            Size = new Size(76, 30),
            Location = new Point(268, 104),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(225, 225, 225),
            ForeColor = Color.FromArgb(70, 70, 70),
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        dismissButton.FlatAppearance.BorderSize = 0;
        dismissButton.Click += (_, _) => Finish(NotifyResult.Dismiss);

        Controls.Add(title);
        Controls.Add(body);
        Controls.Add(_countdownLabel);
        Controls.Add(executeButton);
        Controls.Add(dismissButton);

        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += OnCountdownTick;

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 定位到主屏幕工作区右下角。
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);

        if (_autoExecute)
        {
            _countdownTimer.Start();
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            Finish(NotifyResult.Execute);
            return;
        }

        _countdownLabel.Text = $"{_remainingSeconds} 秒后自动开启省电模式…";
    }

    private void Finish(NotifyResult result)
    {
        _countdownTimer.Stop();
        Result = result;
        DialogResult = result == NotifyResult.Execute ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(210, 215, 225), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
