using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChargeLimiter.Models;
using ChargeLimiter.Services;

namespace ChargeLimiter.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        StatusHeadline = "Starting…";
        StatusDetail = "Reading battery and soft-cap settings.";
        IsLoading = true;

        var s = services.SoftCap.Settings;
        SoftCapEnabled = s.Enabled;
        TargetPercent = s.TargetPercent;
        RearmPercent = s.RearmPercent;
        SelectedActionIndex = (int)s.Action;
        PollSeconds = s.PollSeconds;
        RunAtLogin = s.RunAtLogin;

        services.SoftCap.StatusChanged += (_, _) =>
        {
            var st = services.SoftCap.LastStatus;
            SoftCapHeadline = st.Headline;
            SoftCapDetail = st.Detail;
            SoftCapLive =
                $"Battery {st.BatteryPercent?.ToString() ?? "—"}% · AC={(st.OnAc is true ? "yes" : st.OnAc is false ? "no" : "—")} · " +
                $"armed={(st.Armed ? "yes" : "no")} · {st.LastAction}";
        };
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusHeadline = string.Empty;
    [ObservableProperty] private string _statusDetail = string.Empty;
    [ObservableProperty] private string _batteryText = "Battery: —";
    [ObservableProperty] private string _firmwareText = "Firmware hard limit: unavailable";
    [ObservableProperty] private string _smartChargingText = string.Empty;
    [ObservableProperty] private string _biosReport = string.Empty;
    [ObservableProperty] private string _platformNote = string.Empty;

    // Soft cap UI
    [ObservableProperty] private bool _softCapEnabled;
    [ObservableProperty] private int _targetPercent = 80;
    [ObservableProperty] private int _rearmPercent = 75;
    [ObservableProperty] private int _pollSeconds = 45;
    [ObservableProperty] private int _selectedActionIndex;
    [ObservableProperty] private bool _runAtLogin;
    [ObservableProperty] private string _softCapHeadline = "Soft cap is off";
    [ObservableProperty] private string _softCapDetail = string.Empty;
    [ObservableProperty] private string _softCapLive = string.Empty;
    [ObservableProperty] private string _softCapMessage = string.Empty;
    [ObservableProperty] private bool _hasSoftCapMessage;
    [ObservableProperty] private bool _softCapMessageOk;

    public ObservableCollection<string> ActionChoices { get; } = new()
    {
        "Notify (toast / message)",
        "Sleep",
        "Hibernate"
    };

    public ObservableCollection<string> ProbeLines { get; } = new();

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await RefreshAllAsync().ConfigureAwait(true);
            if (_services.SoftCap.Settings is { Enabled: true, RunMonitorInBackground: true })
            {
                _services.SoftCap.Start();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshAllAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveSoftCapAsync()
    {
        HasSoftCapMessage = false;
        try
        {
            var settings = new SoftCapSettings
            {
                Enabled = SoftCapEnabled,
                TargetPercent = TargetPercent,
                RearmPercent = RearmPercent,
                PollSeconds = PollSeconds,
                Action = (SoftCapAction)Math.Clamp(SelectedActionIndex, 0, 2),
                RunMonitorInBackground = true,
                RunAtLogin = RunAtLogin
            };
            _services.SoftCap.UpdateSettings(settings);
            await _services.SoftCap.TickAsync().ConfigureAwait(true);
            SoftCapMessageOk = true;
            SoftCapMessage = SoftCapEnabled
                ? "Soft cap saved and monitor running."
                : "Soft cap disabled and monitor stopped.";
            HasSoftCapMessage = true;
        }
        catch (Exception ex)
        {
            SoftCapMessageOk = false;
            SoftCapMessage = ex.Message;
            HasSoftCapMessage = true;
        }
    }

    [RelayCommand]
    private async Task TickSoftCapNowAsync()
    {
        await _services.SoftCap.TickAsync().ConfigureAwait(true);
    }

    private async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            var battery = await _services.Battery.GetSnapshotAsync().ConfigureAwait(true);
            BatteryText = $"Battery: {battery.StatusText}";

            var dell = await _services.Dell.RefreshAsync().ConfigureAwait(true);
            ProbeLines.Clear();
            foreach (var p in dell.Probes)
            {
                ProbeLines.Add($"{p.Name}: {(p.Available ? "ready" : "missing")} — {p.Detail}");
            }

            FirmwareText = dell.ActiveLimit is { IsSupported: true } lim
                ? $"Firmware path: {lim.Summary} via {lim.BackendName}"
                : "Firmware hard 80% stop: not available (PrimaryBattChargeCfg unsupported on ARM64 / attributes missing).";

            StatusHeadline = "Pure-software charge care for Latitude 7455";
            StatusDetail =
                "Dell/Qualcomm firmware does not expose a user Custom 75–80% stop on this Snapdragon SKU. " +
                "Use the Soft cap below to act at ~80% on AC (notify / sleep / hibernate). " +
                "OEM Smart Charging (heart icon) may already reduce stress near 100% — it is not a fixed 80% slider.";

            PlatformNote = PlatformInfo.BuildPlatformNote() + "\n\n" + WindowsSmartChargingNotes.Summary;

            var probe = await _services.Probe.ProbeAsync().ConfigureAwait(true);
            SmartChargingText = probe.Summary + "\n" + string.Join("\n", probe.Findings.Take(12));
            if (probe.WmiClasses.Count > 0)
            {
                SmartChargingText += "\nWMI classes (sample): " + string.Join(", ", probe.WmiClasses.Take(15));
            }

            var bios = await _services.Bios.EnumerateAsync().ConfigureAwait(true);
            BiosReport = WindowsSmartChargingNotes.FormatAttributeReport(bios);

            await _services.SoftCap.TickAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusHeadline = "Status refresh failed";
            StatusDetail = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
