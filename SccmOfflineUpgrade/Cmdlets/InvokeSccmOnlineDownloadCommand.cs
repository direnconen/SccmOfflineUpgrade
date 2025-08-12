using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace SccmOfflineUpgrade
{
    public class OnlineDownloadResult
    {
        public string OutputZip { get; set; } = string.Empty;
        public string? ErrorLog { get; set; }
    }

    [Cmdlet(VerbsLifecycle.Invoke, "SccmOnlineDownload")]
    [OutputType(typeof(OnlineDownloadResult))]
    public class InvokeSccmOnlineDownloadCommand : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        public string InputZip { get; set; } = string.Empty;

        [Parameter(Mandatory = true)]
        public string DownloadOutput { get; set; } = string.Empty;

        [Parameter(Mandatory = true)]
        public string OutputZip { get; set; } = string.Empty;

        [Parameter]
        public string Proxy { get; set; } = string.Empty;

        [Parameter]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Invoke-SccmOnlineDownload.log";

        [Parameter]
        public string ErrorLogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\OnlineDownloadErrors.log";

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Invoke-SccmOnlineDownload starting: {InputZip} -> {DownloadOutput}");
            string? work = null;
            try
            {
                if (!File.Exists(InputZip))
                    throw new FileNotFoundException("InputZip not found", InputZip);

                work = Path.Combine(Path.GetTempPath(), "SOT-" + Guid.NewGuid().ToString("N"));
                FileUtils.Unzip(InputZip, work);

                var tool = Path.Combine(work, "ServiceConnectionTool");
                var xfer = Path.Combine(work, "Transfer");
                var exe = Path.Combine(tool, "ServiceConnectionTool.exe");
                if (!File.Exists(exe))
                    throw new FileNotFoundException("ServiceConnectionTool.exe not found in package", exe);

                Directory.CreateDirectory(DownloadOutput);

                var args = $"-connect -usagedatasrc \"{xfer}\" -updatepackdest \"{DownloadOutput}\"";
                if (!string.IsNullOrWhiteSpace(Proxy))
                    args += $" -proxy \"{Proxy}\"";

                Logger.Write(LogPath, "Running ServiceConnectionTool -connect ...");
                var res = ProcessRunner.Run(exe, args);

                // Heuristik hata yakalama
                var combined = (res.StdOut ?? "") + Environment.NewLine + (res.StdErr ?? "");
                var lines = combined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var errorLines = lines.Where(l =>
                    l.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();

                foreach (var file in Directory.EnumerateFiles(DownloadOutput, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    if (info.Length == 0)
                        errorLines.Add($"Zero-length file: {file}");
                }

                string? errorLogOut = null;
                if (errorLines.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath) ?? ".");
                    File.WriteAllLines(ErrorLogPath, errorLines);
                    Logger.Write(LogPath, $"Download issues detected: {errorLines.Count}. Details: {ErrorLogPath}", LogLevel.WARN);
                    errorLogOut = ErrorLogPath;
                }

                if (File.Exists(OutputZip)) File.Delete(OutputZip);
                Logger.Write(LogPath, $"Compressing downloads -> {OutputZip}");
                FileUtils.ZipDirectory(DownloadOutput, OutputZip);

                var dto = new OnlineDownloadResult { OutputZip = OutputZip, ErrorLog = errorLogOut };
                WriteObject(dto);
                Logger.Write(LogPath, "Online download completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "OnlineDownloadFailed", ErrorCategory.NotSpecified, this));
            }
            finally
            {
                if (!string.IsNullOrEmpty(work))
                {
                    try { FileUtils.SafeDeleteDirectory(work); } catch { }
                }
            }
        }
    }
}
