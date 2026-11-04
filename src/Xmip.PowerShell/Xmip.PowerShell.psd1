#
# The operator module's manifest. RootModule is the compiled assembly — this is
# a binary module, per ADR-0014: cmdlets and objects over the ABI, never a
# subprocess and never scraped JSON.
#
@{
    RootModule           = 'Xmip.PowerShell.psm1'
    RequiredAssemblies    = @('Xmip.PowerShell.dll')
    NestedModules         = @('Xmip.PowerShell.dll')
    FormatsToProcess      = @('Xmip.PowerShell.Format.ps1xml')
    ModuleVersion        = '0.3.0'
    GUID                 = '7c3d9f81-2e46-4b0a-9d15-8f6a1c24e7b3'
    Author               = 'Ilian Nilsson'
    CompanyName          = 'Xmip'
    Copyright            = 'Copyright (c) Ilian Nilsson. Licensed AGPL-3.0-or-later.'
    Description          = 'Operate Xmip from PowerShell and show its live health in the prompt through the shared operator surface.'

    PowerShellVersion    = '7.7.0'
    CompatiblePSEditions = @('Core')

    CmdletsToExport      = @(
        'Get-XmipAbi'
        'ConvertFrom-XmipStatus'
        'Get-XmipModuleDescriptor'
        'Get-XmipHealth'
        'Get-XmipScope'
        'Test-XmipNodeConfiguration'
        'New-XmipCertificate'
        'Get-XmipCertificate'
        'Get-XmipDatabaseScript'
        'Suspend-XmipScope'
        'Resume-XmipScope'
        'Get-XmipAudit'
        'Get-XmipEventSubscription'
        'Get-XmipSubscription'
        'Get-XmipDeadMessage'
        'Set-XmipSecret'
    )
    FunctionsToExport    = @()
    VariablesToExport    = @()
    AliasesToExport      = @()

    # What a gallery shows of the module: the Xmip mark, from the estate's
    # one source of it (asset/brand/brand.toml).
    PrivateData          = @{
        PSData = @{
            IconUri = 'https://raw.githubusercontent.com/IlleNilsson/Xmip/main/asset/brand/icon/xmip-x-128.png'
        }
    }
}
