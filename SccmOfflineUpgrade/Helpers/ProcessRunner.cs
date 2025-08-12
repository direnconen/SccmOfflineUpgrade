using System;
using System.Diagnostics;
using System.Text;

namespace SccmOfflineUpgrade
{
    internal sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
    }

    internal static class ProcessRunner
    {
        public static ProcessResult Run(string filePath, string arguments, int timeoutSeconds = 0, string? workingDirectory = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            using var p = new Process { StartInfo = psi };
            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (timeoutSeconds > 0)
            {
                if (!p.WaitForExit(timeoutSeconds * 1000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException($"Process timeout: {filePath} {arguments}");
                }
            }
            else
            {
                p.WaitForExit();
            }

            return new ProcessResult { ExitCode = p.ExitCode, StdOut = sbOut.ToString(), StdErr = sbErr.ToString() };
        }
    }
}
