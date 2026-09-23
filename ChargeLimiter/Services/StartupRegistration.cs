using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ChargeLimiter.Services;

public interface IStartupRegistration
{
    bool IsRegistered { get; }
    void SetEnabled(bool enabled);
}

/// <summary>
/// HKCU Run key so soft-cap can start with the user session (no admin required).
/// </summary>
public sealed class StartupRegistration : IStartupRegistration
{
    public const string ValueName = "ChargeLimiter";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsRegistered
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ApplyWindows(enabled);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("Could not open HKCU\\…\\Run for ChargeLimiter.");
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath
                  ?? Process.GetCurrentProcess().MainModule?.FileName
                  ?? throw new InvalidOperationException("Could not resolve ChargeLimiter.exe path.");
        key.SetValue(ValueName, $"\"{exe}\"");
    }
}

/// <summary>No-op for demo / non-Windows unit tests.</summary>
public sealed class NullStartupRegistration : IStartupRegistration
{
    public bool IsRegistered { get; private set; }
    public void SetEnabled(bool enabled) => IsRegistered = enabled;
}
