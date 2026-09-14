# ScrollSelectProbe — THROWAWAY

Select-a-scroll-region overlay + actuation spike for wayfinder ticket
[#61](https://github.com/knausj85/Windows-Hinting/issues/61) (parent map
[#54](https://github.com/knausj85/Windows-Hinting/issues/54)). **Not** part of
`Windows-Hinting.sln`, never merges. Verdict + eval in [`FINDINGS.md`](FINDINGS.md).

## Build & run

COMReference needs **full-framework MSBuild** (`dotnet build` fails with MSB4803):

```powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
  -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe'
& $msbuild -restore prototype\ScrollSelectProbe\ScrollSelectProbe.csproj
& .\prototype\ScrollSelectProbe\bin\Debug\net10.0-windows\ScrollSelectProbe.exe
```

## Use

Focus a target app, press **Ctrl+Alt+S** to enter scroll mode (numbered, per-region-colored
overlay). Keys: `0`=window (default/fallback wheel) · `1-9`=pick a UIA region · `H/J`=up/down ·
`K/L`=left/right · `PgUp/PgDn`=page · `Esc`=exit. `--target 0x<hwnd>` enters a window directly
(no hotkey). Console prints each detected region + its actuation path.

Reuses the detection heuristic + COM UIA scaffolding from
`prototype/ScrollExposureProbe` (branch `prototype/scroll-exposure-probe`).
