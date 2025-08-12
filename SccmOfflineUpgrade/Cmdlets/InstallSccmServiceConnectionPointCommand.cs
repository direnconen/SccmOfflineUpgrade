using System;
using System.Management.Automation;

namespace SccmOfflineUpgrade
{
    [Cmdlet(VerbsLifecycle.Install, "SccmServiceConnectionPoint", SupportsShouldProcess = true)]
    public class InstallSccmServiceConnectionPointCommand : PSCmdlet
    {
        [Parameter(Mandatory = true)]
        public string SiteCode { get; set; } = string.Empty;

        [Parameter(Mandatory = true)]
        public string SiteSystemServerName { get; set; } = string.Empty;

        [Parameter]
        [ValidateSet("Offline", "Online")]
        public string Mode { get; set; } = "Offline";

        [Parameter]
        public string LogPath { get; set; } = @"C:\ProgramData\SccmOfflineUpgrade\logs\Install-SCP.log";

        protected override void ProcessRecord()
        {
            Logger.Write(LogPath, $"Install-SccmServiceConnectionPoint starting: SiteCode={SiteCode}, Server={SiteSystemServerName}, Mode={Mode}");
            try
            {
                if (!ShouldProcess($"{SiteSystemServerName} ({SiteCode})", $"Configure SCP mode={Mode}"))
                    return;

                // ConfigMgr PowerShell cmdlet'lerini güvenilir çalıştırmak için dış PowerShell süreci.
                var psCmd = $@"
Import-Module ConfigurationManager -ErrorAction Stop;
if (-not (Get-PSDrive -PSProvider CMSite -Name '{SiteCode}' -ErrorAction SilentlyContinue)) {{
  $siteServer = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\SMS\Identification' -ErrorAction SilentlyContinue).'Site Server Name';
  if (-not $siteServer) {{ $siteServer = $env:COMPUTERNAME }}
  New-PSDrive -Name '{SiteCode}' -PSProvider CMSite -Root $siteServer | Out-Null
}}
Set-Location '{SiteCode}:\';
$scp = Get-CMServiceConnectionPoint -SiteSystemServerName '{SiteSystemServerName}' -SiteCode '{SiteCode}' -ErrorAction SilentlyContinue;
if (-not $scp) {{
  Add-CMServiceConnectionPoint -SiteSystemServerName '{SiteSystemServerName}' -SiteCode '{SiteCode}' -Mode {Mode} | Out-Null
}} else {{
  Set-CMServiceConnectionPoint -SiteSystemServerName '{SiteSystemServerName}' -SiteCode '{SiteCode}' -Mode {Mode} | Out-Null
}}";
                var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psCmd.Replace("\"", "\\\"")}\"";
                var res = ProcessRunner.Run("powershell.exe", args);
                if (res.ExitCode != 0)
                {
                    Logger.Write(LogPath, res.StdErr, LogLevel.ERROR);
                    ThrowTerminatingError(new ErrorRecord(new Exception($"SCP install failed. ExitCode={res.ExitCode}"), "SCPInstallFailed", ErrorCategory.InvalidOperation, this));
                }
                Logger.Write(LogPath, "SCP configured successfully.");
            }
            catch (Exception ex)
            {
                Logger.Write(LogPath, $"ERROR: {ex.Message}", LogLevel.ERROR);
                ThrowTerminatingError(new ErrorRecord(ex, "SCPInstallException", ErrorCategory.NotSpecified, this));
            }
        }
    }
}
