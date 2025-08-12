@{
    RootModule        = 'SccmOfflineUpgrade.dll'
    ModuleVersion     = '1.0.0'
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
}
