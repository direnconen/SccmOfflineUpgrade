@{
    RootModule        = 'SccmOfflineUpgrade.dll'
    ModuleVersion     = '1.0.4'
    GUID              = 'f2fdc8e8-5b2c-4f5a-8e57-1d8f1a5c2e31'
    Author            = 'Arksoft Bilisim'
    CompanyName       = 'Arksoft Bilisim'
    Description       = 'Offline SCCM upgrade cmdlets (Prepare/Connect/Import, packaging, logging).'
    PowerShellVersion = '5.1'
    FormatsToProcess  = @()
    TypesToProcess    = @()
    NestedModules     = @()
    FunctionsToExport = @()
    CmdletsToExport   = @(
        'Install-SccmServiceConnectionPoint',
        'Export-SccmOfflinePackage',
        'Invoke-SccmOnlineDownload',
        'Import-SccmOfflineUpdates'
    )

    # Aliases for shorter cmdlet names
    AliasesToExport   = @(
        'Install-SCP',
        'Export-SCPPkg',
        'Invoke-SCPDownload',
        'Import-SCPUpd'
    )

    # Additional files to include in the module package
    FileList          = @(
        'readme.txt'
    )

    PrivateData = @{
        PSData = @{
            ReleaseNotes = @'
v1.0.4 (2025-08-12)
- Invoke-SccmOnlineDownload: CAB name auto-fix, progress bar, per-file logging, error UI suppressed.
- ODBC 18: Include/ensure options; silent install if missing; MSI added to package.

v1.0.3
- Export: CAB fallback search, working directory fix, EXE-or-folder path resolution.

v1.0.2
- Parameter validation, timestamped logs, refined error detection.

v1.0.1
- Improved progress parsing and resilience during partial download failures.

v1.0.0
- Initial Prepare → Connect → Import flow with logging and packaging.
'@
        }
    }
}
