using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Installs an MSI update with elevation after the current app exits.
    /// Strategy: write a <c>update-msi.cmd</c> that waits for the app PID to
    /// exit, runs <c>msiexec /i</c> with verbose logging, and relaunches the
    /// newly installed exe. Launch the script elevated (<c>Verb = "runas"</c>)
    /// so the per-machine <c>msiexec</c> install succeeds even when the current
    /// app runs with UIAccess but no Administrator token.
    /// </summary>
    /// <remarks>
    /// NetSparkle has already verified the Ed25519 signature on the downloaded
    /// MSI before this runs, so the artifact is trust-verified.
    /// </remarks>
    internal sealed class MsiUpdateInstaller
    {
        private readonly ILogger _logger;

        public MsiUpdateInstaller(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Writes the helper script, launches it elevated, and returns <c>true</c>
        /// if the caller should exit now. Returns <c>false</c> if the script could
        /// not be launched (e.g. user declined UAC) so the caller can fall back to
        /// opening the release page.
        /// </summary>
        public bool InstallAndRelaunch(string downloadedMsiPath)
        {
            if (string.IsNullOrEmpty(downloadedMsiPath) || !File.Exists(downloadedMsiPath))
            {
                _logger.Warning($"MSI update: downloaded artifact missing: {downloadedMsiPath}");
                return false;
            }

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                _logger.Warning("MSI update: cannot resolve current exe path.");
                return false;
            }

            // Stage the helper script in a temp folder.
            var stagingDir = Path.Combine(Path.GetTempPath(),
                "WindowsHinting-msi-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);

            var helperPath = Path.Combine(stagingDir, "update-msi.cmd");
            var msiLogPath = Path.Combine(Path.GetTempPath(),
                $"Windows-Hinting-msi-update-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

            File.WriteAllText(helperPath, BuildHelperScript());

            var pid = Environment.ProcessId;
            var psi = new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"\"{pid}\" \"{downloadedMsiPath}\" \"{currentExe}\" \"{msiLogPath}\"",
                UseShellExecute = true,
                Verb = "runas", // Request elevation for per-machine msiexec
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = stagingDir,
            };

            _logger.Info($"MSI update: launching elevated helper script (MSI={downloadedMsiPath}, log={msiLogPath}).");

            try
            {
                Process.Start(psi);
                _logger.Info("MSI update: helper launched with elevation; exiting current process.");
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: user declined UAC prompt.
                _logger.Info($"MSI update: user declined UAC elevation ({ex.Message}).");
                MessageBox.Show(
                    "Update requires administrator permission to install.\n\n" +
                    "If you prefer not to elevate, download the portable version from the releases page.",
                    "Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                TryDelete(stagingDir);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warning($"MSI update: failed to launch elevated helper: {ex.Message}");
                TryDelete(stagingDir);
                return false;
            }
        }

        private static string BuildHelperScript()
        {
            // Args:
            //   %1 = PID of the app we're waiting on
            //   %2 = full path to the MSI (NetSparkle download location)
            //   %3 = full path to the installed exe (for relaunch)
            //   %4 = full path to the msiexec verbose log
            return
@"@echo off
setlocal
set PID=%~1
set MSI_PATH=%~2
set INSTALLED_EXE=%~3
set LOG_PATH=%~4

:wait
tasklist /FI ""PID eq %PID%"" 2>nul | find ""%PID%"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)

rem Run msiexec with /qb (basic UI, no modal dialogs), /norestart (we relaunch),
rem and /l*v for verbose diagnostic logging. Exit code 0 = success,
rem 3010 = success but reboot required (we ignore and relaunch anyway).
msiexec /i ""%MSI_PATH%"" /qb /norestart /l*v ""%LOG_PATH%""
set EXIT_CODE=%ERRORLEVEL%

if %EXIT_CODE% EQU 0 goto launch
if %EXIT_CODE% EQU 3010 goto launch

rem Install failed; open the log file for diagnostics.
echo Windows-Hinting MSI update failed (exit code %EXIT_CODE%).
echo Opening log: %LOG_PATH%
notepad.exe ""%LOG_PATH%""
goto cleanup

:launch
if exist ""%INSTALLED_EXE%"" (
    start """" ""%INSTALLED_EXE%""
) else (
    echo Warning: installed exe not found at ""%INSTALLED_EXE%"".
)

:cleanup
rem Best-effort: delete our staging folder on exit.
(goto) 2>nul & del ""%~f0"" & rmdir /s /q ""%~dp0""
";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
