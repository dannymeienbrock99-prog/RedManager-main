@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo [1/5] Voraussetzungen pruefen...
where node >nul 2>&1 || (echo FEHLER: Node.js wurde nicht gefunden.& exit /b 1)
where npm >nul 2>&1 || (echo FEHLER: npm wurde nicht gefunden.& exit /b 1)
where cargo >nul 2>&1 || (echo FEHLER: Rust/Cargo wurde nicht gefunden.& exit /b 1)
where powershell >nul 2>&1 || (echo FEHLER: PowerShell wurde nicht gefunden.& exit /b 1)

echo [2/5] Gebuendelte Modquelle aktualisieren...
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\tools\Create-BundledModArchive.ps1"
if errorlevel 1 exit /b 1

echo [3/5] npm-Abhaengigkeiten installieren...
call npm ci
if errorlevel 1 exit /b 1

echo [4/5] Svelte/TypeScript pruefen...
call npm run check
if errorlevel 1 exit /b 1

echo [5/5] Windows-Bundle erstellen...
call npm run tauri -- build
if errorlevel 1 exit /b 1

echo.
echo Fertig. Installer und Bundles liegen unter:
echo %CD%\src-tauri\target\release\bundle
endlocal
