using Microsoft.Win32;
using static PowerSaver.Interop.NativeMethods;

namespace PowerSaver.Services;

/// <summary>
/// 电源状态监听。基于系统电源事件（Microsoft.Win32.SystemEvents.PowerModeChanged）
/// 与 WM_POWERBROADCAST 机制实现，事件驱动、不进行轮询，避免额外功耗。
/// </summary>
public sealed class PowerStatusMonitor : IDisposable
{
    private readonly object _sync = new();
    private byte _lastAcLineStatus = AC_LINE_UNKNOWN;
    private bool _started;

    /// <summary>从交流电源切换为电池供电时触发。</summary>
    public event EventHandler? SwitchedToBattery;

    /// <summary>从电池供电切换为交流电源时触发。</summary>
    public event EventHandler? SwitchedToAc;

    /// <summary>电源状态变化（参数为 true 表示已接入交流电源）。</summary>
    public event EventHandler<bool>? PowerStateChanged;

    /// <summary>当前是否使用电池供电。</summary>
    public bool IsOnBattery => GetAcLineStatus() == AC_LINE_OFFLINE;

    /// <summary>当前是否接入交流电源。</summary>
    public bool IsOnAcPower => GetAcLineStatus() == AC_LINE_ONLINE;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _lastAcLineStatus = GetAcLineStatus();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _started = true;
            Logger.Info($"电源监听已启动，初始状态：{DescribeAcLine(_lastAcLineStatus)}");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _started = false;
            Logger.Info("电源监听已停止。");
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            switch (e.Mode)
            {
                case PowerModes.StatusChange:
                case PowerModes.Resume:
                    Evaluate();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("处理电源事件时发生异常。", ex);
        }
    }

    /// <summary>比较当前与上次的电源状态并触发相应事件。</summary>
    private void Evaluate()
    {
        byte current;
        byte previous;

        lock (_sync)
        {
            previous = _lastAcLineStatus;
            current = GetAcLineStatus();
            if (current == AC_LINE_UNKNOWN)
            {
                return;
            }

            _lastAcLineStatus = current;
        }

        if (previous == current)
        {
            return;
        }

        Logger.Info($"电源状态变化：{DescribeAcLine(previous)} -> {DescribeAcLine(current)}");

        if (previous == AC_LINE_ONLINE && current == AC_LINE_OFFLINE)
        {
            SwitchedToBattery?.Invoke(this, EventArgs.Empty);
        }
        else if (previous == AC_LINE_OFFLINE && current == AC_LINE_ONLINE)
        {
            SwitchedToAc?.Invoke(this, EventArgs.Empty);
        }

        PowerStateChanged?.Invoke(this, current == AC_LINE_ONLINE);
    }

    private static byte GetAcLineStatus()
        => GetSystemPowerStatus(out var status) ? status.ACLineStatus : AC_LINE_UNKNOWN;

    private static string DescribeAcLine(byte value) => value switch
    {
        AC_LINE_OFFLINE => "电池供电",
        AC_LINE_ONLINE => "交流电源",
        _ => "未知"
    };

    public void Dispose() => Stop();
}
