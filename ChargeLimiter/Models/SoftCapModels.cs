namespace ChargeLimiter.Models;

public enum SoftCapAction
{
    Notify = 0,
    Sleep = 1,
    Hibernate = 2
}

/// <summary>
/// Soft-cap settings: when on AC and battery >= TargetPercent, fire an action
/// so the pack does not idle at 100%. Rearm below RearmPercent or when unplugged.
/// This is NOT a firmware charge-stop.
/// </summary>
public sealed class SoftCapSettings
{
    public bool Enabled { get; set; }
    public int TargetPercent { get; set; } = 80;
    public int RearmPercent { get; set; } = 75;
    public int PollSeconds { get; set; } = 45;
    public SoftCapAction Action { get; set; } = SoftCapAction.Notify;
    public bool RunMonitorInBackground { get; set; } = true;
    /// <summary>Register HKCU Run so the app starts at user logon.</summary>
    public bool RunAtLogin { get; set; }

    public bool IsValid =>
        TargetPercent > RearmPercent &&
        TargetPercent is >= 50 and <= 100 &&
        RearmPercent is >= 40 and < 100 &&
        PollSeconds is >= 10 and <= 600;
}

public sealed class SoftCapStatus
{
    public bool Enabled { get; init; }
    public string Headline { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int? BatteryPercent { get; init; }
    public bool? OnAc { get; init; }
    public bool Armed { get; init; }
    public bool Triggered { get; init; }
    public string LastAction { get; init; } = string.Empty;
    public DateTimeOffset? LastPollUtc { get; init; }
}

public sealed class SmartChargingProbeResult
{
    public bool OemSmartChargingLikelyActive { get; init; }
    public bool FoundUserControllableHardLimit { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PowerSettingNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> WmiClasses { get; init; } = Array.Empty<string>();
}

public sealed class BiosAttributeRow
{
    public required string Name { get; init; }
    public string? CurrentValue { get; init; }
    public bool LooksChargeRelated { get; init; }
}
