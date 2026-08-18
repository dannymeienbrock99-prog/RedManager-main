[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host 'Validating repository structure...'
$required = @(
    'RedManager.sln',
    'src/CrazyBatto.RedManager/CrazyBatto.RedManager.csproj',
    'src/CrazyBatto.RedManager/App.xaml',
    'src/CrazyBatto.RedManager/MainWindow.xaml',
    'src/CrazyBatto.RedManager/Core.cs',
    'tests/CrazyBatto.RedManager.Tests/CrazyBatto.RedManager.Tests.csproj'
)
foreach ($path in $required) {
    if (-not (Test-Path $path)) {
        throw "Required file missing: $path"
    }
}

Write-Host 'Restoring...'
dotnet restore RedManager.sln

Write-Host 'Compiling...'
dotnet build RedManager.sln -c Release --no-restore

Write-Host 'Running tests...'
dotnet test tests/CrazyBatto.RedManager.Tests/CrazyBatto.RedManager.Tests.csproj -c Release --no-build

Write-Host 'Validation completed successfully.'
