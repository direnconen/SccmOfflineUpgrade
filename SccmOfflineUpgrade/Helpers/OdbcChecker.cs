using Microsoft.Win32;
using System;
using System.IO;

namespace SccmOfflineUpgrade
{
    internal static class OdbcChecker
    {
        // ODBC 18 kurulu mu?
        public static bool IsOdbc18Installed()
        {
            // x64 sistemde 64-bit ODBC'yi kontrol et.
            string[] keys =
            {
                @"SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var k in keys)
            {
                using var hk = Registry.LocalMachine.OpenSubKey(k);
                if (hk == null) continue;

                // 1) ODBC Drivers listesinde isim
                if (string.Equals(k, @"SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var name in hk.GetValueNames())
                    {
                        if (name.IndexOf("ODBC Driver 18 for SQL Server", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }

                // 2) Uninstall altında "ODBC Driver 18"
                foreach (var sub in hk.GetSubKeyNames())
                {
                    using var subk = hk.OpenSubKey(sub);
                    var display = subk?.GetValue("DisplayName") as string;
                    if (!string.IsNullOrEmpty(display) &&
                        display.IndexOf("ODBC Driver 18 for SQL Server", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        // Sessiz kur (MessageBox yok)
        public static void InstallOdbc18Msi(string msiPath, string logPath)
        {
            if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath))
                throw new FileNotFoundException("ODBC MSI not found", msiPath);

            var args = $"/i \"{msiPath}\" IACCEPTMSODBCSQLLICENSETERMS=YES /qn /norestart";
            Logger.Write(logPath, $"Installing ODBC 18: {msiPath}");
            var res = ProcessRunner.Run("msiexec.exe", args, 0, Path.GetDirectoryName(msiPath));
            Logger.Write(logPath, $"ODBC install exit code: {res.ExitCode}");
            // exit code 0/1641/3010 genelde kuruldu/yeniden başlatma istenir anlamına gelir.
        }
    }
}
