# Charge Limiter

Windows on ARM64 desktop app aimed at **Dell Latitude 7455**.

- Shows battery status
- Probes Dell Command | Monitor (WMI) and Dell Command | Configure (`cctk`)
- Tries Custom 75–80% when firmware allows; otherwise Primarily AC / Adaptive via WMI if those attributes exist
- Explains what actually works on this Snapdragon SKU when software backends cannot set a limit

## Latitude 7455 — what actually works

| Approach | On Latitude 7455 (Snapdragon / Win ARM64)? |
|---|---|
| Custom start/stop (e.g. 75–80%) via `PrimaryBattChargeCfg` / WMI | **No** — Dell documents `PrimaryBattChargeCfg` as **not compatible with ARM64** |
| Dell Command \| Configure (`cctk`) for that attribute | Same — unsupported on ARM64 |
| Dell Command \| Monitor ARM64 | **Installable** (Dell lists Latitude 7455). Useful for probing; Custom % still typically absent |
| **Dell Optimizer → Primarily AC / Dynamic Charge** | **Often absent** on this ARM Optimizer build. Power & Battery commonly shows **battery details + Thermal Management only** (confirmed on device). Do not keep looking for Primarily AC on that page if it is not there. |
| **BIOS Power → Battery Configuration** | **Check here first** for a mode list (ExpressCharge is documented for this model; Adaptive / Primarily AC / Standard may appear depending on BIOS). On other Dell platforms, Primarily AC / Adaptive + Smart Charging is what yields the ~80% docked suspend behavior. |
| Qualcomm **Smart Charging** (default) | Heart icon near 100%; reduces max charge voltage — firmware health feature, **not** a user 80% slider |

### What you should click on the laptop

1. **Dell Optimizer → Power & Battery** — note battery % / health / thermal. If there is **no** Dynamic Charge / Charging Mode / Primarily AC control, that is expected on this SKU; move on.
2. **BIOS:** restart → press **F2** → open **Power** → **Battery Configuration**. If you see Adaptive / Primarily AC / Standard / ExpressCharge, pick **Primarily AC** or **Adaptive** for docked health (~80%-class behavior with Smart Charging on other Dell docs), or **Standard / ExpressCharge** when you need a full pack for travel.
3. If BIOS also has no charge-mode list, **Smart Charging is the supported health path** — there is no supported user-settable Custom 75–80% window on ARM64 7455.
4. This app’s **Set 80% limit** stays disabled unless WMI exposes a writable charge-mode attribute (rare on this model even with Monitor installed).

## Build (Windows ARM64)

```powershell
dotnet publish ChargeLimiter\ChargeLimiter.csproj -c Release -r win-arm64 --self-contained true -o .\publish\win-arm64
.\publish\win-arm64\ChargeLimiter.exe
```

```bash
./scripts/publish-win-arm64.sh
```

Demo UI: `ChargeLimiter.exe --demo`

## Tests

```powershell
dotnet test ChargeLimiter.sln -c Release
```

## Known limitations

- Fixed Custom **75–80%** is not exposed on ARM64 7455.
- Optimizer may not offer charging modes on this Qualcomm build.
- Smart Charging ≠ a programmable 80% slider.
- BIOS admin password blocks WMI writes.

## License

Use freely for personal battery care on supported Dell hardware.
