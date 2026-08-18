[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host 'Restoring solution...'
dotnet restore RedManager.sln

Write-Host 'Building and running tests...'
dotnet build RedManager.sln -c $Configuration --no-restore
dotnet test tests/CrazyBatto.RedManager.Tests/CrazyBatto.RedManager.Tests.csproj -c $Configuration --no-build

$output = Join-Path $root "artifacts/portable-$Runtime"
Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing self-contained application for $Runtime..."
dotnet publish src/CrazyBatto.RedManager/CrazyBatto.RedManager.csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

Copy-Item README.md, LICENSE, THIRD_PARTY_NOTICES.md -Destination $output
$packageDirectory = Join-Path $root 'artifacts/package'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$archive = Join-Path $packageDirectory "CrazyBatto-RedManager-$Runtime.zip"
Compress-Archive -Path "$output/*" -DestinationPath $archive -CompressionLevel Optimal -Force

$exe = Get-ChildItem $output -Filter *.exe | Select-Object -First 1
if (-not $exe) {
    throw 'Publish produced no executable.'
}

$checksums = @(
    "$(Get-FileHash $exe.FullName -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $($exe.Name)",
    "$(Get-FileHash $archive -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $(Split-Path $archive -Leaf)"
)
$checksums | Set-Content (Join-Path $packageDirectory 'SHA256SUMS.txt') -Encoding utf8

Write-Host "Build complete: $archive"
