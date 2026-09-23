namespace ChargeLimiter.Models;

public enum ChargeLimitMode
{
    Unknown,
    Standard,
    Express,
    Adaptive,
    PrimarilyAc,
    Custom,
    Unsupported
}

public sealed class ChargeLimitStatus
{
    public required string BackendName { get; init; }
    public bool IsSupported { get; init; }
    public ChargeLimitMode Mode { get; init; }
    public int? StartPercent { get; init; }
    public int? StopPercent { get; init; }
    public string? Detail { get; init; }
    public string? Warning { get; init; }

    public string Summary
    {
        get
        {
            if (!IsSupported)
            {
                return Detail ?? "Charge limiting is not available on this system.";
            }

            return Mode switch
            {
                ChargeLimitMode.Custom when StartPercent is int start && StopPercent is int stop =>
                    $"Custom charge window: start {start}%, stop {stop}%",
                ChargeLimitMode.Custom => "Custom charge window is active.",
                ChargeLimitMode.Standard => "Standard (full charge).",
                ChargeLimitMode.Express => "Express charge.",
                ChargeLimitMode.Adaptive => "Adaptive charge.",
                ChargeLimitMode.PrimarilyAc => "Primarily AC use.",
                _ => Detail ?? "Charge configuration read successfully."
            };
        }
    }
}

public sealed class BatterySnapshot
{
    public bool Available { get; init; }
    public int? Percent { get; init; }
    public bool? Charging { get; init; }
    public bool? PluggedIn { get; init; }
    public string StatusText { get; init; } = "Battery status unavailable.";
}

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public ChargeLimitStatus? Status { get; init; }

    public static OperationResult Ok(string message, ChargeLimitStatus? status = null) =>
        new() { Success = true, Message = message, Status = status };

    public static OperationResult Fail(string message, ChargeLimitStatus? status = null) =>
        new() { Success = false, Message = message, Status = status };
}
