# Project map — NovemposCas

Two-project solution (`CasScaleSender.sln`). Everything except `cli/`, `installer/` and assets lives flat in the repo root.

## Directory tree

```
.
├── CasScaleSender.sln       # Solution: GUI + CLI (only configs: Debug|x86, Release|x86)
├── CasScaleSender.csproj    # GUI project -> NovemposTerazi.exe (WinExe)
├── Program.cs               # GUI entry point (Application.Run(MainForm))
├── MainForm.cs              # Main window: scale list mgmt, send/receive/print orchestration
├── ScaleEditForm.cs         # Add/edit-scale dialog with TCP connection test
├── AppSettings.cs           # Settings load/save (%APPDATA%\Novempos\ayarlar.txt)
├── ScaleConfig.cs           # One scale definition: Name/Ip/Port/Model/DataType
├── ExcelReader.cs           # Hand-rolled .xlsx reader (ZIP + OpenXML parsing, no NuGet)
├── XlsxWriter.cs            # Hand-rolled minimal .xlsx writer (inline-string cells)
├── JsonPluIo.cs             # JSON read/write of PLU rows (POS <-> CLI integration)
├── PluBuilder.cs            # Row dict -> fixed-width CAS V06 PLU record string
├── PluReader.cs             # V06 record -> row dict (BuildV06 inverse) + export column list
├── CasNetReader.cs          # Direct-TCP PLU reading (CL-Works ASCII protocol) + conn test
├── PluListPrinter.cs        # 80mm receipt-printer PLU list rendering (GDI+ PrintDocument)
├── App.config               # .NET 4.8 runtime declaration only
├── OKUBENI.txt              # Turkish end-user guide (installed with the app)
├── ornek_plu.xlsx           # Sample import file (expected header names)
├── novempos.ico             # App icon, embedded into the GUI exe
├── Properties/              # AssemblyInfo.cs: version + branding (GUI project only)
├── cli/                     # novempos-cli console app (links the shared root .cs files)
│   ├── NovemposCli.csproj   # Links ..\*.cs as shared\* ; own files below
│   ├── Program.cs           # Arg parsing; commands: send / receive / print; exit codes
│   └── ScaleHost.cs         # Off-screen form hosting the OCX for console sends
├── installer/               # installer.iss — Inno Setup 6 script (registers OCX, GUI/CLI components)
├── libs/                    # CAS COM interop DLLs (referenced by both projects) — do not touch
├── runtime/                 # CasScale.ocx, CAS native DLLs, DATAOPTION.INI, register.bat — do not touch
└── logos/                   # Brand images (not compiled into anything)
```

Build outputs land in `bin\Release\` (GUI) and `cli\bin\Release\` (CLI); both gitignored.

## To do X, look here

| Task | File(s) |
|---|---|
| Change Excel (.xlsx) import parsing | `ExcelReader.cs` |
| Change the V06 record sent to the scale | `PluBuilder.cs` + `PluReader.cs` (mirrors) |
| Change columns exported on receive (xlsx/JSON) | `PluReader.cs` (`Columns`) |
| Change scale reading (AL), TCP protocol, connection test | `CasNetReader.cs` |
| GUI layout, send flow, scale list, busy/cancel state | `MainForm.cs` |
| Scale add/edit dialog & validation | `ScaleEditForm.cs` |
| CLI commands, flags, help text, exit codes | `cli/Program.cs` |
| CLI send execution (OCX hosting) | `cli/ScaleHost.cs` |
| Persisted settings / ayarlar.txt keys | `AppSettings.cs`, `ScaleConfig.cs` |
| JSON contract with the Flutter POS | `JsonPluIo.cs` (+ `--json` flags in `cli/Program.cs`) |
| 80mm print layout / fonts | `PluListPrinter.cs` |
| Installer contents, shortcuts, OCX registration | `installer/installer.iss` |
| Version bump (3 places) | `Properties/AssemblyInfo.cs`, `cli/Program.cs`, `installer/installer.iss` |
| End-user documentation (Turkish) | `OKUBENI.txt` |

## Entry points

- GUI: `Program.cs` → `MainForm` constructor does all wiring (settings load, UI build, OCX init).
- CLI: `cli/Program.cs` `Main` → `DoSend` / `DoReceive` / `DoPrint`.
- No DI container or router. The shared configuration hub is `AppSettings.Load()`, called by both entry points.
