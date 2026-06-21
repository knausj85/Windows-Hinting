using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using NetSparkleUpdater;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.WinForms;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Thin wrapper around <see cref="SparkleUpdater"/> that selects the right
    /// appcast URL and install strategy based on <see cref="DeploymentMode"/>.
    /// Supports both the signed MSI path (msiexec + UAC) and the portable
    /// self-replacing-exe path (<see cref="PortableUpdateInstaller"/>).
    /// </summary>
    /// <remarks>
    /// Update lifecycle diagnostic logs are always emitted. To capture a failed
    /// update attempt, enable file logging via the tray "Logging → Log to File"
    /// before triggering "Check for updates...", then review the log file after
    /// the update completes (or fails). The log directory can be opened from the
    /// "Logging → Open Log Folder" menu. MSI updates also produce a verbose
    /// msiexec log in %TEMP%\Windows-Hinting-msi-update-*.log.
    /// </remarks>
    internal sealed class UpdateService : IDisposable
    {
#if UPDATE_CHANNEL_BETA
        private const string AppcastBaseUrl =
            "https://github.com/knausj85/Windows-Hinting/releases/download/beta";
        private const string AppcastSuffix = "-beta";
        private const string ReleasesPageUrl =
            "https://github.com/knausj85/Windows-Hinting/releases/tag/beta";
        public const string ChannelName = "Beta";
#else
        private const string AppcastBaseUrl =
            "https://github.com/knausj85/Windows-Hinting/releases/latest/download";
        private const string AppcastSuffix = "";
        private const string ReleasesPageUrl =
            "https://github.com/knausj85/Windows-Hinting/releases/latest";
        public const string ChannelName = "Stable";
#endif

        private const string ProductName = "Windows-Hinting";

        private readonly ILogger _logger;
        private readonly IPreferencesService _preferencesService;
        private SparkleUpdater? _sparkle;
        private bool _disposed;

        public UpdateService(ILogger logger, IPreferencesService preferencesService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        }

        /// <summary>
        /// Called from the main thread after the overlay is shown. Silently
        /// creates the Sparkle instance and, if the user has opted in,
        /// schedules the first background check.
        /// </summary>
        public void Initialize()
        {
            try
            {
                _logger.Info(
                    $"UpdateService: initializing (channel={ChannelName}, deployment={DeploymentMode.Current}, runtime={DeploymentMode.RuntimeId}).");

                _sparkle = CreateSparkle();
                if (_sparkle == null)
                {
                    _logger.Info("UpdateService: disabled for this deployment kind.");
                    return;
                }

                var options = _preferencesService.Load();
                if (options.AutoCheckForUpdates)
                {
                    _sparkle.StartLoop(doInitialCheck: true, forceInitialCheck: true);
                    _logger.Info("UpdateService: automatic update checks enabled; forcing initial check on startup.");
                }
                else
                {
                    _logger.Info("UpdateService: automatic update checks disabled by preference.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"UpdateService.Initialize failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Triggered from the tray "Check for updates..." menu or the About dialog link.
        /// Always surfaces a UI: either the standard Sparkle update prompt, a
        /// "you're up to date" toast, or an error message.
        /// </summary>
        public async Task CheckForUpdatesManuallyAsync()
        {
            try
            {
                _logger.Info($"UpdateService: manual update check requested (channel={ChannelName}).");

                _sparkle ??= CreateSparkle();
                if (_sparkle == null)
                {
                    _logger.Info("UpdateService: manual check aborted; updater not available for this build.");
                    MessageBox.Show(
                        "Automatic updates are not available for this build.\n\n" +
                        "Please visit the releases page to download the latest version.",
                        "Check for Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    OpenReleasesPage();
                    return;
                }

                var info = await _sparkle.CheckForUpdatesAtUserRequest().ConfigureAwait(true);
                if (info == null)
                {
                    _logger.Info("UpdateService: manual check returned no result.");
                }
                else
                {
                    var count = info.Updates?.Count ?? 0;
                    _logger.Info($"UpdateService: manual check completed (status={info.Status}, updatesAvailable={count}).");
                }

                if (info == null || info.Status == UpdateStatus.CouldNotDetermine)
                {
                    MessageBox.Show(
                        "Could not check for updates. Please check your internet connection and try again.",
                        "Check for Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                // Sparkle's UIFactory handles UpdateAvailable / UpdateNotAvailable dialogs itself.
            }
            catch (Exception ex)
            {
                _logger.Warning($"Manual update check failed: {ex.Message}");
                MessageBox.Show(
                    $"Update check failed: {ex.Message}",
                    "Check for Updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private SparkleUpdater? CreateSparkle()
        {
            var publicKey = LoadEmbeddedPublicKey();
            if (string.IsNullOrWhiteSpace(publicKey) ||
                publicKey.StartsWith("REPLACE_WITH", StringComparison.Ordinal))
            {
                _logger.Warning("UpdateService: Ed25519 public key is not configured; updates disabled.");
                return null;
            }

            var appcastUrl = BuildAppcastUrl();
            if (appcastUrl == null)
            {
                _logger.Info($"UpdateService: no appcast URL for deployment={DeploymentMode.Current}; updates disabled.");
                return null;
            }

            _logger.Info($"UpdateService: using appcast {appcastUrl}");

            // Load preferences to configure NetSparkle logging
            var options = _preferencesService.Load();
            _logger.Info($"UpdateService: NetSparkle log routing enabled={options.Logging.NetSparkleEnabled}");

            var signatureChecker = new Ed25519Checker(SecurityMode.Strict, publicKey);

            var icon = TryLoadAppIcon();

            var sparkle = new SparkleUpdater(appcastUrl, signatureChecker)
            {
                UIFactory = new UIFactory(icon)
                {
                    HideReleaseNotes = false,
                    HideRemindMeLaterButton = false,
                    HideSkipButton = false,
                },
                RelaunchAfterUpdate = true,
                UserInteractionMode = UserInteractionMode.NotSilent,
                LogWriter = new NetSparkleLoggerAdapter(_logger, options.Logging.NetSparkleEnabled),
            };

            sparkle.UpdateCheckStarted += (_) =>
                _logger.Info("UpdateService: update check started.");
            sparkle.UpdateCheckFinished += (_, status) =>
                _logger.Info($"UpdateService: update check finished (status={status}).");
            sparkle.UpdateDetected += (_, e) =>
                _logger.Info(
                    $"UpdateService: update detected (latest={e.LatestVersion?.Version}, current={e.ApplicationConfig?.InstalledVersion}).");

            // Diagnostic event handlers to trace download/install lifecycle
            sparkle.DownloadStarted += (item, path) =>
                _logger.Info($"UpdateService: download started (item={item?.Title ?? item?.Version ?? "unknown"}).");

            sparkle.DownloadFinished += (item, path) =>
            {
                var size = path != null && File.Exists(path) ? new FileInfo(path).Length / 1024.0 / 1024.0 : 0;
                _logger.Info($"UpdateService: download finished (path={path}, size={size:F2} MB).");
            };

            sparkle.CloseApplicationAsync += () =>
            {
                _logger.Info($"UpdateService: close application requested for install.");
                return Task.CompletedTask;
            };

            // Deployment-specific install strategy.
            if (DeploymentMode.Current == DeploymentKind.Portable)
            {
                // Portable: intercept the close-to-install phase and do our own
                // self-replacement via the helper script.
                sparkle.CloseApplicationAsync += OnPortableCloseApplicationAsync;
                sparkle.DownloadFinished += OnPortableDownloadFinished;
            }
            else
            {
                // MSI (and Unknown, which defaults to MSI appcast): intercept
                // download and close-to-install to launch msiexec elevated via
                // our helper script. NetSparkle's default msiexec path would run
                // non-elevated from a UIAccess process and silently fail.
                sparkle.CloseApplicationAsync += OnMsiCloseApplicationAsync;
                sparkle.DownloadFinished += OnMsiDownloadFinished;
                // Disable NetSparkle's built-in relaunch; our helper does it.
                sparkle.RelaunchAfterUpdate = false;
            }

            return sparkle;
        }

        private string? BuildAppcastUrl()
        {
            return DeploymentMode.Current switch
            {
                DeploymentKind.Installer => $"{AppcastBaseUrl}/appcast-installer{AppcastSuffix}.xml",
                DeploymentKind.Portable => $"{AppcastBaseUrl}/appcast-portable-{DeploymentMode.RuntimeId}{AppcastSuffix}.xml",
                DeploymentKind.Unknown => $"{AppcastBaseUrl}/appcast-installer{AppcastSuffix}.xml",
                _ => null,
            };
        }

        private static string LoadEmbeddedPublicKey()
        {
            var asm = Assembly.GetExecutingAssembly();
            // Resource name is <RootNamespace>.Resources.netsparkle_pub.txt
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("netsparkle_pub.txt", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream == null)
                        continue;
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd().Trim();
                }
            }
            return string.Empty;
        }

        private static System.Drawing.Icon? TryLoadAppIcon()
        {
            try
            {
                var exe = Environment.ProcessPath ?? Application.ExecutablePath;
                return System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch
            {
                return null;
            }
        }

        private string? _pendingPortableDownloadPath;

        private void OnPortableDownloadFinished(AppCastItem item, string path)
        {
            _pendingPortableDownloadPath = path;
        }

        private async Task OnPortableCloseApplicationAsync()
        {
            if (string.IsNullOrEmpty(_pendingPortableDownloadPath))
                return;

            try
            {
                var installer = new PortableUpdateInstaller(_logger);
                var launched = installer.InstallAndRelaunch(_pendingPortableDownloadPath!);
                if (!launched)
                {
                    // Preflight failed (e.g. read-only folder). Open the release
                    // page so the user can download manually, then let the app
                    // keep running.
                    OpenReleasesPage();
                    return;
                }

                // Give the helper a moment to spawn before we exit the main thread.
                await Task.Delay(250).ConfigureAwait(false);
                Application.Exit();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Portable update install failed: {ex.Message}");
                MessageBox.Show(
                    $"Could not install update automatically: {ex.Message}\n\n" +
                    "Opening the releases page so you can download manually.",
                    "Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                OpenReleasesPage();
            }
        }

        private string? _pendingMsiDownloadPath;

        private void OnMsiDownloadFinished(AppCastItem item, string path)
        {
            _pendingMsiDownloadPath = path;
        }

        private async Task OnMsiCloseApplicationAsync()
        {
            if (string.IsNullOrEmpty(_pendingMsiDownloadPath))
                return;

            try
            {
                var installer = new MsiUpdateInstaller(_logger);
                var launched = installer.InstallAndRelaunch(_pendingMsiDownloadPath!);
                if (!launched)
                {
                    // Preflight failed (e.g. user declined UAC). Open the release
                    // page so the user can download manually, then let the app
                    // keep running.
                    OpenReleasesPage();
                    return;
                }

                // Give the helper a moment to spawn before we exit the main thread.
                await Task.Delay(250).ConfigureAwait(false);
                Application.Exit();
            }
            catch (Exception ex)
            {
                _logger.Warning($"MSI update install failed: {ex.Message}");
                MessageBox.Show(
                    $"Could not install update automatically: {ex.Message}\n\n" +
                    "Opening the releases page so you can download manually.",
                    "Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                OpenReleasesPage();
            }
        }

        private static void OpenReleasesPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ReleasesPageUrl,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // best effort
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _sparkle?.StopLoop();
                _sparkle?.Dispose();
            }
            catch { /* swallow */ }
        }
    }
}
