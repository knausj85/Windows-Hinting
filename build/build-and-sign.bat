@echo off
REM Build and Sign Windows-Hinting - Batch wrapper for PowerShell script
REM Usage: build-and-sign.bat [Release|Debug] [--regen-cert] [--skip-signing]

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_SCRIPT=%SCRIPT_DIR%build-and-sign.ps1"

REM Default parameters
set "Configuration=Release"
set "RegenerateCert="
set "SkipSigning="

REM Parse command line arguments
:parse_args
if "%1"=="" goto :run_build
if /i "%1"=="Debug" (
    set "Configuration=Debug"
    shift
    goto :parse_args
)
if /i "%1"=="Release" (
    set "Configuration=Release"
    shift
    goto :parse_args
)
if /i "%1"=="--regen-cert" (
    set "RegenerateCert=-RegenerateCert"
    shift
    goto :parse_args
)
if /i "%1"=="--skip-signing" (
    set "SkipSigning=-SkipSigning"
    shift
    goto :parse_args
)
shift
goto :parse_args

:run_build
echo Building Windows-Hinting...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!POWERSHELL_SCRIPT!" -Configuration !Configuration! !RegenerateCert! !SkipSigning!

if %errorlevel% neq 0 (
    echo.
    echo Build failed with exit code %errorlevel%
    exit /b %errorlevel%
)

exit /b 0
