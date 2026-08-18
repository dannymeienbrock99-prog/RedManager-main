[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$DllPath = Join-Path $PayloadDirectory 'CrazyBatto.SotfDeathCounter.dll'
$ManifestPath = Join-Path $PayloadDirectory 'manifest.json'
if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Safe Mode DLL is missing: $DllPath"
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Safe Mode manifest is missing: $ManifestPath"
}

$Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($Manifest.version -ne '0.3.3') {
    throw "Expected manifest version 0.3.3 but found $($Manifest.version)."
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$SettingsPath = Join-Path $PayloadDirectory 'settings-safe.json'
@'
{
  "EnableObsOverlay": true,
  "OverlayPort": 19447,
  "CountKnockdowns": false,
  "ShowOfflinePlayers": false,
  "UseLifetimeDeaths": false,
  "SafeMode": true,
  "ScanIntervalMilliseconds": 2500,
  "WorldScanIntervalMilliseconds": 15000,
  "EnableRuntimeHooks": false,
  "WriteDiscoveryDiagnostics": false
}
'@ | Set-Content -LiteralPath $SettingsPath -Encoding utf8

$InstallerPath = Join-Path $OutputDirectory 'CrazyBatto_SOTF_DeathCounter_0.3.3_SafeMode_Setup.exe'
$NsisScriptPath = Join-Path $env:TEMP 'CrazyBattoSafeModeInstaller.nsi'

$NsisTemplate = @'
Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

Name "CrazyBatto SOTF Death Counter 0.3.3 Safe Mode"
OutFile "__OUTPUT__"
InstallDir "C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "Crazy_Batto – Safe Mode"

!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_NOAUTOCLOSE
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "German"

Function .onInit
  ReadRegStr $0 HKCU "Software\Valve\Steam" "SteamPath"
  ${If} $0 != ""
    StrCpy $1 "$0\steamapps\common\Sons Of The Forest"
    ${If} ${FileExists} "$1\SonsOfTheForest.exe"
      StrCpy $INSTDIR $1
      Return
    ${EndIf}
  ${EndIf}

  ReadRegStr $0 HKLM "Software\WOW6432Node\Valve\Steam" "InstallPath"
  ${If} $0 != ""
    StrCpy $1 "$0\steamapps\common\Sons Of The Forest"
    ${If} ${FileExists} "$1\SonsOfTheForest.exe"
      StrCpy $INSTDIR $1
    ${EndIf}
  ${EndIf}
FunctionEnd

Function .onVerifyInstDir
  ${IfNot} ${FileExists} "$INSTDIR\SonsOfTheForest.exe"
    MessageBox MB_ICONSTOP|MB_OK "In diesem Ordner wurde SonsOfTheForest.exe nicht gefunden.$\r$\n$\r$\nWähle den Hauptordner von Sons of the Forest aus."
    Abort
  ${EndIf}
FunctionEnd

Section "Safe Mode Death Counter installieren" SEC01
  SetShellVarContext current

  ${IfNot} ${FileExists} "$INSTDIR\_RedLoader\net6\SonsSdk.dll"
    MessageBox MB_ICONSTOP|MB_OK "RedLoader wurde in diesem Spielordner nicht vollständig gefunden.$\r$\n$\r$\nInstalliere RedLoader, starte Sons of the Forest einmal bis ins Hauptmenü und führe dieses Setup danach erneut aus."
    Abort
  ${EndIf}

  CreateDirectory "$INSTDIR\Mods"
  CreateDirectory "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter"

  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.dll"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.disabled"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.dll.backup"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.dll.new"

  SetOutPath "$INSTDIR\Mods"
  File /oname=CrazyBatto.SotfDeathCounter.dll "__PAYLOAD__\CrazyBatto.SotfDeathCounter.dll"

  SetOutPath "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter"
  File /oname=manifest.json "__PAYLOAD__\manifest.json"
  WriteUninstaller "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter\Uninstall_CrazyBatto_DeathCounter.exe"

  CreateDirectory "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter"
  Delete "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter\settings-before-safe-mode.json"
  ${If} ${FileExists} "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter\settings.json"
    Rename "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter\settings.json" "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter\settings-before-safe-mode.json"
  ${EndIf}
  SetOutPath "$LOCALAPPDATA\Crazy_Batto\SOTFDeathCounter"
  File /oname=settings.json "__PAYLOAD__\settings-safe.json"

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "DisplayName" "CrazyBatto SOTF Death Counter 0.3.3 Safe Mode"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "DisplayVersion" "0.3.3"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "Publisher" "Crazy_Batto"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "InstallLocation" "$INSTDIR\Mods"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "UninstallString" '"$INSTDIR\Mods\CrazyBatto.SotfDeathCounter\Uninstall_CrazyBatto_DeathCounter.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter" "NoRepair" 1

  MessageBox MB_ICONINFORMATION|MB_OK "Safe Mode 0.3.3 wurde installiert.$\r$\n$\r$\nDie alte aggressive DLL und die bisherigen Mod-Einstellungen wurden sicher ersetzt."
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.dll"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter.disabled"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter\manifest.json"
  Delete /REBOOTOK "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter\Uninstall_CrazyBatto_DeathCounter.exe"
  RMDir "$INSTDIR\Mods\CrazyBatto.SotfDeathCounter"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CrazyBattoSOTFDeathCounter"
SectionEnd
'@

$NsisScript = $NsisTemplate.Replace('__OUTPUT__', $InstallerPath).Replace('__PAYLOAD__', $PayloadDirectory)
$NsisScript | Set-Content -LiteralPath $NsisScriptPath -Encoding utf8

$MakeNsis = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if (-not $MakeNsis) {
    $MakeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe'
}
if (-not (Test-Path -LiteralPath $MakeNsis -PathType Leaf)) {
    throw 'makensis.exe was not found after the NSIS installation.'
}

& $MakeNsis $NsisScriptPath
if ($LASTEXITCODE -ne 0) {
    throw "NSIS failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw 'The Safe Mode installer was not created.'
}

$ReadyRoot = Join-Path $OutputDirectory 'ReadyMod'
$ReadyMods = Join-Path $ReadyRoot 'Mods'
$ReadyManifest = Join-Path $ReadyMods 'CrazyBatto.SotfDeathCounter'
New-Item -ItemType Directory -Path $ReadyManifest -Force | Out-Null
Copy-Item -LiteralPath $DllPath -Destination $ReadyMods -Force
Copy-Item -LiteralPath $ManifestPath -Destination $ReadyManifest -Force
Copy-Item -LiteralPath $SettingsPath -Destination $ReadyRoot -Force
@'
CrazyBatto SOTF Death Counter 0.3.3 Safe Mode

EMPFOHLEN:
1. Sons of the Forest vollständig schließen.
2. CrazyBatto_SOTF_DeathCounter_0.3.3_SafeMode_Setup.exe starten.
3. Den Hauptordner mit SonsOfTheForest.exe auswählen.

Safe Mode deaktiviert dynamische Harmony-Todeshooks und den Vollscan sämtlicher MonoBehaviour-Objekte.
OBS-Browserquelle: http://127.0.0.1:19447/overlay
'@ | Set-Content -LiteralPath (Join-Path $ReadyRoot 'START_HIER.txt') -Encoding utf8

$ReadyZip = Join-Path $OutputDirectory 'CrazyBatto_SOTF_DeathCounter_0.3.3_SafeMode_READY.zip'
Compress-Archive -Path (Join-Path $ReadyRoot '*') -DestinationPath $ReadyZip -CompressionLevel Optimal -Force
Remove-Item -LiteralPath $ReadyRoot -Recurse -Force

Get-ChildItem -LiteralPath $OutputDirectory -File |
    Where-Object Name -notin @('SHA256SUMS.txt', 'build-status.json', 'run-id.txt') |
    ForEach-Object {
        $Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$Hash  $($_.Name)"
    } |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii

[ordered]@{
    success = $true
    modVersion = '0.3.3'
    safeMode = $true
    sourceCommit = $env:GITHUB_SHA
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    installer = [IO.Path]::GetFileName($InstallerPath)
    readyZip = [IO.Path]::GetFileName($ReadyZip)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-status.json') -Encoding utf8

if ($env:GITHUB_RUN_ID) {
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'run-id.txt') -Value $env:GITHUB_RUN_ID -Encoding ascii
}

Write-Host "Created Safe Mode installer: $InstallerPath"
Write-Host "Created Ready ZIP: $ReadyZip"
