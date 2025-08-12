using System;
using System.Runtime.InteropServices;

namespace SccmOfflineUpgrade
{
    [Flags]
    internal enum ErrorModes : uint
    {
        SEM_FAILCRITICALERRORS = 0x0001,
        SEM_NOGPFAULTERRORBOX = 0x0002,
        SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
        SEM_NOOPENFILEERRORBOX = 0x8000
    }

    internal static class Native
    {
        [DllImport("kernel32.dll")]
        private static extern ErrorModes SetErrorMode(ErrorModes uMode);

        private static ErrorModes _original;

        /// <summary>
        /// Disable Windows error UI (GPF/critical/open-file error boxes) for this process (and child processes inherit).
        /// Call once at startup or before launching child processes.
        /// </summary>
        public static void SuppressWindowsErrorDialogs()
        {
            // Save current (optional; we won't restore to keep it effective during the run)
            _original = SetErrorMode(0);
            var flags = ErrorModes.SEM_FAILCRITICALERRORS | ErrorModes.SEM_NOGPFAULTERRORBOX | ErrorModes.SEM_NOOPENFILEERRORBOX;
            SetErrorMode(flags);
        }
    }
}
