@echo off
REM Thin wrapper around build-complete.ps1 (PowerShell 7 / pwsh, same as CI).
REM
REM Usage:
REM   build-complete.bat [Release|Debug] [--exe-only] [any build-complete.ps1 parameters]
REM Examples:
REM   build-complete.bat                          (Release exe + MSI)
REM   build-complete.bat Debug --exe-only
REM   build-complete.bat -Portable -Runtime win-x64 -UpdateChannel Beta

setlocal

REM Capture before parsing: shift also shifts %0.
set "SCRIPT=%~dp0build-complete.ps1"
set "ARGS="
:parse_args
if "%~1"=="" goto :run_build
if /i "%~1"=="Debug"      (set "ARGS=%ARGS% -Configuration Debug"   & shift & goto :parse_args)
if /i "%~1"=="Release"    (set "ARGS=%ARGS% -Configuration Release" & shift & goto :parse_args)
if /i "%~1"=="--exe-only" (set "ARGS=%ARGS% -ExeOnly"               & shift & goto :parse_args)
REM A PowerShell parameter (-Name): pass it through with its value, if it has one.
set "ARG=%~1"
set "NEXT=%~2"
set "ARGS=%ARGS% %1"
shift
if not "%ARG:~0,1%"=="-" goto :parse_args
if "%NEXT%"=="" goto :parse_args
if "%NEXT:~0,1%"=="-" goto :parse_args
set "ARGS=%ARGS% %1"
shift
goto :parse_args

:run_build
where pwsh >nul 2>nul
if errorlevel 1 (
    echo ERROR: PowerShell 7 ^(pwsh^) is required. Install it with: winget install Microsoft.PowerShell
    exit /b 1
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %ARGS%
exit /b %errorlevel%
