using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace WindowsHinting.Services
{
    /// <summary>
    /// RPC caller allow-list: only talon.exe installed under Program Files and
    /// Authenticode-signed by Talon Voice, LLC may use the pipe.
    /// </summary>
    /// <remarks>
    /// Program Files is admin-writable only, so a medium-integrity process can neither
    /// plant nor swap the binary after it has been verified; Talon itself runs above
    /// medium integrity, so it can't be injected into either. The signer is matched by
    /// subject name rather than thumbprint so certificate renewals keep working.
    /// </remarks>
    internal static class CallerTrust
    {
        public const string TalonImageName = "talon.exe";
        public const string TalonSigner = "Talon Voice, LLC";

        private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private static readonly object _cacheLock = new();
        private static (string Path, DateTime LastWrite, long Length, string? Reason)? _cache;

        /// <summary>
        /// Null when <paramref name="imagePath"/> is a trusted Talon binary; otherwise
        /// why it was refused.
        /// </summary>
        public static string? CheckImage(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return "caller image path unknown";

            string fullPath = Path.GetFullPath(imagePath);
            if (!string.Equals(Path.GetFileName(fullPath), TalonImageName, StringComparison.OrdinalIgnoreCase))
                return $"caller is not {TalonImageName}";

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(programFiles) ||
                !fullPath.StartsWith(Path.TrimEndingDirectorySeparator(programFiles) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return $"caller is not installed under {programFiles}";

            var file = new FileInfo(fullPath);
            if (!file.Exists)
                return "caller image not found on disk";

            // Signature checks hash the file; skip them while it is unchanged.
            lock (_cacheLock)
            {
                if (_cache is { } c && c.Path == fullPath && c.LastWrite == file.LastWriteTimeUtc && c.Length == file.Length)
                    return c.Reason;
            }

            string? reason = CheckSignature(fullPath);
            lock (_cacheLock)
            {
                _cache = (fullPath, file.LastWriteTimeUtc, file.Length, reason);
            }
            return reason;
        }

        private static string? CheckSignature(string path)
        {
            int status = VerifyTrust(path);
            if (status != 0)
                return $"signature not valid (WinVerifyTrust 0x{status:X8})";

            try
            {
#pragma warning disable SYSLIB0057 // Reads the Authenticode signer, which X509CertificateLoader can't.
                using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                string name = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                return string.Equals(name, TalonSigner, StringComparison.Ordinal)
                    ? null
                    : $"signed by '{name}', not '{TalonSigner}'";
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException)
            {
                return $"cannot read signer: {ex.Message}";
            }
        }

        /// <summary>
        /// WinVerifyTrust with the generic Authenticode policy and no UI. Revocation is
        /// not checked: it needs the network, and the install location is what stops a
        /// stolen-then-revoked certificate from mattering here.
        /// </summary>
        private static unsafe int VerifyTrust(string path)
        {
            fixed (char* pPath = path)
            {
                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                    pcwszFilePath = pPath,
                };
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)sizeof(WINTRUST_DATA),
                    dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                    fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                    dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                    dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
                    dwProvFlags = WINTRUST_DATA_PROVIDER_FLAGS.WTD_CACHE_ONLY_URL_RETRIEVAL,
                };
                data.Anonymous.pFile = &fileInfo;

                Guid action = WintrustActionGenericVerifyV2;
                int status = PInvoke.WinVerifyTrust(HWND.Null, &action, &data);

                data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
                PInvoke.WinVerifyTrust(HWND.Null, &action, &data);
                return status;
            }
        }
    }
}
