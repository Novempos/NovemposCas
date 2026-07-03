; ==========================================================================
;  Novempos Terazi PLU Gonderici - Kurulum Sihirbazi (Inno Setup 6)
;  Derlemek icin:  ISCC.exe installer.iss   (veya bu dosyaya cift tik)
;  OCX'i kurulumda regsvr32 ile kaydeder, kaldirinca geri alir.
; ==========================================================================

#define AppName      "Novempos Terazi PLU Gonderici"
#define AppShortName "Novempos Terazi"
#define AppVersion   "1.6.0"
#define AppPublisher "Novempos"
#define AppExe       "NovemposTerazi.exe"
#define CliExe       "novempos-cli.exe"
#define SrcRel       "..\bin\Release"
#define CliSrc       "..\cli\bin\Release"

[Setup]
; Ayni AppId = ayni urun (guncellemelerde ustune kurar). Degistirmeyin.
AppId={{A7F3C2E1-9B4D-4E6A-8C10-2F6A1B3C4D5E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Novempos\Terazi PLU Gonderici
DefaultGroupName=Novempos
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=NovemposTerazi_Kurulum_{#AppVersion}
SetupIconFile=..\novempos.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; OCX 32-bit; kurulum da 32-bit kalmali (Program Files x86 + 32-bit regsvr).
ArchitecturesInstallIn64BitMode=
; OCX kaydi ve Program Files yazimi icin yonetici sart.
PrivilegesRequired=admin
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

; Kurulumda hangi parcalarin kurulacagi secilir (coklu secim).
[Types]
Name: "full";   Description: "Tam kurulum (GUI + Terminal)"
Name: "custom"; Description: "Ozel secim"; Flags: iscustom

[Components]
Name: "gui"; Description: "Masaustu uygulamasi (GUI - pencere)";       Types: full
Name: "cli"; Description: "Terminal uygulamasi (novempos-cli, komut satiri)"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Components: gui

[Files]
; --- GUI uygulamasi (yalniz 'gui' secilirse) ---
Source: "{#SrcRel}\{#AppExe}";                  DestDir: "{app}"; Flags: ignoreversion; Components: gui
Source: "{#SrcRel}\{#AppExe}.config";           DestDir: "{app}"; Flags: ignoreversion; Components: gui
; --- Terminal uygulamasi (yalniz 'cli' secilirse) ---
Source: "{#CliSrc}\{#CliExe}";                  DestDir: "{app}"; Flags: ignoreversion; Components: cli
; --- Ortak COM interop (her iki uygulama da kullanir) ---
Source: "{#SrcRel}\AxInterop.CASSCALELib.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\Interop.CASSCALELib.dll";    DestDir: "{app}"; Flags: ignoreversion
; --- CAS calisma kutuphaneleri ---
Source: "{#SrcRel}\CASPRTC.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\CLInterpreter.dll";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\CASTCPIP.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\CASSERIAL.dll";      DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\CASFTP.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcRel}\DATAOPTION.INI";     DestDir: "{app}"; Flags: ignoreversion
; --- OCX: kurulumda kaydedilir (regsvr32), kaldirinca geri alinir ---
Source: "{#SrcRel}\CasScale.ocx";       DestDir: "{app}"; Flags: ignoreversion regserver 32bit
; --- Dokumantasyon ---
Source: "..\OKUBENI.txt";               DestDir: "{app}"; Flags: ignoreversion

[Icons]
; GUI kisayollari
Name: "{group}\{#AppName}";      Filename: "{app}\{#AppExe}"; Components: gui
Name: "{autodesktop}\{#AppShortName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Components: gui
; Terminal kisayolu: uygulama klasorunde komut istemi acar, yardimi gosterir
Name: "{group}\Novempos Terminal (komut satiri)"; Filename: "{cmd}"; Parameters: "/K novempos-cli help"; WorkingDir: "{app}"; Components: cli
; Kaldirma kisayolu (her zaman)
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppShortName}}"; Flags: nowait postinstall skipifsilent; Components: gui
; OKUBENI: varsayilan ISARETSIZ gelir; isteyen kutucugu isaretleyip acar.
Filename: "{app}\OKUBENI.txt"; Description: "OKUBENI dosyasini goster"; Flags: postinstall shellexec skipifsilent unchecked

[Code]
// .NET Framework 4.8 var mi? (Windows 11'de yerlesik; yine de uyaralim.)
function InitializeSetup(): Boolean;
var
  release: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
       'Release', release) then
  begin
    if release < 528040 then
    begin
      if MsgBox('Bu program icin .NET Framework 4.8 gereklidir ve sistemde bulunamadi.' + #13#10 +
                'Yine de kuruluma devam edilsin mi? (Program acilmayabilir.)',
                mbConfirmation, MB_YESNO) = IDNO then
        Result := False;
    end;
  end;
end;
