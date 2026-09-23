using ChargeLimiter.Models;
using ChargeLimiter.Services;

namespace ChargeLimiter.Services;

/// <summary>
/// Design-time / non-Windows fallback so the UI can be exercised without Dell hardware.
/// Enabled with --demo or CHARGE_LIMITER_DEMO=1.
/// </summary>
public sealed class DemoChargeLimitBackend : IChargeLimitBackend
{
    private ChargeLimitMode _mode = ChargeLimitMode.Standard;
    private int _start = 50;
    private int _stop = 100;

    public string Name => "Demo (simulated Dell BIOS)";
    public int Priority => 1000;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<ChargeLimitStatus> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChargeLimitStatus
        {
            BackendName = Name,
            IsSupported = true,
            Mode = _mode,
            StartPercent = _mode == ChargeLimitMode.Custom ? _start : null,
            StopPercent = _mode == ChargeLimitMode.Custom ? _stop : null,
            Detail = "Demo backend — no firmware writes are performed.",
            Warning = "Demo mode is for UI verification only."
        });

    public Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default)
    {
        _mode = ChargeLimitMode.Custom;
        _start = ChargeLimitOrchestrator.DefaultStartPercent;
        _stop = ChargeLimitOrchestrator.DefaultStopPercent;
        return Task.FromResult(OperationResult.Ok(
            $"Demo: custom window set to {_start}%–{_stop}%.",
            ReadAsync(CancellationToken.None).Result));
    }

    public Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default)
    {
        _mode = ChargeLimitMode.Standard;
        return Task.FromResult(OperationResult.Ok(
            "Demo: restored Standard (full charge).",
            ReadAsync(CancellationToken.None).Result));
    }
}

public sealed class DemoBatteryStatusService : IBatteryStatusService
{
    public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BatterySnapshot
        {
            Available = true,
            Percent = 67,
            Charging = true,
            PluggedIn = true,
            StatusText = "Charging · 67% (demo)"
        });
}
