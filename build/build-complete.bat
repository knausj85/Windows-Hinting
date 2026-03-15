@echo off
REM Build Complete - Both Executable and Installer
REM Usage: build-complete.bat [Release|Debug] [--installer] [--skip-signing]

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_SCRIPT=%SCRIPT_DIR%build-complete.ps1"

REM Default parameters
set "Configuration=Release"
set "Installer="
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
if /i "%1"=="--installer" (
    set "Installer=-Installer"
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
echo Building HintOverlay complete project...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!POWERSHELL_SCRIPT!" -Configuration !Configuration! !Installer! !SkipSigning!

if %errorlevel% neq 0 (
    echo.
    echo Build failed with exit code %errorlevel%
    exit /b %errorlevel%
)

exit /b 0
