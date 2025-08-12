// File: Cmdlets/InvokeSccmOnlineDownloadCommand.cs
// Namespace: SccmOfflineUpgrade
//
// Features:
// - Uses ServiceConnectionTool from the input ZIP (no external path needed)
// - Normalizes CAB name to "UsageData.cab"
// - Optional ODBC 18 packaging (IncludeOdbc18) and ensure-install (EnsureOdbc18)
// - Progress bar (Write-Progress) with both stdout parsing and folder-size estimation
// - Per-file completion logging
// - Suppresses any interactive dialogs (MessageBox, OS error boxes)
// - Continues best-effort; writes error details to log; validates non-empty download
//
// Requirements: Logger, FileUtils, OdbcChecker, Native, DownloadProgress helpers must exist in the project.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Threading;

namespace SccmOfflineUpgrade
{
    [Cmdlet(VerbsLifecycle.Invoke, "SccmOnlineDownload")]
    [OutputType(typeof(OnlineDownloadResult))]
    public class InvokeSccmOnlineDownloadCommand : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string InputZip { get; set; } = string.Empty;

        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string DownloadOutput { get; set; } = string.Empty;

        [Parameter(Mandatory = true)]
        [ValidateNotNullOrEmpty]
        public string OutputZip { get; set; } = string.Empty;

        [Parameter]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Invoke-SccmOnlineDownload.log";

        [Parameter]
        public string ErrorLogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\OnlineDownloadErrors.log";

        /// <summary>
        /// Download sırasında ODBC 18 MSI'sini pakete Prereqs\ODBC18 altına ekler.
        /// </summary>
        [Parameter]
        public SwitchParameter IncludeOdbc18 { get; set; }

        /// <summary>
        /// Online makinede ODBC 18 kurulu değilse indirip sessiz kurar (önerilir).
        /// </summary>
        [Parameter]
        public SwitchParameter EnsureOdbc18 { get; set; } = true;

        /// <summary>
        /// Gerekirse proxy (örn. http://user:pass@proxy:8080).
        /// </summary>
        [Parameter]
        public string? Proxy { get; set; }

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Invoke-SccmOnlineDownload starting. InputZip='{InputZip}'  Output='{DownloadOutput}'  Package='{OutputZip}'");
            Native.SuppressWindowsErrorDialogs(); // OS error UI'larını bastır

            string? workDir = null;
            var progress = new DownloadProgress();
            var pr = new ProgressRecord(1, "Downloading ConfigMgr updates", "Starting...");

            try
            {
                // --- Hazırlık
                if (!File.Exists(InputZip))
                    throw new FileNotFoundException("InputZip not found", InputZip);

                if (Directory.Exists(DownloadOutput))
                {
                    Logger.Write(LogPath, "Cleaning existing DownloadOutput...");
                    FileUtils.SafeDeleteDirectory(DownloadOutput);
                }
                Directory.CreateDirectory(DownloadOutput);

                workDir = Path.Combine(Path.GetTempPath(), "SccmOnline_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);

                Logger.Write(LogPath, $"Extract input zip -> {workDir}");
                FileUtils.Unzip(InputZip, workDir); // Projende mevcut helper (aksi halde ZipFile.ExtractToDirectory(InputZip, workDir))

                // ServiceConnectionTool'ü paketten bul
                var toolDir = Directory.EnumerateDirectories(workDir, "ServiceConnectionTool", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(toolDir))
                    throw new DirectoryNotFoundException("ServiceConnectionTool folder not found in the input package.");

                var toolExe = Path.Combine(toolDir, "ServiceConnectionTool.exe");
                if (!File.Exists(toolExe))
                    throw new FileNotFoundException("ServiceConnectionTool.exe not found in the package.", toolExe);

                var xferDir = Path.Combine(workDir, "Transfer");
                if (!Directory.Exists(xferDir))
                    throw new DirectoryNotFoundException("Transfer folder not found in the input package.");

                // UsageData.cab adı normalize et (araç çoğu sürümde bu adı bekler)
                var cabs = Directory.EnumerateFiles(xferDir, "*.cab", SearchOption.TopDirectoryOnly).ToList();
                if (cabs.Count == 0)
                    throw new FileNotFoundException("No CAB file found in Transfer folder (UsageData.cab expected).");

                var expectedCab = Path.Combine(xferDir, "UsageData.cab");
                if (!cabs.Any(f => f.EndsWith("UsageData.cab", StringComparison.OrdinalIgnoreCase)))
                {
                    File.Copy(cabs[0], expectedCab, true);
                    Logger.Write(LogPath, $"Renamed CAB to UsageData.cab (from {Path.GetFileName(cabs[0])}).");
                }

                // (Opsiyonel) ODBC 18 MSI'ini pakete ekle
                if (IncludeOdbc18.IsPresent)
                {
                    TryAddOdbcMsiToPackage(Proxy, Path.Combine(DownloadOutput, "Prereqs", "ODBC18"));
                }

                // (Önerilen) ODBC 18 kurulu değilse indir + sessiz kur
                if (EnsureOdbc18.IsPresent && !OdbcChecker.IsOdbc18Installed())
                {
                    EnsureOdbcInstalled(Proxy, Path.Combine(DownloadOutput, "Prereqs", "ODBC18"));
                }

                // --- ServiceConnectionTool -connect
                var args = $"-connect -usagedatasrc \"{xferDir}\" -updatepackdest \"{DownloadOutput}\"";
                if (!string.IsNullOrWhiteSpace(Proxy))
                    args += $" -proxy \"{Proxy}\"";

                Logger.Write(LogPath, $"Run: {toolExe} {args}");
                var psi = new ProcessStartInfo
                {
                    FileName = toolExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = toolDir
                };

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    Logger.Write(LogPath, e.Data, LogLevel.DEBUG);

                    // stdout'tan yüzdelik yakalamaya çalış
                    var pct = progress.ParsePercent(e.Data);
                    if (pct.HasValue)
                    {
                        pr.StatusDescription = e.Data;
                        pr.PercentComplete = Math.Min(100, Math.Max(0, pct.Value));
                        WriteProgress(pr);
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    Logger.Write(LogPath, e.Data, LogLevel.WARN);
                    // Hatalar da ilerleme ipucu içerebilir
                    var pct = progress.ParsePercent(e.Data);
                    if (pct.HasValue)
                    {
                        pr.StatusDescription = e.Data;
                        pr.PercentComplete = Math.Min(100, Math.Max(0, pct.Value));
                        WriteProgress(pr);
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // İlerlemeyi klasör taramasıyla da güncelle (500ms)
                var lastProgressUpdate = DateTime.MinValue;
                while (!proc.HasExited)
                {
                    progress.ScanFolderAndUpdate(DownloadOutput, (file, size) =>
                    {
                        Logger.Write(LogPath, $"[FILE] Completed: {file} ({size} bytes)");
                    });

                    var est = progress.GetPercent();
                    if ((DateTime.Now - lastProgressUpdate).TotalMilliseconds > 500)
                    {
                        if (est >= 0)
                        {
                            pr.StatusDescription = $"Downloaded {FormatBytes(progress.CurrentBytes)} of ~{FormatBytes(progress.ExpectedTotalBytes ?? 0)}";
                            pr.PercentComplete = est;
                        }
                        else
                        {
                            pr.StatusDescription = $"Downloaded {FormatBytes(progress.CurrentBytes)}";
                            pr.PercentComplete = 0;
                        }
                        WriteProgress(pr);
                        lastProgressUpdate = DateTime.Now;
                    }

                    Thread.Sleep(500);
                }

                // Son defa tarayıp %100 yap
                progress.ScanFolderAndUpdate(DownloadOutput, (file, size) =>
                {
                    Logger.Write(LogPath, $"[FILE] Completed: {file} ({size} bytes)");
                });
                pr.StatusDescription = $"Finalizing... Downloaded {FormatBytes(progress.CurrentBytes)}";
                pr.PercentComplete = 100;
                WriteProgress(pr);

                Logger.Write(LogPath, $"ServiceConnectionTool exit code: {proc.ExitCode}");

                // Başarı kriteri: en az birkaç MB veya bilinen klasörler
                var hasUpdates = Directory.Exists(Path.Combine(DownloadOutput, "Updates"));
                var hasRedist = Directory.Exists(Path.Combine(DownloadOutput, "Redist"));
                if (progress.CurrentBytes < 5L * 1024 * 1024 && !hasUpdates && !hasRedist)
                {
                    Logger.Write(LogPath, "No downloaded content detected (size < 5MB and no Updates/Redist).", LogLevel.ERROR);
                }

                // Hata satırlarını çıkar (heuristic)
                try
                {
                    var lines = File.ReadAllLines(LogPath);
                    var errs = lines.Where(l =>
                        l.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                    ).ToList();

                    foreach (var f in Directory.EnumerateFiles(DownloadOutput, "*", SearchOption.AllDirectories))
                    {
                        var info = new FileInfo(f);
                        if (info.Length == 0) errs.Add($"Zero-length file: {f}");
                    }

                    if (errs.Count > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath) ?? ".");
                        File.WriteAllLines(ErrorLogPath, errs);
                        Logger.Write(LogPath, $"Download issues detected: {errs.Count}. See: {ErrorLogPath}", LogLevel.WARN);
                    }
                }
                catch (Exception exErr)
                {
                    Logger.Write(LogPath, $"Error while generating error log: {exErr.Message}", LogLevel.WARN);
                }

                // DownloadOutput'u zip'le
                if (File.Exists(OutputZip)) File.Delete(OutputZip);
                Logger.Write(LogPath, $"Compressing download folder -> {OutputZip}");
                FileUtils.ZipDirectory(DownloadOutput, OutputZip);

                // Sonuç döndür
                var result = new OnlineDownloadResult
                {
                    OutputZip = OutputZip,
                    ErrorLog = File.Exists(ErrorLogPath) ? ErrorLogPath : null
                };
                WriteObject(result);
                Logger.Write(LogPath, "Invoke-SccmOnlineDownload completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"Invoke-SccmOnlineDownload failed: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "InvokeFailed", ErrorCategory.NotSpecified, this));
            }
            finally
            {
                // Progress’i kapat
                var close = new ProgressRecord(1, "Downloading ConfigMgr updates", "Done.")
                {
                    RecordType = ProgressRecordType.Completed
                };
                WriteProgress(close);

                // Geçici klasörü temizle
                if (!string.IsNullOrEmpty(workDir))
                {
                    try { FileUtils.SafeDeleteDirectory(workDir); } catch { /* ignore */ }
                }
            }
        }

        private void TryAddOdbcMsiToPackage(string? proxy, string targetFolder)
        {
            try
            {
                Directory.CreateDirectory(targetFolder);
                var outPath = Path.Combine(targetFolder, "msodbcsql18.msi");
                Logger.Write(LogPath, $"Downloading ODBC 18 MSI -> {outPath}");

                using var wc = new WebClient();
                if (!string.IsNullOrWhiteSpace(proxy))
                    wc.Proxy = new WebProxy(proxy);

                wc.DownloadFile("https://go.microsoft.com/fwlink/?linkid=2220989", outPath);

                var fi = new FileInfo(outPath);
                if (!fi.Exists || fi.Length < 100 * 1024)
                    Logger.Write(LogPath, $"Downloaded ODBC MSI seems too small: {fi.Length} bytes", LogLevel.WARN);
                else
                    Logger.Write(LogPath, "ODBC 18 MSI added to package.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"Could not download ODBC 18 MSI: {ex.Message}", LogLevel.WARN);
            }
        }

        private void EnsureOdbcInstalled(string? proxy, string stagingFolder)
        {
            try
            {
                if (OdbcChecker.IsOdbc18Installed())
                {
                    Logger.Write(LogPath, "ODBC 18 already installed.");
                    return;
                }

                Directory.CreateDirectory(stagingFolder);
                var msiPath = Path.Combine(stagingFolder, "msodbcsql18.msi");

                Logger.Write(LogPath, $"ODBC 18 not found. Downloading to {msiPath}");
                using (var wc = new WebClient())
                {
                    if (!string.IsNullOrWhiteSpace(proxy))
                        wc.Proxy = new WebProxy(proxy);

                    wc.DownloadFile("https://go.microsoft.com/fwlink/?linkid=2220989", msiPath);
                }

                Logger.Write(LogPath, "Installing ODBC 18 silently...");
                OdbcChecker.InstallOdbc18Msi(msiPath, LogPath);

                if (OdbcChecker.IsOdbc18Installed())
                    Logger.Write(LogPath, "ODBC 18 installation verified.");
                else
                    Logger.Write(LogPath, "ODBC 18 install check failed after MSI.", LogLevel.WARN);
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"EnsureOdbcInstalled failed: {ex.Message}", LogLevel.WARN);
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

    public sealed class OnlineDownloadResult
    {
        public string OutputZip { get; set; } = string.Empty;
        public string? ErrorLog { get; set; }
    }
}
