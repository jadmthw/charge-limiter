using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

public interface IChargeLimitBackend
{
    string Name { get; }
    int Priority { get; }
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<ChargeLimitStatus> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort ~80% stop: prefer Custom start/stop, else Primarily AC, else Adaptive.
    /// </summary>
    Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default);
}

public interface IBatteryStatusService
{
    Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IChargeLimitOrchestrator
{
    Task<ChargeSessionState> RefreshAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default);
}

public sealed class ChargeSessionState
{
    public bool IsLoading { get; init; }
    public BatterySnapshot Battery { get; init; } = new();
    public ChargeLimitStatus? ActiveLimit { get; init; }
    public IReadOnlyList<BackendProbe> Probes { get; init; } = Array.Empty<BackendProbe>();
    public string? Error { get; init; }
    public string PlatformNote { get; init; } = string.Empty;
    public string NextSteps { get; init; } = string.Empty;
}

public sealed class BackendProbe
{
    public required string Name { get; init; }
    public bool Available { get; init; }
    public string Detail { get; init; } = string.Empty;
}
