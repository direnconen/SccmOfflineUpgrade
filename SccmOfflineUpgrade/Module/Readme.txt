SccmOfflineUpgrade v1.0.0
================================

Author : Arksoft Bilisim
Module : SccmOfflineUpgrade.psm1
Description : Automates the end-to-end "Prepare → Connect → Import" process for upgrading (in-console update) packages in offline (air-gapped) SCCM/ConfigMgr environments.
Target Audience : SCCM Administrators and DevOps Teams
License : Internal

CONTENTS
Overview

Prerequisites

Installation and Module Loading

Commands (Cmdlets) and Parameters
 4.1) Install-SccmServiceConnectionPoint
 4.2) Export-SccmOfflinePackage
 4.3) Invoke-SccmOnlineDownload
 4.4) Import-SccmOfflineUpdates

Usage Scenarios (End-to-End Flow)
 5.1) Offline Network: Prepare & Create Package
 5.2) Online Network: Connect & Download & Package
 5.3) Offline Network: Import & (Optional) Prereq Check & (Optional) Start Upgrade

Logging and Outputs

Error Handling and Troubleshooting

FAQ

Release Notes

1) OVERVIEW
This module automates the offline update method used to upgrade Microsoft Configuration Manager (SCCM/ConfigMgr) sites that have no internet access. The flow is:

PREPARE: On the offline SCCM server, generate usage data and prepare the transfer package.

CONNECT: On an internet-connected system, send the usage data to Microsoft and download update packages.

IMPORT: On the offline SCCM server, import the downloaded packages; optionally run a prereq check and trigger the upgrade.

The module can also:

Install and set the Service Connection Point (SCP) role to Offline/Online mode.

Log all steps in detail.

Record files that fail to download into a separate log file.

Automatically ZIP transfer and download packages.

2) PREREQUISITES
SCCM Console and/or ConfigMgr PowerShell module (for ConfigMgr cmdlets).

ServiceConnectionTool.exe (from the CD.Latest folder of the same version). The module attempts to auto-locate it.

Administrative privileges on the offline network side.

Internet access on the online network side; proxy details if required.

.NET Framework and required dependencies (per ServiceConnectionTool requirements).

Sufficient disk space (update downloads may be several GB).

3) INSTALLATION AND MODULE LOADING
Copy the module files into a folder (e.g., C:\Modules\SccmOfflineUpgrade\).

(Optional) Place the module manifest file (SccmOfflineUpgrade.psd1) in the same folder.

In PowerShell:

powershell
Copy
Edit
Import-Module "C:\Modules\SccmOfflineUpgrade\SccmOfflineUpgrade.psd1" -Force
Note: If you plan to use ConfigMgr cmdlets, either use the SCCM console shortcut “Connect via Windows PowerShell” or ensure the ConfigurationManager module is loaded.

4) COMMANDS (CMDLETS) AND PARAMETERS
4.1) Install-SccmServiceConnectionPoint
Description: Installs the Service Connection Point (SCP) role or changes its mode (Offline/Online).

Syntax:

powershell
Copy
Edit
Install-SccmServiceConnectionPoint -SiteCode <String> -SiteSystemServerName <String> [-Mode <Offline|Online>] [-LogPath <String>] [-WhatIf] [-Confirm]
Parameters:

-SiteCode (Required): SCCM site code (e.g., ABC).

-SiteSystemServerName (Required): Site system server name where the SCP role will be installed (FQDN recommended).

-Mode: 'Offline' or 'Online'. Default: Offline.

-LogPath: Log file path. Default: C:\ProgramData\SccmOfflineUpgrade\logs\Install-SCP.log

-WhatIf / -Confirm: Standard safety parameters.

Output: None. Logs success/failure details.

Example:

powershell
Copy
Edit
Install-SccmServiceConnectionPoint -SiteCode "ABC" -SiteSystemServerName "SCCM-PRI.contoso.local" -Mode Offline -Verbose
4.2) Export-SccmOfflinePackage
Description: Runs the PREPARE step on the offline SCCM server, producing a transfer ZIP containing the ServiceConnectionTool folder and UsageData.cab.

Syntax:

powershell
Copy
Edit
Export-SccmOfflinePackage -OutputZip <String> [-ServiceConnectionToolPath <String>] [-StagingRoot <String>] [-UsageDataCabName <String>] [-LogPath <String>]
Parameters:

-OutputZip (Required): Full path of the ZIP file to be transferred.

-ServiceConnectionToolPath: Path to the ServiceConnectionTool folder. If omitted, auto-locates from CD.Latest.

-StagingRoot: Temporary working folder. Default: %ProgramData%\SccmOfflineUpgrade\staging

-UsageDataCabName: Name of the usage data CAB file. Default: UsageData.cab

-LogPath: Log file path. Default: %ProgramData%\SccmOfflineUpgrade\logs\Export-SccmOfflinePackage.log

Output: Returns the -OutputZip path as a string.

Example:

powershell
Copy
Edit
$zip1 = Export-SccmOfflinePackage -OutputZip "D:\XFER\CM-UsageData-And-Tool.zip" -Verbose
4.3) Invoke-SccmOnlineDownload
Description: Runs the CONNECT step on an internet-connected machine. Extracts the offline ZIP, uploads usage data, downloads updates, logs failed downloads separately, and packages all updates into one ZIP.

Syntax:

powershell
Copy
Edit
Invoke-SccmOnlineDownload -InputZip <String> -DownloadOutput <String> -OutputZip <String> [-Proxy <String>] [-LogPath <String>] [-ErrorLogPath <String>]
Parameters:

-InputZip (Required): Package from the offline network (output of Export-SccmOfflinePackage).

-DownloadOutput (Required): Folder where updates will be downloaded.

-OutputZip (Required): ZIP file to return to the offline network.

-Proxy: Proxy if required (e.g., http://user:pass@proxy:8080).

-LogPath: Log file path. Default: %ProgramData%\SccmOfflineUpgrade\logs\Invoke-SccmOnlineDownload.log

-ErrorLogPath: Log for failed downloads. Default: %ProgramData%\SccmOfflineUpgrade\logs\OnlineDownloadErrors.log

Output:
[pscustomobject] @{ OutputZip = <String>; ErrorLog = <String|null> }

Example:

powershell
Copy
Edit
$result = Invoke-SccmOnlineDownload `
    -InputZip "E:\CarryIn\CM-UsageData-And-Tool.zip" `
    -DownloadOutput "E:\CM\UpdatePacks" `
    -OutputZip "E:\CarryBack\CM-UpdatePacks.zip" `
    -Verbose
if ($result.ErrorLog) { Write-Host "Error details: $($result.ErrorLog)" }
4.4) Import-SccmOfflineUpdates
Description: Runs the IMPORT step on the offline SCCM server. Imports the downloaded packages; optionally runs a local prereq check and attempts a (best-effort) upgrade trigger.

Syntax:

powershell
Copy
Edit
Import-SccmOfflineUpdates -InputZip <String> [-ServiceConnectionToolPath <String>] [-ExtractTo <String>] [-RunPrereqCheck] [-CdLatestRoot <String>] [-TryStartUpgrade] [-SiteCode <String>] [-LogPath <String>]
Parameters:

-InputZip (Required): ZIP containing updates from the online network (Invoke-SccmOnlineDownload output).

-ServiceConnectionToolPath: Path to ServiceConnectionTool folder. If omitted, auto-locates from CD.Latest.

-ExtractTo: Extraction folder. Default: %ProgramData%\SccmOfflineUpgrade\import

-RunPrereqCheck: Runs prereqchk.exe /LOCAL from CD.Latest if found.

-CdLatestRoot: CD.Latest root path for prereqchk.exe. Defaults from setup registry.

-TryStartUpgrade: Attempts to trigger the newest “Available” in-console update via WMI (best-effort).

-SiteCode: Required for -TryStartUpgrade.

-LogPath: Log file path. Default: %ProgramData%\SccmOfflineUpgrade\logs\Import-SccmOfflineUpdates.log

Output: None. Logs success/failure details.

Example:

powershell
Copy
Edit
Import-SccmOfflineUpdates `
    -InputZip "D:\CarryBack\CM-UpdatePacks.zip" `
    -RunPrereqCheck `
    -TryStartUpgrade `
    -SiteCode "ABC" `
    -Verbose
5) USAGE SCENARIOS (END-TO-END FLOW)
5.1) Offline Network: Prepare & Create Package

(Optional) Install SCP role and set to Offline mode:

powershell
Copy
Edit
Install-SccmServiceConnectionPoint -SiteCode "ABC" -SiteSystemServerName "SCCM-PRI.contoso.local" -Mode Offline -Verbose
Prepare and create package:

powershell
Copy
Edit
$zip1 = Export-SccmOfflinePackage -OutputZip "D:\XFER\CM-UsageData-And-Tool.zip" -Verbose
Transfer the ZIP to the online network.

5.2) Online Network: Connect & Download & Package

Download and package:

powershell
Copy
Edit
$result = Invoke-SccmOnlineDownload `
    -InputZip "E:\CarryIn\CM-UsageData-And-Tool.zip" `
    -DownloadOutput "E:\CM\UpdatePacks" `
    -OutputZip "E:\CarryBack\CM-UpdatePacks.zip" `
    -Verbose
If $result.ErrorLog is populated, review the errors.

Transfer the ZIP back to the offline network.

5.3) Offline Network: Import & (Optional) Prereq & (Optional) Upgrade

Import, run prereq check, and attempt (best-effort) upgrade:

powershell
Copy
Edit
Import-SccmOfflineUpdates `
    -InputZip "D:\CarryBack\CM-UpdatePacks.zip" `
    -RunPrereqCheck `
    -TryStartUpgrade `
    -SiteCode "ABC" `
    -Verbose
Monitor:

Monitoring > Updates and Servicing Status

Logs: CMUpdate.log, ConfigMgrPrereq.log

6) LOGGING AND OUTPUTS
Default log directory: C:\ProgramData\SccmOfflineUpgrade\logs\

Install-SCP.log: SCP role + mode operations

Export-SccmOfflinePackage.log: PREPARE + package creation

Invoke-SccmOnlineDownload.log: CONNECT + download + packaging

OnlineDownloadErrors.log: Failed downloads (separate)

Import-SccmOfflineUpdates.log: IMPORT + (optional) prereq + (optional) upgrade

ZIP Outputs:

CM-UsageData-And-Tool.zip: Package to transfer from offline to online network

CM-UpdatePacks.zip: Package to transfer from online back to offline network

7) ERROR HANDLING AND TROUBLESHOOTING
ServiceConnectionTool.exe not found:

Verify CD.Latest directory is correct. Use -ServiceConnectionToolPath if needed.

Download failed / bad files:

Check OnlineDownloadErrors.log.

Consider proxy settings (-Proxy).

Check disk space and AV/IDS blocking.

Prereq check warnings:

Review ConfigMgrPrereq.log and prereqchk output. Fix missing roles/features.

Upgrade not triggering:

TryStartUpgrade is best-effort (not an official cmdlet). Use GUI “Install Update Pack” as fallback.

Review CMUpdate.log and Updates and Servicing Status.

8) FAQ
Q: Does the module depend on ConfigMgr cmdlets?
A: Yes, for SCP role management and auto-upgrade attempt. PREPARE/CONNECT/IMPORT works via ServiceConnectionTool.

Q: I’m behind a proxy, how do I configure it?
A: Use the -Proxy parameter in Invoke-SccmOnlineDownload (e.g., http://user:pass@proxy:8080).

Q: The packages are huge, what can I do?
A: Ensure the DownloadOutput drive has ample space; keep extra space for ZIP creation.

Q: Which logs should I watch?
A: Module logs plus SCCM logs: CMUpdate.log, ConfigMgrPrereq.log, dmpdownloader.log, hman.log.

9) RELEASE NOTES
v1.0.0

Initial release: PREPARE/CONNECT/IMPORT flows; SCP role management; separate logging of failed downloads; ZIP packaging; prereq check; best-effort upgrade trigger.