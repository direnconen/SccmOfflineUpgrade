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
        public string InputZip { get; set; } = string.Empty;

        [Parameter]
        public string? ServiceConnectionToolPath { get; set; }

        [Parameter]
        public string ExtractTo { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\import";

        [Parameter]
        public SwitchParameter RunPrereqCheck { get; set; }

        [Parameter]
        public string? CdLatestRoot { get; set; }

        [Parameter]
        public SwitchParameter TryStartUpgrade { get; set; }

        [Parameter]
        public string? SiteCode { get; set; }

        [Parameter]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Import-SccmOfflineUpdates.log";

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Import-SccmOfflineUpdates starting: {InputZip}");
            try
            {
                if (!File.Exists(InputZip))
                    throw new FileNotFoundException("InputZip not found", InputZip);

                var toolFolder = ServiceConnectionToolResolver.GetToolFolder(ServiceConnectionToolPath);
                var exe = Path.Combine(toolFolder, "ServiceConnectionTool.exe");

                if (Directory.Exists(ExtractTo))
                {
                    Logger.Write(LogPath, "Cleaning existing extract folder...");
                    FileUtils.SafeDeleteDirectory(ExtractTo);
                }
                FileUtils.Unzip(InputZip, ExtractTo);

                Logger.Write(LogPath, "Import updates...");
                var args = $"-import -updatepacksrc \"{ExtractTo}\"";
                var res = ProcessRunner.Run(exe, args);
                if (res.ExitCode != 0)
                {
                    Logger.Write(LogPath, res.StdOut + Environment.NewLine + res.StdErr, LogLevel.ERROR);
                    throw new Exception("Import step failed.");
                }

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
                        Logger.Write(LogPath, "Running prereqchk.exe /LOCAL ...");
                        var resPrq = ProcessRunner.Run(prq, "/LOCAL");
                        Logger.Write(LogPath, "Prereq check finished.");
                    }
                    else
                    {
                        Logger.Write(LogPath, "prereqchk.exe not found; skipping.", LogLevel.WARN);
                    }
                }

                if (TryStartUpgrade.IsPresent)
                {
                    if (string.IsNullOrWhiteSpace(SiteCode))
                        throw new ArgumentException("TryStartUpgrade requires -SiteCode.");

                    try
                    {
                        // Best-effort WMI tetikleme
                        var ns = $@"root\SMS\site_{SiteCode}";
                        var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT * FROM SMS_CM_UpdatePackages"));
                        var updates = searcher.Get().Cast<ManagementObject>().ToList();
                        if (updates.Count == 0)
                        {
                            Logger.Write(LogPath, "No updates found in SMS_CM_UpdatePackages.", LogLevel.WARN);
                        }
                        else
                        {
                            var target = updates.First();
                            if (updates.Count > 1 && updates[0].Properties["CreationDate"] != null)
                            {
                                target = updates.OrderByDescending(u => u.Properties["CreationDate"]?.Value?.ToString()).First();
                            }
                            var pkgGuid = target.Properties["PackageGuid"]?.Value?.ToString();
                            if (!string.IsNullOrWhiteSpace(pkgGuid))
                            {
                                var statusSearcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery($"SELECT * FROM SMS_CM_UpdatePackageSiteStatus WHERE PackageGuid='{pkgGuid}'"));
                                foreach (ManagementObject st in statusSearcher.Get())
                                {
                                    try
                                    {
                                        st.InvokeMethod("UpdatePackageSiteState", null);
                                        Logger.Write(LogPath, $"Install requested for PackageGuid={pkgGuid} (best-effort).");
                                    }
                                    catch (Exception ex2)
                                    {
                                        Logger.Write(LogPath, $"Failed to request install via WMI: {ex2.Message}", LogLevel.WARN);
                                    }
                                }
                            }
                            else
                            {
                                Logger.Write(LogPath, "Could not determine PackageGuid for selected update.", LogLevel.WARN);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(LogPath, $"TryStartUpgrade failed or unsupported: {ex.Message}", LogLevel.WARN);
                    }
                }

                Logger.Write(LogPath, "Import completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "ImportFailed", ErrorCategory.NotSpecified, this));
            }
        }
    }
}
