using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsBatteryStatusService : IBatteryStatusService
{
    public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
                using var results = searcher.Get();
                var battery = results.Cast<ManagementObject>().FirstOrDefault();
                if (battery is null)
                {
                    return new BatterySnapshot
                    {
                        Available = false,
                        StatusText = "No Win32_Battery instance found."
                    };
                }

                int? percent = battery["EstimatedChargeRemaining"] is null
                    ? null
                    : Convert.ToInt32(battery["EstimatedChargeRemaining"]);
                var statusCode = battery["BatteryStatus"] is null
                    ? 0
                    : Convert.ToInt32(battery["BatteryStatus"]);

                var charging = statusCode is 6 or 7 or 8 or 9;
                var pluggedIn = statusCode is 2 or 6 or 7 or 8 or 9;

                var text = statusCode switch
                {
                    1 => "On battery power",
                    2 => "On AC power",
                    3 => "Fully charged",
                    4 => "Low",
                    5 => "Critical",
                    6 => "Charging",
                    7 => "Charging (high)",
                    8 => "Charging (low)",
                    9 => "Charging (critical)",
                    11 => "Partially charged",
                    _ => $"Battery status code {statusCode}"
                };

                if (percent is int p)
                {
                    text = $"{text} · {p}%";
                }

                return new BatterySnapshot
                {
                    Available = true,
                    Percent = percent,
                    Charging = charging,
                    PluggedIn = pluggedIn,
                    StatusText = text
                };
            }
            catch (Exception ex)
            {
                return new BatterySnapshot
                {
                    Available = false,
                    StatusText = $"Could not read battery status: {ex.Message}"
                };
            }
        }, cancellationToken);
}

public sealed class PlatformInfo
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsArm64 =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ||
        RuntimeInformation.OSArchitecture == Architecture.Arm64;

    public static string ArchitectureLabel =>
        $"{RuntimeInformation.OSDescription} / process={RuntimeInformation.ProcessArchitecture} / os={RuntimeInformation.OSArchitecture}";

    public static string Latitude7455Note =>
        "Latitude 7455 (Snapdragon / Windows on ARM): Dell Smart Charging is on by default. " +
        "Dell documents PrimaryBattChargeCfg (custom start/stop %) as not compatible with ARM64. " +
        "A hard user-settable 75–80% Custom window is generally not available on this model.";

    public static string BuildPlatformNote()
    {
        if (!IsWindows)
        {
            return "Running off Windows — use the win-arm64 build on the Latitude 7455. " + Latitude7455Note;
        }

        return IsArm64
            ? Latitude7455Note
            : "Non-ARM process architecture detected. Prefer the win-arm64 build on Latitude 7455.";
    }

    public static string BuildNextSteps(bool writableBackend)
    {
        if (writableBackend)
        {
            return "Writable Dell backend detected. Use Set 80% limit (Custom if available, otherwise Primarily AC / Adaptive).";
        }

        return
            "What to do on Latitude 7455 (best practical ~80% stop):\n" +
            "1. Open Dell Optimizer (included on Latitude) → Power & Battery.\n" +
            "2. If Dynamic Charge Policy is on, turn it off so Charging Mode unlocks.\n" +
            "3. Choose Primarily AC — Dell lowers the charge threshold so the pack does not sit at 100% " +
            "(with Smart Charging this commonly suspends around ~80% while docked).\n" +
            "4. Adaptive / Dynamic Charge is Dell’s recommended “set and forget” health mode (not a fixed 80%).\n" +
            "5. Optional: install Dell Command | Monitor ARM64 for Latitude 7455, reboot, re-run this app elevated — " +
            "if a charge-mode attribute appears, Set 80% can switch Primarily AC via WMI. " +
            "Custom 75–80% via cctk/WMI remains unsupported on ARM64 per Dell.\n" +
            "6. For travel/full charge: Optimizer → Standard or ExpressCharge (or BIOS equivalent).";
    }
}
