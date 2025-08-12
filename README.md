SccmOfflineUpgrade PowerShell Module
------------------------------------

Version 1.0.6 – 2025-08-12
Author: Arksoft Bilisim

[New Features]
- Module DLL digitally signed to ensure integrity and authenticity.

Description:
This PowerShell module provides cmdlets to export, download, and import SCCM upgrade packages in environments with no direct internet access (offline/air-gapped SCCM scenarios).
The module automates the process of preparing an offline package, downloading updates on an online machine, and importing the updates back to the offline SCCM site for upgrade.

Cmdlets Overview
================

1) Export-SccmOfflinePackage
----------------------------
Prepares the SCCM usage data CAB and service connection tool package for offline transfer.

PARAMETERS:
- -OutputZip <String> – Path for the output ZIP file containing the SCCM export.
- -LogPath <String> – Path to log file.
- -ServiceConnectionToolPath <String> – Path to ServiceConnectionTool.exe or its directory.
- -StagingRoot <String> – Temporary working directory for staging files.
- -UsageDataCabName <String> – Name of the CAB file to generate (default: UsageData.cab).

2) Invoke-SccmOnlineDownload
-----------------------------
Runs the ServiceConnectionTool in download mode on an internet-connected system to fetch all required updates.

PARAMETERS:
- -InputZip <String> – Path to ZIP file produced by Export-SccmOfflinePackage.
- -OutputZip <String> – Path for the output ZIP containing downloaded update files.
- -LogPath <String> – Path to log file.
- -DownloadOutput <String> – Directory where ServiceConnectionTool stores the downloaded files.
- -IncludeOdbc18 [Switch] – Include Microsoft ODBC Driver 18 installer in the output ZIP.
- -EnsureOdbc18 [Switch] – Check and silently install ODBC 18 if not present.

3) Import-SccmOfflineUpdates
-----------------------------
Imports the downloaded update files back into the offline SCCM site and initiates the upgrade.

PARAMETERS:
- -InputZip <String> – Path to ZIP file produced by Invoke-SccmOnlineDownload.
- -LogPath <String> – Path to log file.
- -SiteCode <String> – SCCM site code (e.g., P01).
- -SiteServer <String> – FQDN of the SCCM site server.
- -TriggerUpgrade [Switch] – Automatically trigger upgrade after import.

Usage Scenario – Full Offline Upgrade
=====================================

1. On the offline SCCM server:
   Export-SccmOfflinePackage `
       -OutputZip "C:\Offline\Export.zip" `
       -LogPath "C:\Offline\Export.log" `
       -ServiceConnectionToolPath "E:\ConfigMgr\cd.latest\SMSSETUP\TOOLS\ServiceConnectionTool" `
       -StagingRoot "C:\Offline"

2. Transfer the exported ZIP to an online machine and run:
   Invoke-SccmOnlineDownload `
       -InputZip "C:\Offline\Export.zip" `
       -OutputZip "C:\Offline\SccmUpdateContent.zip" `
       -LogPath "C:\Offline\Download.log" `
       -DownloadOutput "C:\Offline\Download" `
       -IncludeOdbc18 `
       -EnsureOdbc18

3. Transfer the downloaded ZIP back to the offline SCCM server and run:
   Import-SccmOfflineUpdates `
       -InputZip "C:\Offline\SccmUpdateContent.zip" `
       -LogPath "C:\Offline\Import.log" `
       -SiteCode "P01" `
       -SiteServer "SCCMSERVER01.domain.local" `
       -TriggerUpgrade


Full Change Log
===============
Version 1.0.6 – 2025-08-12
[New Features]
- Module DLL digitally signed to ensure integrity and authenticity.

[Improvements]
- Security enhancement by providing signed binary verification.

Version 1.0.5 – 2025-08-12
[New Features]
- Import: Added SiteServer parameter and automatic SMS Provider discovery.
- Import: Optional ODBC 18 ensure-from-package refined; prereq and upgrade logging improved.
- Import: Normalizes PackageGuid to {GUID} format; clearer warnings for missing objects.
- Import: Added parameter alias support (StagingRoot for ExtractTo, Trigger/StartUpgrade aliases).
- Download: Improved .NET Framework ZIP extraction compatibility.
- Download: Enhanced progress parsing (folder size scan + stdout).
- General: Better temp cleanup, UNC path handling, and suppressed OS dialogs globally.

[Improvements]
- More robust WMI connection with Impersonation and PacketPrivacy.
- Aligned parameter naming with README documentation.
- Improved resilience in mixed OS environments.

[Fixes]
- Fixed ODBC 18 silent install logic in some restricted environments.
- Fixed rare issue where SCT progress would freeze after large CAB extraction.

Version 1.0.4 – 2025-08-12
- Invoke-SccmOnlineDownload: CAB name auto-fix, progress bar, per-file logging, error UI suppressed.
- ODBC 18: Include/ensure options; silent install if missing; MSI added to package.

Version 1.0.3
- Export: CAB fallback search, working directory fix, EXE-or-folder path resolution.

Version 1.0.2
- Parameter validation, timestamped logs, refined error detection.

Version 1.0.1
- Improved progress parsing and resilience during partial download failures.

Version 1.0.0
- Initial Prepare → Connect → Import flow with logging and packaging.
