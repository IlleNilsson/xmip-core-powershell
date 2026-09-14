#
# The operator module's manifest. RootModule is the compiled assembly — this is
# a binary module, per ADR-0014: cmdlets and objects over the ABI, never a
# subprocess and never scraped JSON.
#
@{
    RootModule           = 'Xmip.PowerShell.psm1'
    RequiredAssemblies    = @('Xmip.PowerShell.dll')
    NestedModules         = @('Xmip.PowerShell.dll')
    ModuleVersion        = '1.0.0'
    GUID                 = '7c3d9f81-2e46-4b0a-9d15-8f6a1c24e7b3'
    Author               = 'Ilian Nilsson'
    CompanyName          = 'Xmip'
    Copyright            = 'Copyright (c) Ilian Nilsson. Licensed AGPL-3.0-or-later.'
    Description          = 'Operate Xmip from PowerShell and show its live health in the prompt through the shared operator surface.'

    PowerShellVersion    = '7.6.5'
    CompatiblePSEditions = @('Core')

    CmdletsToExport      = @(
        'Get-XmipRuntime'
        'Test-XmipRuntime'
        'Set-XmipRuntime'
    )
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()
}
