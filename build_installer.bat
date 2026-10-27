@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo [1/2] Building QuickNotch Release (win-x64)...
dotnet publish -c Release -r win-x64 -o publish_v2
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to build QuickNotch.exe!
    pause
    exit /b 1
)

echo.
echo [2/2] Building QuickNotch_Setup.exe with Inno Setup...

set "ISCC_PATH="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if "%ISCC_PATH%"=="" (
    echo.
    echo [!] Inno Setup 6 is not installed yet.
    echo Run this command in PowerShell/CMD to install it automatically:
    echo.
    echo     winget install JRSoftware.InnoSetup
    echo.
    echo Or download from: https://jrsoftware.org/isdl.php
    echo Then run build_installer.bat again!
    pause
    exit /b 1
)

"%ISCC_PATH%" setup.iss
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to compile installer!
    pause
    exit /b 1
)

echo.
echo ========================================================
echo DONE! Installer created at: installer\QuickNotch_Setup.exe
echo ========================================================
explorer "%~dp0installer"
pause

