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
        public string OutputZip { get; set; } = string.Empty;

        [Parameter]
        public string? ServiceConnectionToolPath { get; set; }

        [Parameter]
        public string StagingRoot { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\staging";

        [Parameter]
        public string UsageDataCabName { get; set; } = "UsageData.cab";

        [Parameter]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Export-SccmOfflinePackage.log";

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Export-SccmOfflinePackage starting -> {OutputZip}");
            try
            {
                var toolFolder = ServiceConnectionToolResolver.GetToolFolder(ServiceConnectionToolPath);
                var exe = Path.Combine(toolFolder, "ServiceConnectionTool.exe");

                var stage = FileUtils.EnsureDirectory(Path.Combine(StagingRoot, "xfer-" + Guid.NewGuid().ToString("N")));
                var xTool = FileUtils.EnsureDirectory(Path.Combine(stage, "ServiceConnectionTool"));
                var xTransfer = FileUtils.EnsureDirectory(Path.Combine(stage, "Transfer"));

                Logger.Write(LogPath, "Copy ServiceConnectionTool to staging...");
                foreach (var file in Directory.GetFileSystemEntries(toolFolder, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(toolFolder.Length).TrimStart(Path.DirectorySeparatorChar);
                    var dest = Path.Combine(xTool, rel);
                    if (Directory.Exists(file))
                    {
                        Directory.CreateDirectory(dest);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? xTool);
                        File.Copy(file, dest, true);
                    }
                }

                var cabDest = Path.Combine(xTransfer, UsageDataCabName);
                Logger.Write(LogPath, $"Prepare (usage data) -> {cabDest}");
                var args = $"-prepare -usagedatadest \"{cabDest}\"";

                // Çalışma dizinini xTool yap
                var res = ProcessRunner.Run(Path.Combine(xTool, "ServiceConnectionTool.exe"), args, 0, xTool);

                // Başarısızsa veya cab beklenen yerde yoksa, fallback tara
                if (!File.Exists(cabDest))
                {
                    var found = Directory.EnumerateFiles(stage, "*.cab", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                    {
                        Logger.Write(LogPath, $"CAB not at expected path. Found: {found}. Moving to {cabDest}");
                        Directory.CreateDirectory(Path.GetDirectoryName(cabDest)!);
                        if (File.Exists(cabDest)) File.Delete(cabDest);
                        File.Move(found, cabDest);
                    }
                }

                if (!File.Exists(cabDest))
                {
                    Logger.Write(LogPath, res.StdOut + Environment.NewLine + res.StdErr, LogLevel.ERROR);
                    throw new Exception("Prepare step failed or CAB not created.");
                }

                if (File.Exists(OutputZip)) File.Delete(OutputZip);
                Logger.Write(LogPath, $"Compressing staging -> {OutputZip}");
                FileUtils.ZipDirectory(stage, OutputZip);

                WriteObject(OutputZip);
                Logger.Write(LogPath, "Export completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "ExportFailed", ErrorCategory.NotSpecified, this));
            }
        }
    }
}
