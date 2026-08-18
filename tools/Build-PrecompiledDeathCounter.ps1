[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Continue'
$Root = Split-Path -Parent $PSScriptRoot
$Work = Join-Path $env:TEMP 'CrazyBattoRedManager\PrecompiledDeathCounter'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $Work, $OutputDirectory -Force | Out-Null

$LogPath = Join-Path $OutputDirectory 'build.log'
$Success = $true
$Failure = ''

function Write-Log {
    param([string]$Message)
    $Line = "[$([DateTime]::UtcNow.ToString('u'))] $Message"
    Write-Host $Line
    $Line | Add-Content -LiteralPath $LogPath -Encoding utf8
}

function Build-UnityStub {
    param(
        [string]$ProjectName,
        [string]$AssemblyName,
        [string]$Source,
        [string]$StubRoot,
        [string]$Destination
    )

    $ProjectDirectory = Join-Path $StubRoot $ProjectName
    New-Item -ItemType Directory -Path $ProjectDirectory -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>$AssemblyName</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>10.0</LangVersion>
    <AssemblyVersion>0.0.0.0</AssemblyVersion>
    <FileVersion>0.0.0.0</FileVersion>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $ProjectDirectory "$ProjectName.csproj") -Encoding utf8
    $Source | Set-Content -LiteralPath (Join-Path $ProjectDirectory 'Stub.cs') -Encoding utf8

    & dotnet build (Join-Path $ProjectDirectory "$ProjectName.csproj") `
        -c Release --nologo --output (Join-Path $ProjectDirectory 'out') 2>&1 |
        Tee-Object -FilePath $LogPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "Unity stub build failed: $ProjectName"
    }

    Copy-Item `
        -LiteralPath (Join-Path $ProjectDirectory "out\$AssemblyName.dll") `
        -Destination (Join-Path $Destination "$AssemblyName.dll") `
        -Force
}

Write-Log 'Starting precompiled CrazyBatto SOTF Death Counter 0.3.2 build.'

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK was not found.'
    }

    $ReleaseZip = Join-Path $Work 'Redloader.zip'
    $ReleaseDirectory = Join-Path $Work 'Redloader'
    Write-Log 'Downloading official RedLoader 0.8.6 release.'
    Invoke-WebRequest `
        -Uri 'https://github.com/ToniMacaroni/RedLoader/releases/download/0.8.6/Redloader.zip' `
        -OutFile $ReleaseZip
    Expand-Archive -LiteralPath $ReleaseZip -DestinationPath $ReleaseDirectory -Force

    $Net6Directory = Join-Path $ReleaseDirectory '_Redloader\net6'
    foreach ($Required in @('RedLoader.dll', 'SonsSdk.dll', '0Harmony.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Net6Directory $Required))) {
            throw "Official RedLoader release is missing $Required"
        }
    }
    Write-Log 'Official RedLoader SDK assemblies verified.'

    $FakeGame = Join-Path $Work 'FakeGame'
    $UnityDirectory = Join-Path $FakeGame '_RedLoader\unity-libs'
    $StubRoot = Join-Path $Work 'UnityStubs'
    New-Item -ItemType Directory -Path $UnityDirectory, $StubRoot -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $FakeGame 'SonsOfTheForest.exe') -Force | Out-Null

    Build-UnityStub `
        -ProjectName 'UnityCoreStub' `
        -AssemblyName 'UnityEngine.CoreModule' `
        -StubRoot $StubRoot `
        -Destination $UnityDirectory `
        -Source @'
namespace UnityEngine
{
    public class Object
    {
        public static T[] FindObjectsOfType<T>() where T : Object => System.Array.Empty<T>();
        public string name { get; set; } = string.Empty;
        public int GetInstanceID() => 0;
    }

    public class GameObject : Object { }

    public class Component : Object
    {
        public Transform transform => null!;
        public GameObject gameObject => null!;
    }

    public class MonoBehaviour : Component { }

    public class Transform : Component
    {
        public Transform root => null!;
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => System.Array.Empty<T>();
    }
}
'@

    Build-UnityStub `
        -ProjectName 'UnityFacadeStub' `
        -AssemblyName 'UnityEngine' `
        -StubRoot $StubRoot `
        -Destination $UnityDirectory `
        -Source @'
namespace UnityEngine;
public static class FacadePlaceholder { }
'@

    $ModProject = Join-Path $Root 'bundled-mods\CrazyBatto.SotfDeathCounter\CrazyBatto.SotfDeathCounter.csproj'
    $ModOutput = Join-Path $Work 'mod-output'
    Write-Log 'Compiling against the official RedLoader and SonsSdk assemblies.'
    & dotnet build $ModProject -c Release --nologo --output $ModOutput `
        "-p:GameDir=$FakeGame" `
        "-p:RedLoaderRoot=$(Join-Path $FakeGame '_RedLoader')" `
        "-p:RedLoaderNet6Dir=$Net6Directory" `
        "-p:UnityReferenceDir=$UnityDirectory" 2>&1 |
        Tee-Object -FilePath $LogPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "Death-counter compilation failed with exit code $LASTEXITCODE"
    }

    $Dll = Join-Path $ModOutput 'CrazyBatto.SotfDeathCounter.dll'
    if (-not (Test-Path -LiteralPath $Dll)) {
        throw 'Compiled Death Counter DLL was not created.'
    }

    Copy-Item -LiteralPath $Dll -Destination $OutputDirectory -Force
    $Pdb = Join-Path $ModOutput 'CrazyBatto.SotfDeathCounter.pdb'
    if (Test-Path -LiteralPath $Pdb) {
        Copy-Item -LiteralPath $Pdb -Destination $OutputDirectory -Force
    }
    Copy-Item `
        -LiteralPath (Join-Path $Root 'bundled-mods\CrazyBatto.SotfDeathCounter\manifest.json') `
        -Destination $OutputDirectory `
        -Force

    $Hash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'CrazyBatto.SotfDeathCounter.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    "$Hash  CrazyBatto.SotfDeathCounter.dll" |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'DLL-SHA256.txt') -Encoding ascii
    Write-Log "Precompiled DLL created successfully: $Hash"
}
catch {
    $Success = $false
    $Failure = $_.Exception.Message
    Write-Log "ERROR: $Failure"
}

[ordered]@{
    success = $Success
    modVersion = '0.3.2'
    redLoaderVersion = '0.8.6'
    sourceCommit = $env:GITHUB_SHA
    runId = $env:GITHUB_RUN_ID
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    error = $Failure
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-status.json') -Encoding utf8

if ($Success) {
    Write-Log 'FINAL RESULT: SUCCESS'
}
else {
    Write-Log 'FINAL RESULT: FAILED'
}
