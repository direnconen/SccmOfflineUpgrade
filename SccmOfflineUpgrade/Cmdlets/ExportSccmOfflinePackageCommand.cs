using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace SccmOfflineUpgrade
{
    [Cmdlet(VerbsData.Export, "SccmOfflinePackage")]
    [OutputType(typeof(string))]
    public class ExportSccmOfflinePackageCommand : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string OutputZip { get; set; } = string.Empty;

        [Parameter]
        public string? ServiceConnectionToolPath { get; set; }

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string StagingRoot { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\staging";

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string UsageDataCabName { get; set; } = "UsageData.cab";

        [Parameter]
        [ValidateNotNullOrEmpty]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Export-SccmOfflinePackage.log";

        /// <summary>
        /// Offline makinede prepare'den önce ODBC 18 kontrol et/sessiz kur (opsiyonel).
        /// </summary>
        [Parameter]
        public SwitchParameter EnsureOdbc18 { get; set; }

        /// <summary>
        /// EnsureOdbc18 kullanılırsa, offline MSI yolunu (ör. D:\Prereqs\msodbcsql18.msi) verin.
        /// </summary>
        [Parameter]
        public string? OdbcInstallerPath { get; set; }

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Export-SccmOfflinePackage starting -> OutputZip={OutputZip}");
            string? stage = null;

            try
            {
                // 0) ODBC 18 ön-kontrol (opsiyonel)
                if (EnsureOdbc18.IsPresent)
                {
                    if (!OdbcChecker.IsOdbc18Installed())
                    {
                        if (string.IsNullOrWhiteSpace(OdbcInstallerPath) || !File.Exists(OdbcInstallerPath))
                            throw new Exception("Microsoft ODBC Driver 18 is missing. Provide -OdbcInstallerPath to install offline.");

                        OdbcChecker.InstallOdbc18Msi(OdbcInstallerPath!, LogPath);

                        if (!OdbcChecker.IsOdbc18Installed())
                            throw new Exception("ODBC 18 installation did not complete successfully.");
                        else
                            Logger.Write(LogPath, "ODBC 18 installed successfully (pre-check).");
                    }
                    else
                    {
                        Logger.Write(LogPath, "ODBC 18 is already installed.");
                    }
                }

                // 1) ServiceConnectionTool klasörünü çöz (klasör veya exe yolu kabul edilir)
                var toolFolder = ServiceConnectionToolResolver.GetToolFolder(ServiceConnectionToolPath);
                var exe = Path.Combine(toolFolder, "ServiceConnectionTool.exe");
                if (!File.Exists(exe))
                    throw new FileNotFoundException("ServiceConnectionTool.exe not found under resolved folder", exe);
                Logger.Write(LogPath, $"ServiceConnectionTool resolved: {exe}");

                // 2) Staging alanını hazırla
                stage = FileUtils.EnsureDirectory(Path.Combine(StagingRoot, "xfer-" + Guid.NewGuid().ToString("N")));
                var xTool = FileUtils.EnsureDirectory(Path.Combine(stage, "ServiceConnectionTool"));
                var xTransfer = FileUtils.EnsureDirectory(Path.Combine(stage, "Transfer"));

                // 3) Aracı staging'e kopyala (tüm alt içeriklerle)
                Logger.Write(LogPath, "Copy ServiceConnectionTool to staging...");
                CopyDirectory(toolFolder, xTool);

                // 4) Prepare çağrısı (WorkingDirectory = xTool)
                var cabDest = Path.Combine(xTransfer, UsageDataCabName);
                Logger.Write(LogPath, $"Prepare (usage data) -> {cabDest}");
                var args = $"-prepare -usagedatadest \"{cabDest}\"";

                var res = ProcessRunner.Run(Path.Combine(xTool, "ServiceConnectionTool.exe"), args, 0, xTool);
                if (!string.IsNullOrWhiteSpace(res.StdOut)) Logger.Write(LogPath, res.StdOut.Trim(), LogLevel.DEBUG);
                if (!string.IsNullOrWhiteSpace(res.StdErr)) Logger.Write(LogPath, res.StdErr.Trim(), LogLevel.WARN);

                // 5) CAB beklenen yerde yoksa fallback: staging altında bul ve yerine taşı
                if (!File.Exists(cabDest))
                {
                    var found = Directory.EnumerateFiles(stage, "*.cab", SearchOption.AllDirectories)
                                         .OrderByDescending(f => new FileInfo(f).Length)
                                         .FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cabDest)!);
                        Logger.Write(LogPath, $"CAB not at expected path. Found: {found}. Moving to {cabDest}");
                        if (File.Exists(cabDest)) File.Delete(cabDest);
                        File.Move(found, cabDest);
                    }
                }

                if (!File.Exists(cabDest))
                {
                    Logger.Write(LogPath, "Prepare step completed but CAB was not created/found.", LogLevel.ERROR);
                    throw new Exception("Prepare step failed or CAB not created.");
                }

                // 6) ZIP oluştur
                if (File.Exists(OutputZip)) File.Delete(OutputZip);
                Logger.Write(LogPath, $"Compressing staging -> {OutputZip}");
                FileUtils.ZipDirectory(stage, OutputZip);

                // 7) Çıktı
                WriteObject(OutputZip);
                Logger.Write(LogPath, "Export completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "ExportFailed", ErrorCategory.NotSpecified, this));
            }
            finally
            {
                // staging'i silmek istemezsen yoruma al
                if (!string.IsNullOrEmpty(stage))
                {
                    try { FileUtils.SafeDeleteDirectory(stage); } catch { /* ignore cleanup errors */ }
                }
            }
        }

        /// <summary>
        /// Basit ve sağlam bir klasör kopyalama (alt dizinler ve dosyalar).
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            var src = new DirectoryInfo(sourceDir);
            if (!src.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            Directory.CreateDirectory(destDir);

            foreach (var dir in src.GetDirectories("*", SearchOption.AllDirectories))
            {
                var target = dir.FullName.Replace(src.FullName, destDir);
                Directory.CreateDirectory(target);
            }

            foreach (var file in src.GetFiles("*", SearchOption.AllDirectories))
            {
                var target = file.FullName.Replace(src.FullName, destDir);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                file.CopyTo(target, true);
            }
        }
    }
}
