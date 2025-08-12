using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Threading;

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

        // --- Regex kalıpları (stdout/err için)
        private static readonly Regex RxFileOfTotal = new(@"Downloading\s+file\s+(?<cur>\d+)\s+of\s+(?<tot>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxBytesOfTotal = new(@"(?<done>\d+(?:\.\d+)?)\s*(MB|GB)\s+of\s+(?<tot>\d+(?:\.\d+)?)\s*(MB|GB)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static long ToBytes(double value, string unit)
        {
            unit = unit.ToUpperInvariant();
            if (unit == "GB") return (long)(value * 1024 * 1024 * 1024);
            return (long)(value * 1024 * 1024); // MB
        }

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Invoke-SccmOnlineDownload starting: {InputZip} -> {DownloadOutput}");
            string? work = null;
            string? errorLogOut = null;

            var progress = new DownloadProgress();
            var pr = new ProgressRecord(1, "Downloading ConfigMgr updates", "Starting...");

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

                // --- Süreci manuel yönet: stdout/err satırlarını canlı yakala; MessageBox yok, pencere yok.
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = tool
                };
                var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };

                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    Logger.Write(LogPath, e.Data, LogLevel.DEBUG);
                    TryParseProgressFromLine(e.Data, progress);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    Logger.Write(LogPath, e.Data, LogLevel.WARN);
                    TryParseProgressFromLine(e.Data, progress);
                };

                // --- İlerleme & dosya tamamlama izleme döngüsü
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                var lastProgressUpdate = DateTime.MinValue;

                while (!p.HasExited)
                {
                    // Klasörü tara, tamamlanan dosyaları logla
                    progress.ScanFolderAndUpdate(DownloadOutput, (file, size) =>
                    {
                        Logger.Write(LogPath, $"[FILE] Completed: {file} ({size} bytes)");
                    });

                    // Yüzdeyi güncelle
                    var percent = progress.GetPercent(); // -1 ise bilinmiyor
                    var status = $"Downloaded: {FormatBytes(progress.CurrentBytes)}";
                    if (progress.ExpectedTotalBytes.HasValue)
                        status += $" / {FormatBytes(progress.ExpectedTotalBytes.Value)}";
                    status += $" | Completed files: {progress.CompletedFilesCount}";

                    // Çok sık yazmasın
                    if ((DateTime.Now - lastProgressUpdate).TotalMilliseconds > 500)
                    {
                        pr.StatusDescription = status;
                        pr.PercentComplete = percent >= 0 ? percent : 0; // bilinmiyorsa 0 göster
                        WriteProgress(pr);
                        lastProgressUpdate = DateTime.Now;
                    }

                    Thread.Sleep(500);
                }

                // Çıkıştan sonra son bir tarama + progress 100
                progress.ScanFolderAndUpdate(DownloadOutput, (file, size) =>
                {
                    Logger.Write(LogPath, $"[FILE] Completed: {file} ({size} bytes)");
                });
                pr.StatusDescription = $"Finalizing... Downloaded: {FormatBytes(progress.CurrentBytes)}";
                pr.PercentComplete = 100;
                WriteProgress(pr);

                // Heuristik hata tespiti (önceki sürümde olduğu gibi)
                var combined = File.ReadAllLines(LogPath); // satır içinden error/fail ara
                var errorLines = combined.Where(l =>
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

                if (errorLines.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath) ?? ".");
                    File.WriteAllLines(ErrorLogPath, errorLines);
                    Logger.Write(LogPath, $"Download issues detected: {errorLines.Count}. Details: {ErrorLogPath}", LogLevel.WARN);
                    errorLogOut = ErrorLogPath;
                }

                // Not: p.ExitCode != 0 olsa bile İSTEYEREK devam ediyoruz (paketlemeyi deneriz)
                if (File.Exists(OutputZip)) File.Delete(OutputZip);
                Logger.Write(LogPath, $"Compressing downloads -> {OutputZip}");
                FileUtils.ZipDirectory(DownloadOutput, OutputZip);

                var dto = new OnlineDownloadResult { OutputZip = OutputZip, ErrorLog = errorLogOut };
                WriteObject(dto);
                Logger.Write(LogPath, "Online download completed.");
            }
            catch (Exception ex)
            {
                // Hatalarda MessageBox kesinlikle yok; sadece log ve PowerShell error stream.
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                // Devam etmeyi istiyorsanız, burada ThrowTerminatingError atmayın.
                // Ancak cmdlet semantiği gereği çağırana hata dönmek istenirse aşağıyı açın:
                // ThrowTerminatingError(new ErrorRecord(ex, "OnlineDownloadFailed", ErrorCategory.NotSpecified, this));
            }
            finally
            {
                if (!string.IsNullOrEmpty(work))
                {
                    try { FileUtils.SafeDeleteDirectory(work); } catch { }
                }

                // Progress kaydını kapat
                var close = new ProgressRecord(1, "Downloading ConfigMgr updates", "Done.") { RecordType = ProgressRecordType.Completed };
                WriteProgress(close);
            }
        }

        private void TryParseProgressFromLine(string line, DownloadProgress progress)
        {
            try
            {
                // 1) "Downloading file X of Y"
                var m1 = RxFileOfTotal.Match(line);
                if (m1.Success)
                {
                    var cur = int.Parse(m1.Groups["cur"].Value);
                    var tot = int.Parse(m1.Groups["tot"].Value);
                    if (tot > 0)
                    {
                        // dosya adedinden yüzde (yaklaşık) -> CurrentBytes bilgisi yine klasör taramasından gelecek
                        // Burada bir şey set etmeye gerek yok; Write-Progress yüzdesini ExpectedTotalBytes varsa tercih ediyoruz.
                    }
                }

                // 2) "A MB of B MB" veya "A GB of B GB"
                var m2 = RxBytesOfTotal.Match(line);
                if (m2.Success)
                {
                    var done = double.Parse(m2.Groups["done"].Value.Replace(',', '.'));
                    var tot = double.Parse(m2.Groups["tot"].Value.Replace(',', '.'));
                    var unit = m2.Groups[2].Value; // ilk yakalanan birim
                    var totalBytes = ToBytes(tot, unit);
                    if (totalBytes > 0) progress.ExpectedTotalBytes = totalBytes;
                }
            }
            catch
            {
                // parse hatalarını yok say
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024d * 1024):0.00} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024d:0} KB";
            return $"{bytes} B";
        }
    }
}
