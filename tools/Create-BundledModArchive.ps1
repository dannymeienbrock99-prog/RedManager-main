[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Source = Join-Path $Root 'bundled-mods\CrazyBatto.SotfDeathCounter'
$ResourceDirectory = Join-Path $Root 'src-tauri\resources'
$Destination = Join-Path $ResourceDirectory 'CrazyBatto.SotfDeathCounter-source.zip'

if (-not (Test-Path (Join-Path $Source 'CrazyBatto.SotfDeathCounter.csproj'))) {
    throw "Mod source project was not found: $Source"
}

New-Item -ItemType Directory -Path $ResourceDirectory -Force | Out-Null
if (Test-Path $Destination) { Remove-Item $Destination -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::Open($Destination, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $files = Get-ChildItem -Path $Source -File -Recurse |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|ReleaseBuild|build-output)[\\/]' -and
            $_.Extension -notin @('.dll', '.pdb', '.exe')
        } |
        Sort-Object FullName

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($Source.Length).TrimStart([char[]]'\/') -replace '\\', '/'
        $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        # Stable timestamp keeps archive diffs deterministic across machines.
        $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-08-18T00:00:00+00:00')
        $input = [System.IO.File]::OpenRead($file.FullName)
        $output = $entry.Open()
        try { $input.CopyTo($output) }
        finally { $output.Dispose(); $input.Dispose() }
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $Destination).Hash.ToLowerInvariant()
Set-Content -Encoding Ascii -Path "$Destination.sha256" -Value "$hash  CrazyBatto.SotfDeathCounter-source.zip"
Write-Host "Created: $Destination"
Write-Host "SHA-256: $hash"
