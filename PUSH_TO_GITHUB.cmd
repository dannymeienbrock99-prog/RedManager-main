@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where git >nul 2>&1 || (echo FEHLER: Git wurde nicht gefunden.& exit /b 1)

if not exist ".git" (
  git init
  if errorlevel 1 exit /b 1
)

git branch -M main

git remote get-url origin >nul 2>&1
if errorlevel 1 (
  git remote add origin git@github.com:dannymeienbrock99-prog/RedManager-main.git
) else (
  git remote set-url origin git@github.com:dannymeienbrock99-prog/RedManager-main.git
)

git add -A

git diff --cached --quiet
if errorlevel 1 (
  git -c user.name="Crazy_Batto" -c user.email="dannymeienbrock99-prog@users.noreply.github.com" commit -m "Build CrazyBatto RedManager 1.2.0 with SOTF death counter"
  if errorlevel 1 exit /b 1
) else (
  echo Keine neuen Aenderungen fuer einen Commit gefunden.
)

git rev-parse -q --verify "refs/tags/v1.2.0" >nul 2>&1
if errorlevel 1 (
  git -c user.name="Crazy_Batto" -c user.email="dannymeienbrock99-prog@users.noreply.github.com" tag -a v1.2.0 -m "CrazyBatto RedManager 1.2.0"
  if errorlevel 1 exit /b 1
)

echo Push nach GitHub...
git push -u origin main --follow-tags
if errorlevel 1 (
  echo.
  echo Der Push ist fehlgeschlagen. Pruefe deinen GitHub-SSH-Schluessel und die Repository-Berechtigung.
  exit /b 1
)

echo Repository erfolgreich aktualisiert.
endlocal
