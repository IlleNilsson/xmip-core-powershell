#
# The operator module's manifest. RootModule is the compiled assembly — this is
# a binary module, per ADR-0014: cmdlets and objects over the ABI, never a
# subprocess and never scraped JSON.
#
@{
    RootModule           = 'Xmip.PowerShell.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = '7c3d9f81-2e46-4b0a-9d15-8f6a1c24e7b3'
    Author               = 'Ilian Nilsson'
    CompanyName          = 'Xmip'
    Copyright            = 'Copyright (c) Ilian Nilsson. Licensed AGPL-3.0-or-later.'
    Description          = 'Operate Xmip from PowerShell: the module boundary, its statuses, and what a loadable module says it is.'

    PowerShellVersion    = '7.6.5'
    CompatiblePSEditions = @('Core')

    CmdletsToExport      = @(
        'Get-XmipAbi'
        'ConvertFrom-XmipStatus'
        'Get-XmipModuleDescriptor'
    )
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
