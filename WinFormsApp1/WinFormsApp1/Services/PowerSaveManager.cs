using PowerSaver.Models;

namespace PowerSaver.Services;

/// <summary>
/// 省电策略执行器：禁用目标 GPU + 将目标显示器切换到低功耗档位。
/// 所有硬件操作均带异常捕获与回滚，任何一步失败都会撤销已执行的步骤，避免系统显示异常。
/// </summary>
public sealed class PowerSaveManager
{
    private readonly object _sync = new();
    private AppliedState? _applied;

    /// <summary>当前是否已处于省电状态。</summary>
    public bool IsApplied
    {
        get { lock (_sync) { return _applied is not null; } }
    }

    /// <summary>本次应用中是否实际修改了显示器分辨率/刷新率。</summary>
    public bool DisplayChangeApplied
    {
        get { lock (_sync) { return _applied?.DisplayDeviceName is not null; } }
    }

    /// <summary>
    /// 执行省电策略。
    /// </summary>
    /// <param name="rollbackOnFailure">任一步骤失败时是否回滚已执行的步骤。</param>
    public bool Apply(AppConfig config, out string message, bool rollbackOnFailure = true)
    {
        lock (_sync)
        {
            if (_applied is not null)
            {
                message = "已处于省电状态，无需重复执行。";
                return true;
            }

            var state = new AppliedState();

            try
            {
                // 步骤 1：调整显示参数（相对安全，先执行）。
                if (config.HasDisplayTarget)
                {
                    if (!DisplayManager.TryGetCurrentMode(config.DisplayDeviceName!, out var current))
                    {
                        message = $"无法读取显示器当前模式：{config.DisplayDeviceName}";
                        Logger.Error(message);
                        return false;
                    }

                    // 已经是目标模式则无需切换。
                    var target = new DisplayModeInfo
                    {
                        Width = config.Width,
                        Height = config.Height,
                        RefreshRate = config.RefreshRate,
                        BitsPerPel = config.BitsPerPel
                    };

                    if (!current.Equals(target))
                    {
                        if (!DisplayManager.ApplyMode(config.DisplayDeviceName!, target, out var previous))
                        {
                            message = "切换显示器分辨率/刷新率失败，可能目标模式不被驱动支持。";
                            Logger.Error(message);
                            if (rollbackOnFailure)
                            {
                                RollbackInternal(state);
                            }
                            return false;
                        }

                        state.DisplayDeviceName = config.DisplayDeviceName;
                        state.PreviousMode = previous;
                    }
                }

                // 步骤 2：禁用目标 GPU。
                if (config.HasGpuTarget)
                {
                    if (!GpuManager.Disable(config.GpuInstanceId!))
                    {
                        message = "禁用目标 GPU 失败。";
                        Logger.Error(message);
                        if (rollbackOnFailure)
                        {
                            RollbackInternal(state);
                        }
                        return false;
                    }

                    state.DisabledGpuInstanceId = config.GpuInstanceId;
                }

                _applied = state;
                message = BuildAppliedMessage(config);
                Logger.Info($"省电策略已生效：{message}");

                // 落盘记录，供重启后判断"当前是否为省电状态"并恢复原始配置。
                PowerSaveStateStore.Save(new PowerSaveState
                {
                    Applied = true,
                    GpuInstanceId = state.DisabledGpuInstanceId,
                    DisplayDeviceName = state.DisplayDeviceName,
                    AppliedWidth = config.Width,
                    AppliedHeight = config.Height,
                    AppliedRefreshRate = config.RefreshRate,
                    AppliedBitsPerPel = config.BitsPerPel,
                    OriginalWidth = state.PreviousMode?.Width ?? 0,
                    OriginalHeight = state.PreviousMode?.Height ?? 0,
                    OriginalRefreshRate = state.PreviousMode?.RefreshRate ?? 0,
                    OriginalBitsPerPel = state.PreviousMode?.BitsPerPel ?? 0,
                    AppliedAt = DateTime.Now
                });

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("执行省电策略时发生异常。", ex);
                if (rollbackOnFailure)
                {
                    RollbackInternal(state);
                }

                message = $"执行省电策略时发生异常：{ex.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// 回滚省电策略：重新启用 GPU、恢复显示器原显示参数。
    /// </summary>
    public bool Rollback(out string message)
    {
        lock (_sync)
        {
            if (_applied is null)
            {
                message = "当前未处于省电状态，无需恢复。";
                return true;
            }

            var errors = RollbackInternal(_applied);
            _applied = null;

            if (errors.Count == 0)
            {
                // 已完全恢复，清除持久化记录，避免下次启动重复提示。
                PowerSaveStateStore.Clear();
                message = "已恢复原始硬件配置。";
                Logger.Info(message);
                return true;
            }

            message = "恢复过程中出现以下问题：" + Environment.NewLine + string.Join(Environment.NewLine, errors);
            Logger.Warn(message);
            return false;
        }
    }

    /// <summary>
    /// 回滚指定状态，返回发生的错误列表。始终尽力回滚所有步骤。
    /// </summary>
    private static List<string> RollbackInternal(AppliedState state)
    {
        var errors = new List<string>();

        // 先恢复显示，保证有可用画面。
        if (state.DisplayDeviceName is not null && state.PreviousMode is not null)
        {
            try
            {
                if (!DisplayManager.Restore(state.DisplayDeviceName, state.PreviousMode))
                {
                    errors.Add($"恢复显示器 {state.DisplayDeviceName} 的显示参数失败。");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("回滚显示参数时异常。", ex);
                errors.Add($"恢复显示器显示参数异常：{ex.Message}");
            }
            finally
            {
                state.DisplayDeviceName = null;
                state.PreviousMode = null;
            }
        }

        // 再恢复 GPU。
        if (state.DisabledGpuInstanceId is not null)
        {
            try
            {
                if (!GpuManager.Enable(state.DisabledGpuInstanceId))
                {
                    errors.Add($"重新启用 GPU 设备 {state.DisabledGpuInstanceId} 失败。");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("回滚 GPU 状态时异常。", ex);
                errors.Add($"重新启用 GPU 异常：{ex.Message}");
            }
            finally
            {
                state.DisabledGpuInstanceId = null;
            }
        }

        return errors;
    }

    private static string BuildAppliedMessage(AppConfig config)
    {
        var parts = new List<string>();
        if (config.HasGpuTarget)
        {
            parts.Add($"已禁用 GPU：{config.GpuName}");
        }

        if (config.HasDisplayTarget)
        {
            parts.Add($"显示器已切换至 {config.Width}x{config.Height} @ {config.RefreshRate}Hz");
        }

        return parts.Count == 0 ? "未配置任何省电动作。" : string.Join("；", parts);
    }

    /// <summary>记录已应用的硬件变更，供回滚使用。</summary>
    private sealed class AppliedState
    {
        public string? DisabledGpuInstanceId { get; set; }
        public string? DisplayDeviceName { get; set; }
        public DisplayModeInfo? PreviousMode { get; set; }
    }
}
