# Charge Limiter

Pure-software battery care for **Dell Latitude 7455** (Snapdragon / Windows on ARM).

No external hardware. No smart plugs. Soft-cap monitor is the controllable path.

## What actually holds ~80% on this device?

| Mechanism | TRUE hard stop at 80%? | Controllable in this app? | Notes |
|---|---|---|---|
| Dell `PrimaryBattChargeCfg` Custom 75–80% | **No on ARM64** | Probe only | Dell documents **incompatible with ARM64** |
| Dell Optimizer Primarily AC / Dynamic Charge | Often absent on 7455 | No | Power & Battery page often shows thermal + status only |
| Windows / OEM **Smart charging** (heart icon) | OEM-defined, not a fixed user 80% | Probe only | [Microsoft](https://support.microsoft.com/en-us/windows/experience/power-battery/use-smart-charging-in-windows): each OEM implements it in **firmware**. Surface has Limit-to-80% in the Surface app — **not** on Latitude. **No documented universal `powercfg` / registry hard 80% lever.** |
| Writable WMI charge gate (`root\wmi` / `root\cimv2` / `root\dcim\sysman`) | Not found on stock 7455 | Probe only | App enumerates battery/power classes; none exposed a user 80% write on this SKU |
| **Soft cap monitor (this app)** | Soft (software action) | **Yes** | At target % on AC → Notify / Sleep / Hibernate. Prevents idling at 100%. Does **not** cut charge current in the EC. |

**Bottom line:** There is **no confirmed pure-software TRUE hard 80% charge-stop** on Latitude 7455 ARM64. The working controllable path is the **soft cap**.

### Smart Charging investigation (exact levers)

Commands / paths this app probes on Windows:

```text
powercfg /q
powercfg -attributes SUB_BATTERY -ATTRIB_HIDE
HKLM\SYSTEM\CurrentControlSet\Control\Power   (and charge-related subkeys/values)
HKLM\SOFTWARE\Microsoft\Surface               (Surface-only; absent on Dell)
HKLM\SOFTWARE\Dell
WMI: root\cimv2, root\wmi, root\dcim\sysman   (battery/power/MSFT_/Qualcomm-named classes)
```

Result on Latitude 7455: OEM Smart Charging (heart near 100%, reduced voltage) may already be active in Qualcomm firmware — that is **not** a user-tunable fixed 80% slider, and Microsoft does not document a Windows Settings toggle backed by a public registry DWORD for Dell.

## Soft cap (flagship — guaranteed software path)

1. Run `ChargeLimiter.exe` (elevated optional for soft cap; needed for Dell WMI probes).
2. Enable **Soft cap monitor**.
3. Target **80%**, rearm **75%**, action **Notify** (or Sleep / Hibernate).
4. Optionally enable **Run at Windows logon** (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ChargeLimiter`).
5. **Save & start** — closing the window hides to the tray; use tray **Exit** to quit.

Behavior:

- On AC and battery ≥ target → fire action once, then wait to rearm.
- Rearm when battery ≤ rearm **or** you unplug.
- Poll interval configurable (default 45s).

Settings file: `%LocalAppData%\ChargeLimiter\softcap-settings.json`

This is explicitly a **soft** cap: it prevents you from leaving the pack sitting near 100% while plugged in. It does **not** tell the embedded controller to stop accepting charge current.

## Build (Windows ARM64)

```powershell
dotnet publish ChargeLimiter\ChargeLimiter.csproj -c Release -r win-arm64 --self-contained true -o .\publish\win-arm64
.\publish\win-arm64\ChargeLimiter.exe
```

```bash
./scripts/publish-win-arm64.sh
# or: CHARGE_LIMITER_DEMO=1 for UI without Windows battery APIs
```

## Tests

```powershell
dotnet test ChargeLimiter.sln -c Release
```

Logic tests (hysteresis + monitor) are mockable and run off-Windows.

## What the probes report

On launch the app:

- Runs `powercfg /q` and scans names for charge/smart/limit-like settings.
- Best-effort `powercfg -attributes SUB_BATTERY -ATTRIB_HIDE`.
- Scans `HKLM\SYSTEM\CurrentControlSet\Control\Power` (and Dell / Surface software keys).
- Lists battery/power-related WMI classes under `root\cimv2`, `root\wmi`, `root\dcim\sysman`.
- Lists Dell DCIM BIOS attributes (charge-related first).

These are diagnostic. They do **not** invent a hard 80% stop if firmware never exposed one.

## License

Use freely for personal battery care.
