# Charge Limiter

Windows on ARM64 desktop app aimed at **Dell Latitude 7455** that:

- Shows battery status
- Probes Dell Command | Monitor (WMI) and Dell Command | Configure (`cctk`)
- Tries to apply an **~80% charge policy** when firmware allows it
- Explains the real Latitude 7455 path when software backends are missing

## Latitude 7455 — what actually works

| Approach | On Latitude 7455 (Snapdragon / Win ARM64)? |
|---|---|
| Custom start/stop (e.g. 75–80%) via `PrimaryBattChargeCfg` / WMI | **No** — Dell documents `PrimaryBattChargeCfg` as **not compatible with ARM64** |
| Dell Command \| Configure (`cctk`) for that attribute | Same — attribute unsupported on ARM64 |
| Dell Command \| Monitor ARM64 | **Installable** (Dell lists Latitude 7455). May expose other BIOS attrs; Custom % still typically absent |
| Classic Dell Power Manager custom thresholds | Not a supported ARM64 path for this model |
| **Dell Optimizer → Primarily AC** | **Best practical ~80% docked stop** — lowers charge threshold so the pack does not sit at 100% (with Smart Charging often suspends ~80%) |
| Adaptive / Dynamic Charge | Dell’s recommended adaptive health mode (not a fixed 80%) |
| Qualcomm **Smart Charging** (default) | Heart icon near 100%; reduces max charge voltage — health feature, not a user 80% slider |

### What you should do on the laptop

1. Open **Dell Optimizer** → **Power & Battery**.
2. If **Dynamic Charge Policy** is on, turn it **off** so **Charging Mode** unlocks.
3. Select **Primarily AC** for docked / desk use (~80%-class stop).
4. For travel / full pack: choose **Standard** or **ExpressCharge**.
5. Optional: install **Dell Command | Monitor (WINARM64)** for Latitude 7455 from Dell Support, reboot, re-run this app elevated — if a charge-mode attribute appears, **Set 80% limit** can switch Primarily AC / Adaptive via WMI.

## What this app does in software

When backends exist:

1. Prefer **Custom 75–80%** (x64 Dell machines with the attribute).
2. Else set charge mode to **Primarily AC**, then **Adaptive** (ARM64-friendly ~80% policy).
3. **Restore full charge** → Standard (or Express if that is all that is exposed).

When backends are missing (your screenshot case), buttons stay disabled and the UI shows the Optimizer steps above — that is expected on a stock 7455.

## Prerequisites

| Requirement | Why |
|---|---|
| Windows 11 ARM64 | Native target |
| Run as Administrator | BIOS / WMI writes need elevation |
| Dell Optimizer | Primary control surface on Latitude 7455 |
| Dell Command \| Monitor ARM64 (optional) | Enables WMI probing / mode flips if firmware exposes them |

## Build (Windows ARM64)

```powershell
dotnet publish ChargeLimiter\ChargeLimiter.csproj -c Release -r win-arm64 --self-contained true -o .\publish\win-arm64
.\publish\win-arm64\ChargeLimiter.exe
```

Cross-publish:

```bash
./scripts/publish-win-arm64.sh
```

Demo UI without Dell hardware: `ChargeLimiter.exe --demo`

## Tests

```powershell
dotnet test ChargeLimiter.sln -c Release
```

## Known limitations

- A fixed Custom **75–80%** window is **not** something Dell exposes on ARM64 7455.
- Installing Monitor/Configure does not invent that attribute if the EC/BIOS omit it.
- Smart Charging ≠ a programmable 80% slider.
- BIOS admin password blocks WMI writes.

## License

Use freely for personal battery care on supported Dell hardware.
