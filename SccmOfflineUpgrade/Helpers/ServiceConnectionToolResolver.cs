using Microsoft.Win32;
using System.IO;

namespace SccmOfflineUpgrade
{
    internal static class ServiceConnectionToolResolver
    {
        public static string GetToolFolder(string? overridePath = null)
        {
            if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
                return Path.GetFullPath(overridePath);

            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SMS\Setup");
            var instDir = key?.GetValue("Installation Directory") as string;
            if (!string.IsNullOrEmpty(instDir))
            {
                var candidate = Path.Combine(instDir, @"CD.Latest\SMSSETUP\TOOLS\ServiceConnectionTool");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new DirectoryNotFoundException("ServiceConnectionTool folder not found. Provide -ServiceConnectionToolPath.");
        }
    }
}
