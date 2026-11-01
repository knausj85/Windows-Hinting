using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace WindowsHinting.Services
{
    /// <summary>
    /// Process token queries (integrity level, UIAccess) backing the RPC caller
    /// policy in <see cref="PipeCommandService"/> and <see cref="HintController"/>.
    /// </summary>
    internal static class ProcessSecurity
    {
        // Mandatory integrity level RIDs (SECURITY_MANDATORY_*_RID).
        public const int LowRid = 0x1000;
        public const int MediumRid = 0x2000;
        public const int MediumPlusRid = 0x2100;
        public const int HighRid = 0x3000;
        public const int SystemRid = 0x4000;

        private static readonly Lazy<bool> _currentHasUIAccess = new(() =>
        {
            using var process = OpenForQuery((uint)Environment.ProcessId);
            return process != null && QueryToken(process).UIAccess;
        });

        /// <summary>True when this process runs with a UIAccess token (installed Release build).</summary>
        public static bool CurrentProcessHasUIAccess => _currentHasUIAccess.Value;

        /// <summary>
        /// Opens <paramref name="pid"/> with PROCESS_QUERY_LIMITED_INFORMATION.
        /// Returns null if the process is gone or access is denied.
        /// </summary>
        public static SafeFileHandle? OpenForQuery(uint pid)
        {
            var handle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }
            return handle;
        }

        public static string? GetImagePath(SafeHandle process)
        {
            Span<char> buffer = stackalloc char[1024];
            uint size = (uint)buffer.Length;
            if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size))
                return null;
            return buffer[..(int)size].ToString();
        }

        /// <summary>
        /// Reads the integrity RID and UIAccess flag from the process token.
        /// IntegrityRid is null when the token cannot be opened or read.
        /// </summary>
        public static (int? IntegrityRid, bool UIAccess) QueryToken(SafeHandle process)
        {
            if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out SafeFileHandle token))
                return (null, false);

            using (token)
            {
                return (ReadIntegrityRid(token), ReadUIAccess(token));
            }
        }

        /// <summary>Integrity RID of the process <paramref name="pid"/>, or null if unreadable.</summary>
        public static int? GetIntegrityRid(uint pid)
        {
            using var process = OpenForQuery(pid);
            return process == null ? null : QueryToken(process).IntegrityRid;
        }

        public static uint GetWindowProcessId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return 0;
            PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint pid);
            return pid;
        }

        public static string DescribeIntegrity(int? rid) => rid switch
        {
            null => "Unknown",
            >= SystemRid => "System",
            >= HighRid => "High",
            >= MediumPlusRid => "MediumPlus",
            >= MediumRid => "Medium",
            >= LowRid => "Low",
            _ => "Untrusted",
        } + (rid is int r ? $" (0x{r:X4})" : "");

        private static unsafe int? ReadIntegrityRid(SafeFileHandle token)
        {
            Span<byte> buffer = stackalloc byte[256];
            fixed (byte* p = buffer)
            {
                if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                        p, (uint)buffer.Length, out _))
                    return null;

                var label = (TOKEN_MANDATORY_LABEL*)p;
                PSID sid = label->Label.Sid;
                byte count = *PInvoke.GetSidSubAuthorityCount(sid);
                if (count == 0)
                    return null;
                return (int)*PInvoke.GetSidSubAuthority(sid, (uint)(count - 1));
            }
        }

        private static unsafe bool ReadUIAccess(SafeFileHandle token)
        {
            uint uiAccess = 0;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUIAccess,
                       &uiAccess, sizeof(uint), out _)
                   && uiAccess != 0;
        }
    }
}
