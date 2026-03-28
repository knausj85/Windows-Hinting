@echo off
REM Build Complete - Executable and Installer (Default)
REM Usage: build-complete.bat [Release|Debug] [--exe-only] [--skip-signing]
REM
REM Set CERT_PASSWORD environment variable before running to provide the
REM signing certificate password (matches the GitHub Actions secret).

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_SCRIPT=%SCRIPT_DIR%build-complete.ps1"

REM Default parameters
set "Configuration=Release"
set "ExeOnly="
set "SkipSigning="
set "CertPasswordArg="

REM Pick up CERT_PASSWORD from the environment (same var name as GitHub Actions)
if defined CERT_PASSWORD (
    set "CertPasswordArg=-CertPassword !CERT_PASSWORD!"
)

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
if /i "%1"=="--exe-only" (
    set "ExeOnly=-ExeOnly"
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
echo --- Parameters (debug) ---
echo   Configuration : !Configuration!
echo   ExeOnly       : !ExeOnly!
echo   SkipSigning   : !SkipSigning!
echo   CERT_PASSWORD : !CertPasswordArg!
echo   Raw args      : %*
echo --------------------------
echo.
echo Building Windows-Hinting complete project...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "!POWERSHELL_SCRIPT!" -Configuration !Configuration! !ExeOnly! !SkipSigning! !CertPasswordArg!

if %errorlevel% neq 0 (
    echo.
    echo Build failed with exit code %errorlevel%
    exit /b %errorlevel%
)

exit /b 0
