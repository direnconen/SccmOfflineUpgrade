// File: Cmdlets/ImportSccmOfflineUpdatesCommand.cs
// Namespace: SccmOfflineUpgrade
//
// Features:
// - Unzips online package to staging and runs ServiceConnectionTool -import
// - Optional prereq check (prereqchk.exe /LOCAL) with auto CD.Latest discovery
// - Optional best-effort upgrade trigger via WMI (auto-detect SMS Provider)
// - Optional ODBC 18 silent install from packaged Prereqs\ODBC18 if missing
// - Suppresses any interactive OS error dialogs
// - Detailed logging at each step
//
// Requirements: Logger, FileUtils, ServiceConnectionToolResolver, OdbcChecker, Native, ProcessRunner helpers.

using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Management.Automation;

namespace SccmOfflineUpgrade
{
    [Cmdlet(VerbsData.Import, "SccmOfflineUpdates")]
    public class ImportSccmOfflineUpdatesCommand : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string InputZip { get; set; } = string.Empty;

        [Parameter]
        public string? ServiceConnectionToolPath { get; set; }

        /// <summary>
        /// Staging (extract) folder. README uyumu için StagingRoot; geriye dönük uyum için ExtractTo alias'ı desteklenir.
        /// </summary>
        [Parameter]
        [Alias("ExtractTo")]
        [ValidateNotNullOrEmpty]
        public string StagingRoot { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\import";

        [Parameter]
        public SwitchParameter RunPrereqCheck { get; set; }

        [Parameter]
        public string? CdLatestRoot { get; set; }

        /// <summary>
        /// Upgrade tetikleme anahtarı. README'deki StartUpgrade/TriggerUpgrade alias'ları desteklenir.
        /// </summary>
        [Parameter]
        [Alias("StartUpgrade", "TriggerUpgrade")]
        public SwitchParameter TryStartUpgrade { get; set; }

        /// <summary>
        /// WMI namespace için gereklidir (root\SMS\site_<SiteCode>).
        /// </summary>
        [Parameter]
        public string? SiteCode { get; set; }

        /// <summary>
        /// Opsiyonel. Verilirse WMI scope '\\<SiteServer>\root\SMS\site_<SiteCode>' olarak kurulur. Verilmezse SMS Provider otomatik bulunur.
        /// </summary>
        [Parameter]
        public string? SiteServer { get; set; }

        /// <summary>
        /// Paket içinden (Prereqs\ODBC18) Microsoft ODBC 18 MSI varsa ve sistemde kurulu değilse sessiz kur.
        /// </summary>
        [Parameter]
        public SwitchParameter EnsureOdbc18 { get; set; } = true;

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Import-SccmOfflineUpdates.log";

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Import-SccmOfflineUpdates starting. InputZip='{InputZip}'  StagingRoot='{StagingRoot}'");
            Native.SuppressWindowsErrorDialogs();

            try
            {
                if (!File.Exists(InputZip))
                    throw new FileNotFoundException("InputZip not found", InputZip);

                // ServiceConnectionTool klasörünü çöz
                var toolFolder = ServiceConnectionToolResolver.GetToolFolder(ServiceConnectionToolPath);
                var exe = Path.Combine(toolFolder, "ServiceConnectionTool.exe");
                if (!File.Exists(exe))
                    throw new FileNotFoundException("ServiceConnectionTool.exe not found", exe);

                // Staging temizle & prepare
                if (Directory.Exists(StagingRoot))
                {
                    Logger.Write(LogPath, $"Cleaning existing staging folder: {StagingRoot}");
                    FileUtils.SafeDeleteDirectory(StagingRoot);
                }
                Directory.CreateDirectory(StagingRoot);

                Logger.Write(LogPath, $"Extracting package -> {StagingRoot}");
                FileUtils.Unzip(InputZip, StagingRoot);

                // ODBC 18: Paket içinden (Prereqs\ODBC18\msodbcsql*.msi) sessiz kur (opsiyonel)
                if (EnsureOdbc18.IsPresent)
                {
                    try
                    {
                        if (!OdbcChecker.IsOdbc18Installed())
                        {
                            var msi = Directory
                                .EnumerateFiles(StagingRoot, "msodbcsql*.msi", SearchOption.AllDirectories)
                                .FirstOrDefault(p =>
                                    p.IndexOf("Prereqs", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    p.IndexOf("ODBC18", StringComparison.OrdinalIgnoreCase) >= 0);

                            if (!string.IsNullOrEmpty(msi))
                            {
                                Logger.Write(LogPath, $"ODBC 18 not found. Installing from package: {msi}");
                                OdbcChecker.InstallOdbc18Msi(msi, LogPath);
                            }
                            else
                            {
                                Logger.Write(LogPath, "ODBC 18 not found and installer not present in package. Import may fail.", LogLevel.WARN);
                            }
                        }
                        else
                        {
                            Logger.Write(LogPath, "ODBC 18 is already installed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(LogPath, $"ODBC 18 precheck/install failed: {ex.Message}", LogLevel.WARN);
                    }
                }

                // IMPORT
                Logger.Write(LogPath, "Importing updates via ServiceConnectionTool -import ...");
                var args = $"-import -updatepacksrc \"{StagingRoot}\"";
                var res = ProcessRunner.Run(exe, args, 0, toolFolder);
                if (!string.IsNullOrWhiteSpace(res.StdOut)) Logger.Write(LogPath, res.StdOut.Trim(), LogLevel.DEBUG);
                if (!string.IsNullOrWhiteSpace(res.StdErr)) Logger.Write(LogPath, res.StdErr.Trim(), LogLevel.WARN);

                if (res.ExitCode != 0)
                {
                    Logger.Write(LogPath, "ServiceConnectionTool -import returned non-zero exit code. Check CMUpdate.log/dmpdownloader.log.", LogLevel.ERROR);
                    // Devam: prereq/upgrade adımlarını istek üzerine yine deneyebiliriz.
                }

                // PREREQ CHECK (opsiyonel)
                if (RunPrereqCheck.IsPresent)
                {
                    string? cd = CdLatestRoot;
                    if (string.IsNullOrWhiteSpace(cd))
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SMS\Setup");
                        var inst = key?.GetValue("Installation Directory") as string;
                        if (!string.IsNullOrEmpty(inst))
                            cd = Path.Combine(inst, "CD.Latest");
                    }

                    var prq = !string.IsNullOrEmpty(cd) ? Path.Combine(cd!, @"SMSSETUP\BIN\x64\prereqchk.exe") : null;
                    if (!string.IsNullOrEmpty(prq) && File.Exists(prq))
                    {
                        Logger.Write(LogPath, $"Running prereqchk.exe /LOCAL (path: {prq}) ...");
                        var resPrq = ProcessRunner.Run(prq, "/LOCAL", 0, Path.GetDirectoryName(prq));
                        if (!string.IsNullOrWhiteSpace(resPrq.StdOut)) Logger.Write(LogPath, resPrq.StdOut.Trim(), LogLevel.DEBUG);
                        if (!string.IsNullOrWhiteSpace(resPrq.StdErr)) Logger.Write(LogPath, resPrq.StdErr.Trim(), LogLevel.WARN);
                        Logger.Write(LogPath, $"Prereq check finished (exit {resPrq.ExitCode}).");
                    }
                    else
                    {
                        Logger.Write(LogPath, "prereqchk.exe not found; skipping.", LogLevel.WARN);
                    }
                }

                // UPGRADE TETİKLE (opsiyonel, best-effort)
                if (TryStartUpgrade.IsPresent)
                {
                    if (string.IsNullOrWhiteSpace(SiteCode))
                        Logger.Write(LogPath, "TryStartUpgrade requested but -SiteCode not provided.", LogLevel.WARN);
                    else
                        BestEffortStartUpgrade(SiteCode!, SiteServer);
                }

                Logger.Write(LogPath, "Import completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "ImportFailed", ErrorCategory.NotSpecified, this));
            }
        }

        private void BestEffortStartUpgrade(string siteCode, string? siteServer)
        {
            try
            {
                // SMS Provider makinesini belirle (parametre öncelikli, yoksa WMI'dan bul)
                string? providerMachine = GetSmsProviderMachine(siteServer, siteCode);
                if (string.IsNullOrWhiteSpace(providerMachine))
                {
                    Logger.Write(LogPath, "Could not determine SMS Provider machine. Aborting best-effort install.", LogLevel.WARN);
                    return;
                }

                var options = new ConnectionOptions
                {
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy,
                    EnablePrivileges = true
                };

                string nsPath = $@"\\{providerMachine}\root\SMS\site_{siteCode}";
                var scope = new ManagementScope(nsPath, options);
                scope.Connect();

                var updates = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM SMS_CM_UpdatePackages"))
                              .Get()
                              .Cast<ManagementObject>()
                              .ToList();

                if (updates.Count == 0)
                {
                    Logger.Write(LogPath, "No updates found in SMS_CM_UpdatePackages (is import completed/Available?).", LogLevel.WARN);
                    return;
                }

                // En yeni paketi seç (CreationDate varsa)
                ManagementObject target = updates.First();
                try
                {
                    target = updates.OrderByDescending(u => u.Properties["CreationDate"]?.Value?.ToString()).First();
                }
                catch { /* ignore */ }

                var rawGuid = target.Properties["PackageGuid"]?.Value?.ToString();
                if (string.IsNullOrWhiteSpace(rawGuid))
                {
                    Logger.Write(LogPath, "Selected update has no PackageGuid.", LogLevel.WARN);
                    return;
                }

                string pkgGuid = rawGuid.Trim();
                if (!pkgGuid.StartsWith("{")) pkgGuid = "{" + pkgGuid.Trim('{', '}') + "}";

                string q = $"SELECT * FROM SMS_CM_UpdatePackageSiteStatus WHERE PackageGuid='{pkgGuid}'";
                var status = new ManagementObjectSearcher(scope, new ObjectQuery(q)).Get().Cast<ManagementObject>().ToList();

                if (status.Count == 0)
                {
                    Logger.Write(LogPath, $"No site status objects found for PackageGuid={pkgGuid}. Package may not be Available yet or wrong site/namespace.", LogLevel.WARN);
                    return;
                }

                int ok = 0, fail = 0;
                foreach (var st in status)
                {
                    try
                    {
                        st.InvokeMethod("UpdatePackageSiteState", null);
                        ok++;
                    }
                    catch (Exception ex2)
                    {
                        fail++;
                        Logger.Write(LogPath, $"Failed to request install via WMI: {ex2.Message}", LogLevel.WARN);
                    }
                }

                Logger.Write(LogPath, $"Install requested (best-effort). Success={ok}, Failed={fail}.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"TryStartUpgrade failed or unsupported: {ex.Message}", LogLevel.WARN);
            }
        }

        /// <summary>
        /// SMS Provider makinesini döndürür.
        /// 1) Kullanıcı -SiteServer verdiyse onu kullanır.
        /// 2) Verilmemişse local root\SMS altındaki SMS_ProviderLocation ile ProviderForLocalSite=true ve SiteCode eşleşen kaydı arar.
        /// Bulamazsa null döner.
        /// </summary>
        private string? GetSmsProviderMachine(string? explicitServer, string siteCode)
        {
            if (!string.IsNullOrWhiteSpace(explicitServer))
                return explicitServer;

            try
            {
                var local = new ManagementScope(@"\\.\root\SMS");
                local.Connect();

                var searcher = new ManagementObjectSearcher(local,
                    new ObjectQuery("SELECT * FROM SMS_ProviderLocation WHERE ProviderForLocalSite=true"));

                foreach (ManagementObject mo in searcher.Get())
                {
                    var sc = mo["SiteCode"] as string;
                    var mach = mo["Machine"] as string;
                    if (!string.IsNullOrWhiteSpace(sc) &&
                        sc.Equals(siteCode, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(mach))
                    {
                        return mach;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"GetSmsProviderMachine lookup failed: {ex.Message}", LogLevel.WARN);
            }

            return null;
        }
    }
}
