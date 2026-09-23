using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

/// <summary>
/// Invokes Dell Command | Configure (cctk.exe) when installed.
/// Official Dell docs: --PrimaryBattChargeCfg is not compatible with ARM64-bit systems.
/// The backend still probes so the UI can report an honest failure on Latitude 7455.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CctkChargeLimitBackend : IChargeLimitBackend
{
    private static readonly string[] CandidatePaths =
    [
        @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe",
        @"C:\Program Files\Dell\Command Configure\X86_64\cctk.exe",
        @"C:\Program Files\Dell\Command Configure\cctk.exe",
        @"C:\Program Files (x86)\Dell\Command Configure\cctk.exe",
        @"C:\Program Files\Dell\Command Configure\ARM64\cctk.exe",
        @"C:\Program Files\Dell\Command Configure\arm64\cctk.exe"
    ];

    public string Name => "Dell Command | Configure (cctk)";
    public int Priority => 20;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ResolveCctkPath() is not null);

    public async Task<ChargeLimitStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolveCctkPath();
        if (path is null)
        {
            return Unsupported("Dell Command | Configure (cctk.exe) was not found.");
        }

        var (exit, stdout, stderr) = await RunAsync(path, "--PrimaryBattChargeCfg", cancellationToken)
            .ConfigureAwait(false);
        var text = $"{stdout}\n{stderr}".Trim();

        if (exit != 0)
        {
            return new ChargeLimitStatus
            {
                BackendName = Name,
                IsSupported = false,
                Mode = ChargeLimitMode.Unsupported,
                Detail = string.IsNullOrWhiteSpace(text)
                    ? $"cctk exited with code {exit}."
                    : text,
                Warning =
                    "Dell documents PrimaryBattChargeCfg as incompatible with ARM64. " +
                    "Latitude 7455 typically cannot set a custom stop threshold this way."
            };
        }

        var parsed = ParseCctkOutput(text);
        return new ChargeLimitStatus
        {
            BackendName = Name,
            IsSupported = true,
            Mode = parsed.Mode,
            StartPercent = parsed.StartPercent,
            StopPercent = parsed.StopPercent,
            Detail = text,
            Warning = parsed.Warning
        };
    }

    public async Task<OperationResult> ApplyEightyPercentAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolveCctkPath();
        if (path is null)
        {
            return OperationResult.Fail("Dell Command | Configure (cctk.exe) was not found.");
        }

        // Dell: PrimaryBattChargeCfg is not compatible with ARM64 — expect failures here on 7455.
        foreach (var (args, label) in new[]
                 {
                     ($"--PrimaryBattChargeCfg=Custom:{ChargeLimitOrchestrator.DefaultStartPercent}-{ChargeLimitOrchestrator.DefaultStopPercent}",
                         $"Custom:{ChargeLimitOrchestrator.DefaultStartPercent}-{ChargeLimitOrchestrator.DefaultStopPercent}"),
                     ("--PrimaryBattChargeCfg=PrimAcUse", "Primarily AC Use"),
                     ("--PrimaryBattChargeCfg=Adaptive", "Adaptive")
                 })
        {
            var (exit, stdout, stderr) = await RunAsync(path, args, cancellationToken).ConfigureAwait(false);
            var text = $"{stdout}\n{stderr}".Trim();
            if (exit != 0 ||
                text.Contains("not compatible", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var status = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok($"Applied {label} via cctk.", status);
        }

        return OperationResult.Fail(
            "cctk could not set Custom, PrimAcUse, or Adaptive. " +
            "Dell documents PrimaryBattChargeCfg as incompatible with ARM64 — use Dell Optimizer → Primarily AC on Latitude 7455.");
    }

    public async Task<OperationResult> RestoreFullChargeAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolveCctkPath();
        if (path is null)
        {
            return OperationResult.Fail("Dell Command | Configure (cctk.exe) was not found.");
        }

        var (exit, stdout, stderr) = await RunAsync(path, "--PrimaryBattChargeCfg=Standard", cancellationToken)
            .ConfigureAwait(false);
        var text = $"{stdout}\n{stderr}".Trim();
        if (exit != 0)
        {
            return OperationResult.Fail(string.IsNullOrWhiteSpace(text) ? $"cctk failed (exit {exit})." : text);
        }

        var status = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Restored Standard charge mode via cctk.", status);
    }

    private static ChargeLimitStatus Unsupported(string detail) =>
        new()
        {
            BackendName = "Dell Command | Configure (cctk)",
            IsSupported = false,
            Mode = ChargeLimitMode.Unsupported,
            Detail = detail
        };

    private static string? ResolveCctkPath()
    {
        foreach (var candidate in CandidatePaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static ChargeLimitStatus ParseCctkOutput(string text)
    {
        var custom = Regex.Match(
            text,
            @"PrimaryBattChargeCfg\s*=\s*Custom\s*:\s*(\d+)\s*-\s*(\d+)",
            RegexOptions.IgnoreCase);
        if (custom.Success)
        {
            return new ChargeLimitStatus
            {
                BackendName = "Dell Command | Configure (cctk)",
                IsSupported = true,
                Mode = ChargeLimitMode.Custom,
                StartPercent = int.Parse(custom.Groups[1].Value),
                StopPercent = int.Parse(custom.Groups[2].Value)
            };
        }

        var mode = Regex.Match(text, @"PrimaryBattChargeCfg\s*=\s*(\w+)", RegexOptions.IgnoreCase);
        var modeName = mode.Success ? mode.Groups[1].Value : "Unknown";
        var mapped = modeName.ToLowerInvariant() switch
        {
            "standard" => ChargeLimitMode.Standard,
            "express" => ChargeLimitMode.Express,
            "adaptive" => ChargeLimitMode.Adaptive,
            "primacuse" => ChargeLimitMode.PrimarilyAc,
            "custom" => ChargeLimitMode.Custom,
            _ => ChargeLimitMode.Unknown
        };

        return new ChargeLimitStatus
        {
            BackendName = "Dell Command | Configure (cctk)",
            IsSupported = true,
            Mode = mapped
        };
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
