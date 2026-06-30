using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Deployment shape of the currently running process. Drives which auto-update
    /// channel (appcast + installer strategy) <see cref="UpdateService"/> uses.
    /// </summary>
    internal enum DeploymentKind
    {
        /// <summary>Installed from the signed WiX MSI (per-machine, under Program Files).</summary>
        Installer,
        /// <summary>Running from a portable single-file self-contained publish.</summary>
        Portable,
        /// <summary>Could not be determined (e.g. local dev/debug run).</summary>
        Unknown,
    }

    internal static class DeploymentMode
    {
        /// <summary>
        /// Resolved at first access. Prefers the compile-time constant set by the
        /// portable build (<c>PortableBuild=true</c> ? <c>PORTABLE</c> define).
        /// Falls back to a runtime heuristic.
        /// </summary>
        public static DeploymentKind Current { get; } = Detect();

        /// <summary>
        /// Runtime identifier string (<c>win-x64</c> / <c>win-x86</c>) used to pick
        /// the per-architecture portable appcast. Falls back to <c>win-x64</c>.
        /// </summary>
        public static string RuntimeId =>
            RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64",
            };

        private static DeploymentKind Detect()
        {
#if PORTABLE
            return DeploymentKind.Portable;
#else
            // Runtime fallback: an MSI install lives under Program Files and has
            // the original assembly on disk next to the exe. Single-file self-
            // contained publishes report Assembly.Location == string.Empty.
            try
            {
                var asmLocation = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(asmLocation))
                    return DeploymentKind.Portable;

                var exePath = Environment.ProcessPath ?? asmLocation;
                var exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (!string.IsNullOrEmpty(programFiles) &&
                    exeDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
                    return DeploymentKind.Installer;
                if (!string.IsNullOrEmpty(programFilesX86) &&
                    exeDir.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
                    return DeploymentKind.Installer;
            }
            catch
            {
                // fall through
            }
            return DeploymentKind.Unknown;
#endif
        }
    }
}
