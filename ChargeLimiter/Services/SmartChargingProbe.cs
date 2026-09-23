using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using ChargeLimiter.Models;
using Microsoft.Win32;

namespace ChargeLimiter.Services;

public interface ISmartChargingProbe
{
    Task<SmartChargingProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Enumerates powercfg battery settings + registry clues + WMI power/battery classes.
/// On Latitude 7455 / generic Windows, Smart charging is OEM firmware — Microsoft documents
/// no universal user powercfg/registry hard 80% lever outside OEM apps (e.g. Surface).
/// </summary>
public sealed class SmartChargingProbe : ISmartChargingProbe
{
    public async Task<SmartChargingProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<string>();
        var powerNames = new List<string>();
        var wmiClasses = new List<string>();
        var foundHard = false;
        var oemLikely = false;

        if (!OperatingSystem.IsWindows())
        {
            findings.Add("Not running on Windows — Smart Charging / powercfg probe skipped.");
            return new SmartChargingProbeResult
            {
                Summary = WindowsSmartChargingNotes.Summary,
                Findings = findings
            };
        }

        // powercfg /q full dump — look for charge/smart/threshold-like setting names
        var q = await RunCaptureAsync("powercfg", "/q", cancellationToken).ConfigureAwait(false);
        if (q.Exit == 0)
        {
            foreach (Match m in Regex.Matches(
                         q.StdOut,
                         @"Power Setting GUID:\s*[0-9a-fA-F\-]+\s*\(([^)]+)\)",
                         RegexOptions.Multiline))
            {
                var name = m.Groups[1].Value.Trim();
                powerNames.Add(name);
                if (LooksChargeRelated(name))
                {
                    findings.Add($"powercfg setting: {name}");
                    if (Regex.IsMatch(name, @"charge\s*limit|max(imum)?\s*charge|stop\s*charg|smart\s*charg",
                            RegexOptions.IgnoreCase))
                    {
                        foundHard = true;
                        findings.Add($"Possible hard-limit candidate in powercfg: {name}");
                    }
                }
            }

            findings.Add($"powercfg /q returned {powerNames.Count} named power settings.");
        }
        else
        {
            findings.Add($"powercfg /q failed (exit {q.Exit}): {q.StdErr}".Trim());
        }

        // Unhide SUB_BATTERY category (best-effort; requires admin). Does not create a charge limit.
        var unhide = await RunCaptureAsync("powercfg", "-attributes SUB_BATTERY -ATTRIB_HIDE", cancellationToken)
            .ConfigureAwait(false);
        findings.Add(unhide.Exit == 0
            ? "Ran powercfg -attributes SUB_BATTERY -ATTRIB_HIDE (reveals hidden battery discharge settings if any)."
            : $"Could not unhide SUB_BATTERY attributes (exit {unhide.Exit}) — run elevated to retry.");

        // Registry scan under Power
        ScanRegistry(findings, ref foundHard, ref oemLikely);

        // WMI namespaces
        if (OperatingSystem.IsWindows())
        {
            EnumerateWmi(wmiClasses, findings);
        }

        oemLikely = oemLikely || findings.Any(f =>
            f.Contains("SmartCharg", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("heart", StringComparison.OrdinalIgnoreCase));

        var summary = foundHard
            ? "A charge-limit-like power setting name was found — inspect findings and try powercfg /setacvalueindex if writable."
            : WindowsSmartChargingNotes.Summary;

        return new SmartChargingProbeResult
        {
            OemSmartChargingLikelyActive = oemLikely,
            FoundUserControllableHardLimit = foundHard,
            Summary = summary,
            Findings = findings,
            PowerSettingNames = powerNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            WmiClasses = wmiClasses.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList()
        };
    }

    private static bool LooksChargeRelated(string name) =>
        Regex.IsMatch(name,
            @"batt|charge|ac |adapter|smart|threshold|limit|express|peak",
            RegexOptions.IgnoreCase);

    [SupportedOSPlatform("windows")]
    private static void ScanRegistry(List<string> findings, ref bool foundHard, ref bool oemLikely)
    {
        string[] roots =
        [
            @"SYSTEM\CurrentControlSet\Control\Power",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" // unlikely but cheap
        ];

        foreach (var root in roots)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key is null)
                {
                    findings.Add($"Registry HKLM\\{root}: not present.");
                    continue;
                }

                Walk(key, root, findings, ref foundHard, ref oemLikely, depth: 0);
            }
            catch (Exception ex)
            {
                findings.Add($"Registry HKLM\\{root}: {ex.Message}");
            }
        }

        // Known Microsoft / Surface paths (present only on Surface — read for honesty)
        ProbeKnownSurfaceKeys(findings, ref foundHard);

        // Dell-specific software keys
        try
        {
            using var dell = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Dell");
            if (dell is null)
            {
                findings.Add("Registry HKLM\\SOFTWARE\\Dell: not present.");
            }
            else
            {
                foreach (var sub in dell.GetSubKeyNames().Take(30))
                {
                    findings.Add($"Dell software key: HKLM\\SOFTWARE\\Dell\\{sub}");
                    if (LooksChargeRelated(sub))
                    {
                        oemLikely = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            findings.Add($"Dell registry scan: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ProbeKnownSurfaceKeys(List<string> findings, ref bool foundHard)
    {
        // Surface "Limit to 80%" lives in the Surface app + firmware — not a documented public
        // powercfg GUID. We only detect whether Surface software keys exist on this machine.
        string[] surfacePaths =
        [
            @"SOFTWARE\Microsoft\Surface",
            @"SOFTWARE\Microsoft\Surface\OSConfig",
            @"SYSTEM\CurrentControlSet\Control\Power\EnergyEstimation\TaggedEnergy"
        ];

        foreach (var path in surfacePaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                findings.Add(key is null
                    ? $"Registry HKLM\\{path}: not present (expected on non-Surface)."
                    : $"Registry HKLM\\{path}: present (Surface/energy estimation path).");
            }
            catch (Exception ex)
            {
                findings.Add($"Registry HKLM\\{path}: {ex.Message}");
            }
        }

        findings.Add(
            "No universal HKLM SmartCharging DWORD was found in Microsoft docs. " +
            "Dell Latitude 7455 does not expose Surface-style Limit-to-80%.");
        _ = foundHard; // Surface presence alone is not a writable hard limit on Dell.
    }

    [SupportedOSPlatform("windows")]
    private static void Walk(
        RegistryKey key,
        string path,
        List<string> findings,
        ref bool foundHard,
        ref bool oemLikely,
        int depth)
    {
        if (depth > 3)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            if (!LooksChargeRelated(path) && !LooksChargeRelated(valueName))
            {
                continue;
            }

            var val = key.GetValue(valueName);
            findings.Add($"Registry {path}\\{valueName} = {val}");
            if (Regex.IsMatch(valueName + path, @"SmartCharg|ChargeLimit|MaxCharge|StopCharge",
                    RegexOptions.IgnoreCase))
            {
                foundHard = true;
                oemLikely = true;
            }
        }

        foreach (var subName in key.GetSubKeyNames().Take(40))
        {
            if (!LooksChargeRelated(subName) && depth > 0)
            {
                continue;
            }

            try
            {
                using var sub = key.OpenSubKey(subName);
                if (sub is not null)
                {
                    Walk(sub, path + "\\" + subName, findings, ref foundHard, ref oemLikely, depth + 1);
                }
            }
            catch
            {
                // skip denied
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EnumerateWmi(List<string> wmiClasses, List<string> findings)
    {
        void ListNamespace(string ns)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(ns, "SELECT * FROM meta_class");
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    var name = obj["__CLASS"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (LooksChargeRelated(name) ||
                        name.Contains("Battery", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Acpi", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("MSFT_", StringComparison.OrdinalIgnoreCase))
                    {
                        wmiClasses.Add($"{ns}:{name}");
                    }
                }
            }
            catch (Exception ex)
            {
                findings.Add($"WMI {ns}: {ex.Message}");
            }
        }

        ListNamespace(@"root\cimv2");
        ListNamespace(@"root\wmi");
        ListNamespace(@"root\dcim\sysman");
        findings.Add($"WMI battery/power-related classes matched: {wmiClasses.Count}.");
    }

    private static async Task<(int Exit, string StdOut, string StdErr)> RunCaptureAsync(
        string file,
        string args,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}

public static class WindowsSmartChargingNotes
{
    public const string Summary =
        "Microsoft: Smart charging is implemented by the OEM in firmware — there is no documented universal " +
        "Windows powercfg/registry hard 80% lever. Surface exposes Limit-to-80% in the Surface app; " +
        "Dell Latitude 7455 uses Qualcomm Smart Charging (heart near 100%, voltage reduce) without a user 80% slider. " +
        "This app’s software soft-cap (notify/sleep/hibernate at target %) is the controllable pure-software path.";

    public static string FormatAttributeReport(IReadOnlyList<BiosAttributeRow> rows)
    {
        if (rows.Count == 0)
        {
            return "No DCIM BIOS attributes returned.";
        }

        var sb = new StringBuilder();
        var charge = rows.Where(r => r.LooksChargeRelated).ToList();
        sb.AppendLine($"Total BIOS attributes: {rows.Count}. Charge-related keyword matches: {charge.Count}.");
        foreach (var row in charge.Take(50))
        {
            sb.AppendLine($"  • {row.Name} = {row.CurrentValue ?? "(null)"}");
        }

        if (charge.Count == 0)
        {
            sb.AppendLine("No attribute names matched batt/charge/ac/power/smart/express/prim.");
        }

        return sb.ToString().TrimEnd();
    }
}
