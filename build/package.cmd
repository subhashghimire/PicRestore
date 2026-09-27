@echo off
rem Double-click (or run) to build PicRestore's installer and portable zip into the artifacts folder.
rem Any arguments are passed through, e.g.  build\package.cmd -Mode FrameworkDependent -SkipTests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed - see the messages above.
)
if "%~1"=="" pause
