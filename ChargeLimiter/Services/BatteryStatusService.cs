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
        "Latitude 7455 (Snapdragon / Windows on ARM): Qualcomm Smart Charging is on by default " +
        "(heart icon near 100% = reduced max charge voltage). " +
        "Dell documents PrimaryBattChargeCfg (custom start/stop %) as not compatible with ARM64. " +
        "Dell Optimizer on this SKU often shows only battery status + Thermal Management — not Primarily AC.";

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
            return "A writable Dell charge-mode attribute was found. Use Set 80% limit " +
                   "(Custom if exposed; otherwise Primarily AC / Adaptive).";
        }

        return
            "What to do on Latitude 7455 (honest path):\n" +
            "1. Dell Optimizer → Power & Battery on this ARM SKU typically shows battery details + Thermal Management only. " +
            "If you do not see Dynamic Charge / Charging Mode / Primarily AC, that control is not offered in Optimizer for this device — " +
            "do not keep hunting for it on that page.\n" +
            "2. Check BIOS: restart → mash F2 → look under Power / Battery Configuration " +
            "(ExpressCharge is documented for this model; Adaptive / Primarily AC / Standard may appear depending on BIOS). " +
            "Primarily AC / Adaptive + Smart Charging is what Dell documents as the ~80% docked suspend behavior on other platforms.\n" +
            "3. If BIOS has no charge-mode list either, rely on Qualcomm Smart Charging (default) for battery health — " +
            "it is not a user 80% slider; a fixed Custom 75–80% window is unsupported on ARM64 per Dell.\n" +
            "4. Optional: Dell Command | Monitor ARM64 is already useful for probing. Custom % via WMI/cctk stays unsupported on ARM64.\n" +
            "5. For a full charge before travel: BIOS Battery Configuration → Standard or ExpressCharge when those entries exist.";
    }
}
