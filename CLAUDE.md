# NovemposCas — Novempos Terazi PLU Gonderici (CAS scale PLU sender)

Windows desktop (WinForms) + CLI tool that pushes PLU/product lists to CAS CL3000/CL5000 label scales over TCP/IP, reads PLUs back from a scale, and prints PLU lists on 80mm receipt printers. Sending uses the official CasScale.ocx COM component; reading speaks the CL-Works ASCII protocol over raw TCP. Within NovemPOS it is a standalone on-prem tool: the Flutter POS integrates by shelling out to `novempos-cli` with JSON files — there is no network/API link to other NovemPOS repos.

## Reference docs (read on demand, do not paste contents here)
- Project map: `docs/PROJECT_MAP.md`
- Code references & dependencies: `docs/CODE_REFERENCES.md`
- Ecosystem overview (all NovemPOS repos & relations): `../docs/ECOSYSTEM_MAP.md`

## Stack & versions

- C# / .NET Framework 4.8, WinForms, old-style csproj. **No NuGet** — all dependencies are checked-in DLLs (`libs/` = CAS COM interop assemblies, `runtime/` = CasScale.ocx + CAS native DLLs).
- One solution, two assemblies: `CasScaleSender.csproj` → GUI `NovemposTerazi.exe`; `cli/NovemposCli.csproj` → console `novempos-cli.exe`. The CLI compiles the GUI's root `.cs` files via MSBuild `<Compile Include="..\X.cs"><Link>` (shared core).
- Installer: Inno Setup 6 (`installer/installer.iss`). Current version: 1.7.0.

## Commands

```
msbuild CasScaleSender.sln /p:Configuration=Release /p:Platform=x86
```

- Run from a VS Developer Command Prompt (or use the full MSBuild.exe path). Do NOT use `dotnet build` — .NET Framework 4.8 + OCX interop.
- Outputs: `bin\Release\NovemposTerazi.exe` and `cli\bin\Release\novempos-cli.exe` (CAS runtime files are auto-copied next to the GUI exe).
- Installer package: build Release first, then `ISCC.exe installer\installer.iss` (output in `installer\Output\`).
- First run on a machine: register the OCX once by running `register.bat` (next to the built exe, or `runtime\register.bat`) as administrator — the installer does this automatically. `receive` and `print` work without the OCX; `send` does not.
- CLI smoke test: `cli\bin\Release\novempos-cli.exe help`
- No test or lint tooling exists.

## Test

- No physical CAS scale needed: run the TCP emulator, `python tools/cas_scale_emulator.py [--port 20304 --http 8081]`.
- Point the app/CLI's scale IP at `127.0.0.1:20304`; the emulator's live PLU store is viewable at `http://127.0.0.1:8081`.
- Supported protocol: R02F read / W02A OCX write + ACK / C43F13 delete — GONDER/AL/SIL and full-replace flows are tested against it.

## Code conventions (observed)

- **Row contract:** all PLU data flows as `List<Dictionary<string,string>>` with `StringComparer.OrdinalIgnoreCase` keys named after the Excel headers (`"PLU No"`, `"Name"`, `"Price"`, ... — full list in `PluReader.Columns`). Excel, JSON, scale-read and print paths all share this shape (see `docs/CODE_REFERENCES.md`).
- Pre-C#6 style throughout: no string interpolation, no `nameof`, `out` variables declared before use. Match it.
- Comments and all UI/CLI strings are Turkish in plain ASCII (no diacritics: "Baglanti", "gonder"). Keep new user-facing text in that style.
- Error handling: user-facing failures go to the GUI status list (`MainForm.Info/Warn`) or to CLI stderr + exit code (0 = ok, 1 = error, 2 = bad input, 3 = OCX init failed, 4 = send had failures — `cli/Program.cs`). Best-effort operations (settings save, disconnect, icon load) swallow exceptions with empty `catch`.
- Settings: plain `key=value` lines in `%APPDATA%\Novempos\ayarlar.txt` (`AppSettings.cs`); multi-scale entries as `scaleN.*` keys (max 10, `AppSettings.MaxScales`). Legacy flat keys (`ip=`, `port=`, ...) are mirrored to `Scales[0]` on save because the CLI uses them as defaults.
- Sending is an OCX event-driven state machine (`SendDataString` → wait for `RecvEventString`, 10 s timeout). It exists twice: `MainForm.cs` (GUI) and `cli/ScaleHost.cs` (console; OCX hosted on an off-screen form because it needs a real HWND + message pump). Reading deliberately bypasses the OCX (`CasNetReader.cs`, raw TCP) because the OCX's ReadPLU got no response from these scales.

## Critical warnings

- **x86 only.** CasScale.ocx and the CAS DLLs are 32-bit; both projects pin `PlatformTarget=x86` + `Prefer32Bit`. Never switch to AnyCPU/x64. The only solution configs are `Debug|x86` and `Release|x86`.
- **Shared-core-via-link:** editing any root `.cs` file changes BOTH binaries — always consider the CLI (and its POS callers) when touching them. A new shared file must be added to both `.csproj` files.
- `PluBuilder.BuildV06` and `PluReader.Parse` are exact mirrors of the CAS V06 fixed-width record (field order/widths from the official CAS sample). Change them only together.
- Do not modify anything in `libs/` or `runtime/` (vendor CAS binaries + `DATAOPTION.INI`). The `runtime/` file list is duplicated as csproj `<Content>` items and in `installer/installer.iss` `[Files]`.
- Version is declared in 3 places; bump together: `Properties/AssemblyInfo.cs`, the hardcoded string in `cli/Program.cs` (`version` command), `installer/installer.iss` (`AppVersion`).
- `PluReader.cs` assumes the single-byte `windows-1254` encoding (byte width == char width). Multi-byte encodings would break its parsing.

## Ecosystem links

- No API/network connection to other NovemPOS repos. Integration is process-level: the NovemPOS Flutter POS invokes `novempos-cli send/receive/print --json <file>`. The CLI flags, the JSON row schema (`JsonPluIo.cs`: array of objects keyed like `PluReader.Columns`) and the exit codes are the public contract.
- Hardware: CAS CL3000/CL5000 scales over TCP, default port 20304.

**Cross-repo impact — if you change this here, check there:**
- The CLI public contract (flags of `novempos-cli send/receive/print --json`, the JSON row schema, exit codes 0–4) is consumed by kasapos-terminal-v2 (`../kasapos-terminal-v2` in this workspace — the "Flutter POS" referred to above; consumer code: `lib/core/services/cas_scale/cas_scale_service.dart`) — any change needs coordination there; novempos-backend is unaffected.

## Working style

**Think before coding.** State assumptions. If the prompt has multiple interpretations, surface them. Push back on overcomplication.

**Simplicity first.** Minimum solving the problem. No speculative abstractions, flexibility, or error-handling for impossible cases.

**Surgical changes.** Touch only what's needed. Don't refactor adjacent code. Match existing style. Remove only orphans your change created.

**Verifiable goals.** Transform tasks into checkable outcomes — "spec parses + lint passes" beats "looks good." For multi-step work, declare the plan briefly with a verify check per step.

## Source of truth (SSOT)

**One place, one truth — applies to docs AND code.** Avoid re-declaring the same thing in multiple files; one source, all consumers import. Fixing or renaming in one place propagates everywhere.

**Plans are authoritative for decisions.** Scope, ownership, deferred work, code-review carryovers. Don't record in commit messages or scattered TODO comments.

**Fixtures auto-generated from YAML.** Hand-edited fixture files are forbidden once the YAML SSOT is in place.
