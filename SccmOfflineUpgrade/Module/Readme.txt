SccmOfflineUpgrade PowerShell Module
------------------------------------

Version: 1.0.4
Author: Arksoft Bilisim
Description:
    This PowerShell module provides cmdlets to export, download, and import SCCM upgrade packages
    in environments with no direct internet access (offline/air-gapped SCCM scenarios).
    The module automates the process of preparing an offline package, downloading updates on an online machine,
    and importing the updates back to the offline SCCM site for upgrade.

================================================================================
Cmdlets Overview
================================================================================

1) Export-SccmOfflinePackage
   Prepares the SCCM usage data CAB and service connection tool package for offline transfer.

   PARAMETERS:
   -OutputZip <String>            : Path for the output ZIP file containing the SCCM export.
   -LogPath <String>              : Path to log file.
   -ServiceConnectionToolPath <String>
                                   : Path to ServiceConnectionTool.exe or its directory.
   -StagingRoot <String>          : Temporary working directory for staging files.
   -UsageDataCabName <String>     : Name of the CAB file to generate (default: UsageData.cab).

   USAGE EXAMPLE:
   Export-SccmOfflinePackage `
       -OutputZip "C:\Offline\Export-SccmOfflinePackage.zip" `
       -LogPath "C:\Offline\Export.log" `
       -ServiceConnectionToolPath "E:\ConfigMgr\cd.latest\SMSSETUP\TOOLS\ServiceConnectionTool" `
       -StagingRoot "C:\Offline" `
       -UsageDataCabName "Export-SccmOfflinePackage.cab"

--------------------------------------------------------------------------------

2) Invoke-SccmOnlineDownload
   Runs the ServiceConnectionTool in download mode on an internet-connected system to fetch all required updates.

   PARAMETERS:
   -InputZip <String>              : Path to ZIP file produced by Export-SccmOfflinePackage.
   -OutputZip <String>             : Path for the output ZIP containing downloaded update files.
   -LogPath <String>                : Path to log file.
   -DownloadOutput <String>        : Directory where ServiceConnectionTool stores the downloaded files.
   -IncludeOdbc18 [Switch]         : Include Microsoft ODBC Driver 18 installer in the output ZIP.
   -EnsureOdbc18 [Switch]          : Check and silently install ODBC 18 if not present.

   USAGE EXAMPLE:
   Invoke-SccmOnlineDownload `
       -InputZip "C:\Offline\Export-SccmOfflinePackage.zip" `
       -OutputZip "C:\Offline\SccmUpdateContent.zip" `
       -LogPath "C:\Offline\Download.log" `
       -DownloadOutput "C:\Offline\Download" # -IncludeOdbc18 -EnsureOdbc18

--------------------------------------------------------------------------------

3) Import-SccmOfflineUpdates
   Imports the downloaded update files back into the offline SCCM site and initiates the upgrade.

   PARAMETERS:
   -InputZip <String>         : Path to ZIP file produced by Invoke-SccmOnlineDownload.
   -LogPath <String>          : Path to log file.
   -SiteCode <String>         : SCCM site code (e.g., P01).
   -SiteServer <String>       : FQDN of the SCCM site server.
   -TriggerUpgrade [Switch]   : If set, automatically trigger the upgrade after import.

   USAGE EXAMPLE:
   Import-SccmOfflineUpdates `
       -InputZip "C:\Offline\SccmUpdateContent.zip" `
       -LogPath "C:\Offline\Import.log" `
       -SiteCode "ARK" `
       -SiteServer "arksccm.arksoft.local" `
       -TriggerUpgrade

================================================================================
Usage Scenarios
================================================================================

Scenario 1 - Full Offline Upgrade Workflow
------------------------------------------
1. On the offline SCCM server:
   Export-SccmOfflinePackage -OutputZip "C:\Offline\Export-SccmOfflinePackage.zip" -LogPath "C:\Offline\Export.log" -ServiceConnectionToolPath "E:\ConfigMgr\cd.latest\SMSSETUP\TOOLS\ServiceConnectionTool" -StagingRoot "C:\Offline"

2. Transfer "Export-SccmOfflinePackage.zip" to an internet-connected machine.

3. On the online machine:
   Invoke-SccmOnlineDownload -InputZip "C:\Offline\Export-SccmOfflinePackage.zip" -OutputZip "C:\Offline\SccmUpdateContent.zip" -LogPath "C:\Offline\Download.log" -DownloadOutput "C:\Offline\Download" -IncludeOdbc18 -EnsureOdbc18

4. Transfer "SccmUpdateContent.zip" back to the offline SCCM server.

5. On the offline SCCM server:
   Import-SccmOfflineUpdates -InputZip "C:\Offline\SccmUpdateContent.zip" -LogPath "C:\Offline\Import.log" -SiteCode "P01" -SiteServer "SCCMSERVER01.domain.local" -TriggerUpgrade

================================================================================
Full Change Log
================================================================================

Version 1.0.0 - Initial Release
- Added Export-SccmOfflinePackage, Invoke-SccmOnlineDownload, Import-SccmOfflineUpdates cmdlets.
- Implemented logging and ZIP packaging for offline workflow.
- Basic error handling for missing files.

Version 1.0.1
- Improved download progress tracking and error handling.
- Added proxy support.
- Fixed temp folder cleanup.

Version 1.0.2
- Parameter validation improvements.
- More consistent logging output.
- Fixed unlock issues when CAB not generated.

Version 1.0.3
- Added ChangeLog.txt to module manifest.
- Updated README with examples.
- Enhanced Import cmdlet upgrade triggering.

Version 1.0.4 – 2025-08-12
[New Features]
- Integrated ODBC 18 handling (IncludeOdbc18, EnsureOdbc18).
- Suppressed all Windows error dialogs.
- Per-file completion logging.
- Enhanced progress tracking (stdout + folder scan).
- Auto-normalized CAB name.

[Improvements]
- SCT runs from InputZip directly.
- Expanded error logging for failed/small files.
- Better temp folder cleanup.
- Improved proxy handling.

[Fixes]
- Fixed premature "completed" issue.
- Fixed CAB rename issue.
- Fixed empty message box bug.
- Fixed InputZip validation handling.
