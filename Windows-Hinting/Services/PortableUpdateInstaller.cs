using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using WindowsHinting.Logging;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Replaces the running portable single-file executable on exit.
    /// Strategy: verify we can write next to the exe, stage the new exe,
    /// write a tiny <c>update.cmd</c> that waits for us to exit, does
    /// <c>move /Y new current</c>, and relaunches.
    /// </summary>
    /// <remarks>
    /// NetSparkle has already verified the Ed25519 signature on the
    /// downloaded archive before this runs, so by the time we extract
    /// the exe it is trust-verified.
    /// </remarks>
    internal sealed class PortableUpdateInstaller
    {
        private readonly ILogger _logger;

        public PortableUpdateInstaller(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Preflights writability, stages the new exe, spawns the helper
        /// script, and returns <c>true</c> if the caller should exit now.
        /// Returns <c>false</c> if the update could not be applied (e.g.
        /// read-only folder) so the caller can fall back to opening the
        /// release page.
        /// </summary>
        public bool InstallAndRelaunch(string downloadedArtifactPath)
        {
            if (string.IsNullOrEmpty(downloadedArtifactPath) || !File.Exists(downloadedArtifactPath))
            {
                _logger.Warning($"Portable update: downloaded artifact missing: {downloadedArtifactPath}");
                return false;
            }

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                _logger.Warning("Portable update: cannot resolve current exe path.");
                return false;
            }

            var exeDir = Path.GetDirectoryName(currentExe)!;

            // Preflight: can we write to the folder the exe lives in?
            if (!CanWriteTo(exeDir))
            {
                _logger.Info($"Portable update: '{exeDir}' is not writable; skipping in-place update.");
                MessageBox.Show(
                    "This portable copy lives in a read-only location and cannot update itself.\n\n" +
                    "The releases page will open so you can download the new version manually.",
                    "Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            // Extract (or copy, if it's a raw exe) to a temp staging folder.
            var stagingDir = Path.Combine(Path.GetTempPath(),
                "WindowsHinting-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);

            string newExe;
            try
            {
                newExe = StageArtifact(downloadedArtifactPath, stagingDir);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Portable update: failed to stage artifact: {ex.Message}");
                TryDelete(stagingDir);
                return false;
            }

            if (!File.Exists(newExe))
            {
                _logger.Warning($"Portable update: staged exe not found at {newExe}");
                TryDelete(stagingDir);
                return false;
            }

            // Write the helper script.
            var helperPath = Path.Combine(stagingDir, "update.cmd");
            File.WriteAllText(helperPath, BuildHelperScript());

            var pid = Environment.ProcessId;
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // /c runs and exits; helper re-launches the app.
                Arguments = $"/c \"\"{helperPath}\" {pid} \"{newExe}\" \"{currentExe}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = stagingDir,
            };

            try
            {
                Process.Start(psi);
                _logger.Info("Portable update: helper launched; exiting current process.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Portable update: failed to launch helper: {ex.Message}");
                TryDelete(stagingDir);
                return false;
            }
        }

        private static string StageArtifact(string downloadedArtifactPath, string stagingDir)
        {
            // Support both raw .exe and .zip-packed artifacts.
            if (downloadedArtifactPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(downloadedArtifactPath, stagingDir, overwriteFiles: true);
                var candidate = Path.Combine(stagingDir, "Windows-Hinting.exe");
                if (File.Exists(candidate))
                    return candidate;

                // Fall back: pick the first .exe in the archive.
                foreach (var f in Directory.EnumerateFiles(stagingDir, "*.exe", SearchOption.AllDirectories))
                    return f;

                throw new FileNotFoundException("Zip did not contain an .exe.", downloadedArtifactPath);
            }

            var dest = Path.Combine(stagingDir, "Windows-Hinting.exe");
            File.Copy(downloadedArtifactPath, dest, overwrite: true);
            return dest;
        }

        private static bool CanWriteTo(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".wh-update-probe-" + Guid.NewGuid().ToString("N"));
                using (var fs = File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                    // noop
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildHelperScript()
        {
            // Args:
            //   %1 = PID of the app we're waiting on
            //   %2 = full path to the new exe (in staging)
            //   %3 = full path to the current exe (destination)
            // Retries the move for up to ~20s to cope with AV scanners holding
            // the file briefly after process exit.
            return
@"@echo off
setlocal
set PID=%~1
set NEW_EXE=%~2
set DEST=%~3

:wait
tasklist /FI ""PID eq %PID%"" 2>nul | find ""%PID%"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait
)

set /a TRIES=0
:move
move /Y ""%NEW_EXE%"" ""%DEST%"" >nul 2>nul
if not errorlevel 1 goto launch
set /a TRIES+=1
if %TRIES% GEQ 20 goto fail
timeout /t 1 /nobreak >nul
goto move

:launch
start """" ""%DEST%""
goto cleanup

:fail
echo Windows-Hinting update: could not replace ""%DEST%"".
echo The new version is staged at ""%NEW_EXE%"".
start """" ""%~dp0""

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
