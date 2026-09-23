using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

public sealed class ChargeLimitOrchestrator : IChargeLimitOrchestrator
{
    public const int DefaultStartPercent = 75;
    public const int DefaultStopPercent = 80;

    private readonly IReadOnlyList<IChargeLimitBackend> _backends;
    private readonly IBatteryStatusService _batteryStatus;

    public ChargeLimitOrchestrator(
        IEnumerable<IChargeLimitBackend> backends,
        IBatteryStatusService batteryStatus)
    {
        _backends = backends.OrderBy(b => b.Priority).ToArray();
        _batteryStatus = batteryStatus;
    }

    public async Task<ChargeSessionState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var probes = new List<BackendProbe>();
        ChargeLimitStatus? active = null;
        string? error = null;

        foreach (var backend in _backends)
        {
            try
            {
                var available = await backend.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
                if (!available)
                {
                    probes.Add(new BackendProbe
                    {
                        Name = backend.Name,
                        Available = false,
                        Detail = DescribeMissing(backend.Name)
                    });
                    continue;
                }

                var status = await backend.ReadAsync(cancellationToken).ConfigureAwait(false);
                probes.Add(new BackendProbe
                {
                    Name = backend.Name,
                    Available = status.IsSupported,
                    Detail = status.Summary
                });

                if (status.IsSupported && active is null)
                {
                    active = status;
                }
                else if (!status.IsSupported && active is null)
                {
                    error ??= status.Detail;
                }
            }
            catch (Exception ex)
            {
                probes.Add(new BackendProbe
                {
                    Name = backend.Name,
                    Available = false,
                    Detail = ex.Message
                });
            }
        }

        var battery = await _batteryStatus.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!PlatformInfo.IsWindows && !battery.Available)
        {
            battery = new BatterySnapshot
            {
                Available = false,
                StatusText = "Battery telemetry requires Windows."
            };
        }

        if (active is null && error is null)
        {
            error = PlatformInfo.IsWindows
                ? "Neither Dell Command | Monitor (WMI) nor Dell Command | Configure (cctk) is installed. " +
                  "On Latitude 7455 ARM64, custom 75–80% BIOS thresholds are not supported via software APIs — " +
                  "see Next steps (BIOS Battery Configuration / Smart Charging)."
                : "Charge Limiter must run on Windows for real Dell backends. Use --demo to exercise the UI here.";
        }

        return new ChargeSessionState
        {
            Battery = battery,
            ActiveLimit = active,
            Probes = probes,
            Error = active is null ? error : null,
            PlatformNote = PlatformInfo.BuildPlatformNote(),
            NextSteps = PlatformInfo.BuildNextSteps(active is not null)
        };
    }

    public async Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        foreach (var backend in _backends)
        {
            if (!await backend.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var result = await backend.ApplyEightyPercentAsync(cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                return result;
            }

            failures.Add($"{backend.Name}: {result.Message}");
        }

        if (failures.Count == 0)
        {
            return OperationResult.Fail(
                "No Dell backend is installed.\n\n" + PlatformInfo.BuildNextSteps(writableBackend: false));
        }

        return OperationResult.Fail(
            "Could not apply an ~80% charge policy from software.\n\n" +
            string.Join("\n", failures) +
            "\n\n" + PlatformInfo.BuildNextSteps(writableBackend: false));
    }

    public async Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        foreach (var backend in _backends)
        {
            if (!await backend.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var result = await backend.RestoreFullChargeAsync(cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                return result;
            }

            failures.Add($"{backend.Name}: {result.Message}");
        }

        if (failures.Count == 0)
        {
            return OperationResult.Fail(
                "No Dell backend is installed. For a full charge, use BIOS Power → Battery Configuration → Standard or ExpressCharge when available.");
        }

        return OperationResult.Fail("Could not restore full charge.\n\n" + string.Join("\n", failures));
    }

    private static string DescribeMissing(string backendName) =>
        backendName.Contains("Monitor", StringComparison.OrdinalIgnoreCase)
            ? "Not installed. ARM64 package is available for Latitude 7455 (Dell Command | Monitor WINARM64)."
            : backendName.Contains("cctk", StringComparison.OrdinalIgnoreCase) ||
              backendName.Contains("Configure", StringComparison.OrdinalIgnoreCase)
                ? "Not installed. Even if installed, PrimaryBattChargeCfg is documented as incompatible with ARM64."
                : "Not detected on this PC.";
}
