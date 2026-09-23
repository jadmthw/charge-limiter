using System.Management;
using System.Runtime.Versioning;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

/// <summary>
/// Dell Command | Monitor path: root\dcim\sysman BIOS attributes.
/// On supported Dell platforms this maps to Primary Battery Charge Configuration
/// (Custom start/stop). Dell documents PrimaryBattChargeCfg as incompatible with ARM64.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DellWmiChargeLimitBackend : IChargeLimitBackend
{
    public const string Namespace = @"root\dcim\sysman";

    private static readonly string[] ModeAttributeNames =
    [
        "PrimaryBatteryChargeConfiguration",
        "Primary Batt Charge Config",
        "PrimaryBattChargeCfg",
        "Primary Battery Charge Configuration"
    ];

    private static readonly string[] StartAttributeNames =
    [
        "PrimaryBatteryCustomChargeStart",
        "Primary Battery Custom Charge Start",
        "Custom Charge Start"
    ];

    private static readonly string[] StopAttributeNames =
    [
        "PrimaryBatteryCustomChargeEnd",
        "PrimaryBatteryCustomChargeStop",
        "Primary Battery Custom Charge End",
        "Custom Charge End",
        "Custom Charge Stop"
    ];

    public string Name => "Dell Command | Monitor (WMI)";
    public int Priority => 10;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    Namespace,
                    "SELECT * FROM DCIM_BIOSService");
                using var results = searcher.Get();
                return results.Count > 0;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);

    public Task<ChargeLimitStatus> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                var attributes = ReadAllAttributes();
                var modeRaw = FindValue(attributes, ModeAttributeNames);
                var startRaw = FindValue(attributes, StartAttributeNames);
                var stopRaw = FindValue(attributes, StopAttributeNames);

                if (modeRaw is null && startRaw is null && stopRaw is null)
                {
                    return new ChargeLimitStatus
                    {
                        BackendName = Name,
                        IsSupported = false,
                        Mode = ChargeLimitMode.Unsupported,
                        Detail =
                            "Dell Command | Monitor WMI is present, but no primary-battery charge attributes were exposed. " +
                            "On Latitude 7455, Dell marks PrimaryBattChargeCfg (custom start/stop) as incompatible with ARM64.",
                        Warning =
                            "Use Dell Optimizer → Primarily AC for an ~80% docked stop, or rely on Qualcomm Smart Charging."
                    };
                }

                var mode = ParseMode(modeRaw);
                int? start = ParsePercent(startRaw);
                int? stop = ParsePercent(stopRaw);
                var hasCustomBounds = start is not null && stop is not null;
                // Mode-only systems can still switch to Primarily AC / Adaptive (~80% with Smart Charging).
                var supported = modeRaw is not null || hasCustomBounds;

                return new ChargeLimitStatus
                {
                    BackendName = Name,
                    IsSupported = supported,
                    Mode = mode,
                    StartPercent = start,
                    StopPercent = stop,
                    Detail = $"Mode={modeRaw ?? "(n/a)"}; start={startRaw ?? "(n/a)"}; stop={stopRaw ?? "(n/a)"}",
                    Warning = hasCustomBounds
                        ? null
                        : "Custom start/stop percentages are missing. Set 80% will try Primarily AC / Adaptive instead."
                };
            }
            catch (Exception ex)
            {
                return new ChargeLimitStatus
                {
                    BackendName = Name,
                    IsSupported = false,
                    Mode = ChargeLimitMode.Unsupported,
                    Detail = $"Failed to read Dell BIOS attributes: {ex.Message}"
                };
            }
        }, cancellationToken);

    public Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            const int startPercent = 75;
            const int stopPercent = 80;

            try
            {
                var attributes = ReadAllAttributes();
                var modeName = FindName(attributes, ModeAttributeNames);
                var startName = FindName(attributes, StartAttributeNames);
                var stopName = FindName(attributes, StopAttributeNames);

                // Prefer true custom window when firmware exposes it (rare on ARM64).
                if (modeName is not null && startName is not null && stopName is not null)
                {
                    var startResult = SetAttributes(startName, startPercent.ToString());
                    if (!startResult.Success)
                    {
                        return startResult;
                    }

                    var stopResult = SetAttributes(stopName, stopPercent.ToString());
                    if (!stopResult.Success)
                    {
                        return stopResult;
                    }

                    var customMode = ResolveModeValueByKeyword(attributes, modeName, "Custom") ?? "Custom";
                    var modeResult = SetAttributes(modeName, customMode);
                    if (!modeResult.Success)
                    {
                        return modeResult;
                    }

                    var status = ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return OperationResult.Ok(
                        $"Applied custom charge window {startPercent}%–{stopPercent}% via Dell WMI.",
                        status);
                }

                // ARM64-friendly fallback: Primarily AC / Adaptive ≈ ~80% stop with Smart Charging.
                if (modeName is null)
                {
                    return OperationResult.Fail(
                        "No primary battery charge mode attribute over WMI. " +
                        "On Latitude 7455 use Dell Optimizer → Power & Battery → Primarily AC, " +
                        "or install Dell Command | Monitor (ARM64) and retry.");
                }

                foreach (var keyword in new[] { "Primarily AC", "PrimAc", "AC Use", "Adaptive" })
                {
                    var value = ResolveModeValueByKeyword(attributes, modeName, keyword);
                    if (value is null)
                    {
                        continue;
                    }

                    var modeResult = SetAttributes(modeName, value);
                    if (!modeResult.Success)
                    {
                        continue;
                    }

                    var status = ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                    return OperationResult.Ok(
                        $"Custom start/stop is not available on this ARM64 firmware. " +
                        $"Switched charge mode to '{keyword}' via WMI (Dell’s ~80% docked / health profile with Smart Charging).",
                        status);
                }

                return OperationResult.Fail(
                    "WMI exposes a charge mode attribute but neither Custom nor Primarily AC / Adaptive could be set. " +
                    "Set Primarily AC in Dell Optimizer or BIOS instead.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Dell WMI write failed: {ex.Message}");
            }
        }, cancellationToken);

    public Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                var attributes = ReadAllAttributes();
                var modeName = FindName(attributes, ModeAttributeNames);
                if (modeName is null)
                {
                    return OperationResult.Fail(
                        "Cannot restore full charge: primary battery charge mode attribute is missing.");
                }

                var standardValue = ResolveModeValueByKeyword(attributes, modeName, "Standard")
                    ?? ResolveModeValueByKeyword(attributes, modeName, "Express")
                    ?? "Standard";
                var result = SetAttributes(modeName, standardValue);
                if (!result.Success)
                {
                    return result;
                }

                var status = ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
                return OperationResult.Ok("Restored Standard/Express (full charge) mode via Dell WMI.", status);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Dell WMI restore failed: {ex.Message}");
            }
        }, cancellationToken);

    private static Dictionary<string, BiosAttribute> ReadAllAttributes()
    {
        var map = new Dictionary<string, BiosAttribute>(StringComparer.OrdinalIgnoreCase);

        void Ingest(string className, bool isInteger)
        {
            using var searcher = new ManagementObjectSearcher(
                Namespace,
                $"SELECT AttributeName, CurrentValue{(isInteger ? "" : ", PossibleValues, PossibleValuesDescription")} FROM {className}");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var name = obj["AttributeName"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var current = FirstString(obj["CurrentValue"]);
                string[]? possible = null;
                string[]? descriptions = null;
                if (!isInteger)
                {
                    possible = ToStringArray(obj["PossibleValues"]);
                    descriptions = ToStringArray(obj["PossibleValuesDescription"]);
                }

                map[name] = new BiosAttribute(name, current, possible, descriptions);
            }
        }

        try { Ingest("DCIM_BIOSEnumeration", isInteger: false); } catch { /* optional */ }
        try { Ingest("DCIM_BIOSInteger", isInteger: true); } catch { /* optional */ }
        try { Ingest("DCIM_BIOSString", isInteger: true); } catch { /* optional */ }

        return map;
    }

    private static OperationResult SetAttributes(string attributeName, string attributeValue)
    {
        using var searcher = new ManagementObjectSearcher(Namespace, "SELECT * FROM DCIM_BIOSService");
        using var results = searcher.Get();
        var service = results.Cast<ManagementObject>().FirstOrDefault();
        if (service is null)
        {
            return OperationResult.Fail("DCIM_BIOSService was not found. Install Dell Command | Monitor.");
        }

        var inParams = service.GetMethodParameters("SetBIOSAttributes");
        inParams["AttributeName"] = new[] { attributeName };
        inParams["AttributeValue"] = new[] { attributeValue };
        // Empty password; callers with a BIOS admin password must set it in Dell tools.
        inParams["AuthorizationToken"] = string.Empty;

        var outParams = service.InvokeMethod("SetBIOSAttributes", inParams, null);
        var code = Convert.ToUInt32(outParams?["SetResult"] ?? outParams?["ReturnValue"] ?? 1u);
        return code switch
        {
            0 => OperationResult.Ok($"Set {attributeName}={attributeValue}"),
            2 => OperationResult.Fail(
                "BIOS rejected the change (authentication failure). Clear or supply the BIOS admin password via Dell Command | Configure / Monitor."),
            _ => OperationResult.Fail(
                $"BIOS SetBIOSAttributes returned code {code} for {attributeName}={attributeValue}. " +
                "On ARM64 Latitude 7455 this often means the attribute is unsupported.")
        };
    }

    private static string? FindValue(IReadOnlyDictionary<string, BiosAttribute> map, IEnumerable<string> names) =>
        Find(map, names)?.CurrentValue;

    private static string? FindName(IReadOnlyDictionary<string, BiosAttribute> map, IEnumerable<string> names) =>
        Find(map, names)?.Name;

    private static BiosAttribute? Find(IReadOnlyDictionary<string, BiosAttribute> map, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var attr))
            {
                return attr;
            }
        }

        foreach (var attr in map.Values)
        {
            if (names.Any(n => attr.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                               n.Contains(attr.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return attr;
            }
        }

        return null;
    }

    private static string? ResolveModeValueByKeyword(
        IReadOnlyDictionary<string, BiosAttribute> map,
        string modeName,
        string keyword)
    {
        if (!map.TryGetValue(modeName, out var attr) || attr.PossibleValues is not { Length: > 0 })
        {
            // No enumeration metadata — try the keyword itself as the value.
            return keyword.Contains(' ') ? null : keyword;
        }

        for (var i = 0; i < attr.PossibleValues.Length; i++)
        {
            var description = attr.PossibleDescriptions is { Length: > 0 } && i < attr.PossibleDescriptions.Length
                ? attr.PossibleDescriptions[i]
                : attr.PossibleValues[i];
            if (description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                attr.PossibleValues[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return attr.PossibleValues[i];
            }
        }

        return null;
    }

    private static ChargeLimitMode ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ChargeLimitMode.Unknown;
        }

        if (raw.Contains("Custom", StringComparison.OrdinalIgnoreCase))
        {
            return ChargeLimitMode.Custom;
        }

        if (raw.Contains("Express", StringComparison.OrdinalIgnoreCase))
        {
            return ChargeLimitMode.Express;
        }

        if (raw.Contains("Adaptive", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return ChargeLimitMode.Adaptive;
        }

        if (raw.Contains("Prim", StringComparison.OrdinalIgnoreCase) &&
            raw.Contains("AC", StringComparison.OrdinalIgnoreCase))
        {
            return ChargeLimitMode.PrimarilyAc;
        }

        if (raw.Contains("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return ChargeLimitMode.Standard;
        }

        return ChargeLimitMode.Unknown;
    }

    private static int? ParsePercent(string? raw) =>
        int.TryParse(raw, out var value) ? value : null;

    private static string? FirstString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is string[] arr)
        {
            return arr.FirstOrDefault();
        }

        if (value is object[] objArr)
        {
            return objArr.FirstOrDefault()?.ToString();
        }

        return value.ToString();
    }

    private static string[]? ToStringArray(object? value) =>
        value switch
        {
            null => null,
            string[] s => s,
            object[] o => o.Select(x => x?.ToString() ?? string.Empty).ToArray(),
            _ => new[] { value.ToString() ?? string.Empty }
        };

    private sealed record BiosAttribute(
        string Name,
        string? CurrentValue,
        string[]? PossibleValues,
        string[]? PossibleDescriptions);
}
