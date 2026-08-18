[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$GameDirectory
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ProjectDirectory = Join-Path $Root 'bundled-mods\CrazyBatto.SotfDeathCounter'
$Project = Join-Path $ProjectDirectory 'CrazyBatto.SotfDeathCounter.csproj'
$AssemblyName = 'CrazyBatto.SotfDeathCounter'

function Find-SotfGameDirectory {
    $steamRoots = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in @(
        (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath,
        (Get-ItemProperty -Path 'HKLM:\Software\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath,
        (Get-ItemProperty -Path 'HKLM:\Software\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
    )) {
        if ($candidate -and -not $steamRoots.Contains($candidate)) { $steamRoots.Add($candidate) }
    }

    foreach ($steamRoot in @($steamRoots)) {
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path $libraryFile) {
            foreach ($line in Get-Content $libraryFile) {
                if ($line -match '"path"\s+"([^"]+)"') {
                    $library = $Matches[1] -replace '\\\\', '\'
                    if (-not $steamRoots.Contains($library)) { $steamRoots.Add($library) }
                }
            }
        }
    }

    foreach ($root in $steamRoots) {
        $manifest = Join-Path $root 'steamapps\appmanifest_1326470.acf'
        if (-not (Test-Path $manifest)) { continue }
        $content = Get-Content $manifest -Raw
        if ($content -match '"installdir"\s+"([^"]+)"') {
            $directory = Join-Path $root ("steamapps\common\" + $Matches[1])
            if (Test-Path (Join-Path $directory 'SonsOfTheForest.exe')) { return $directory }
        }
    }
    return $null
}

if (-not $GameDirectory) { $GameDirectory = Find-SotfGameDirectory }
if (-not $GameDirectory -or -not (Test-Path (Join-Path $GameDirectory 'SonsOfTheForest.exe'))) {
    throw 'A valid Sons of the Forest game directory was not found. Pass -GameDirectory explicitly.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found in PATH.'
}
$InstalledSdks = @(dotnet --list-sdks)
if (-not $InstalledSdks) {
    throw 'An installed .NET SDK is required to build this RedLoader mod.'
}

$RedLoaderRoot = Join-Path $GameDirectory '_RedLoader'
$RedLoaderNet6 = Join-Path $RedLoaderRoot 'net6'
foreach ($required in @('SonsSdk.dll', 'RedLoader.dll', '0Harmony.dll')) {
    if (-not (Test-Path (Join-Path $RedLoaderNet6 $required))) {
        throw "$required is missing in $RedLoaderNet6. Install RedLoader and start the game once."
    }
}

$UnityReferenceDirectory = @(
    (Join-Path $RedLoaderRoot 'Game'),
    (Join-Path $RedLoaderRoot 'unity-libs')
) | Where-Object {
    Test-Path (Join-Path $_ 'UnityEngine.CoreModule.dll')
} | Select-Object -First 1

if (-not $UnityReferenceDirectory) {
    throw 'UnityEngine.CoreModule.dll is missing. Start Sons of the Forest once with RedLoader, wait for the main menu, close the game, and retry.'
}

$Output = Join-Path $env:TEMP 'CrazyBattoRedManager\DeathCounterManualBuild'
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Path $Output -Force | Out-Null

& dotnet build $Project --configuration Release --nologo --output $Output `
    "-p:GameDir=$GameDirectory" `
    "-p:RedLoaderRoot=$RedLoaderRoot" `
    "-p:RedLoaderNet6Dir=$RedLoaderNet6" `
    "-p:UnityReferenceDir=$UnityReferenceDirectory"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$Mods = Join-Path $GameDirectory 'Mods'
New-Item -ItemType Directory -Path $Mods -Force | Out-Null
$Disabled = Join-Path $Mods "$AssemblyName.disabled"
if (Test-Path $Disabled) { Remove-Item $Disabled -Force }
Copy-Item (Join-Path $Output "$AssemblyName.dll") (Join-Path $Mods "$AssemblyName.dll") -Force
if (Test-Path (Join-Path $Output "$AssemblyName.pdb")) {
    Copy-Item (Join-Path $Output "$AssemblyName.pdb") (Join-Path $Mods "$AssemblyName.pdb") -Force
}
$ManifestDirectory = Join-Path $Mods $AssemblyName
New-Item -ItemType Directory -Path $ManifestDirectory -Force | Out-Null
Copy-Item (Join-Path $ProjectDirectory 'manifest.json') (Join-Path $ManifestDirectory 'manifest.json') -Force

Write-Host "Installed: $(Join-Path $Mods "$AssemblyName.dll")"
Write-Host 'OBS overlay: http://127.0.0.1:19447/overlay'
