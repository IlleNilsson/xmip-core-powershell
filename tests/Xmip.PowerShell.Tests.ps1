#requires -PSEdition Core
#requires -Version 7.6.5

<#
    The operator module had no tests, and ARCHITECTURE.md said it did.

    Two things are checked here and they are not the same thing.

    **The binding agrees with the header.** ADR-0012 clause 1 makes
    `include/xmip_module.h` normative and this assembly's C# a convenience over
    it, and the file says so in its own remarks: if the two ever differ, the
    header is right and this is a defect. A comment cannot enforce that. These
    tests parse the header and compare it to what the assembly actually
    exposes, so the defect fails a run rather than waiting for a module to
    return a status nobody handles.

    **The cmdlets answer.** Names, manifest agreement, and the objects they
    emit. Objects rather than text is the whole reason this surface exists
    (ADR-0014), so the tests assert on properties and never on rendering.

    ## Why this builds into a temporary directory

    A loaded binary module locks its own assembly. On 2026-09-03 a `dotnet
    build` in this repository failed ten retries because a pwsh session started
    the day before still held `bin/Debug/net10.0/Xmip.PowerShell.dll` — and the
    session belonged to the operator, not to the build.

    Running the tests out of `bin/` would make that worse: every test run would
    lock the file the next build has to write. So the suite builds a fresh copy
    into a per-run temporary directory and imports that. The repository's own
    output is never loaded and never locked, and `dotnet build` keeps working
    while a test session is open.

    The temporary copy is left behind on purpose. It is locked by the session
    that imported it, deleting it would fail, and a failed cleanup at the end of
    a green run reads as a broken suite.
#>

BeforeDiscovery {
    # Pester's own hook for anything a test name or a -Skip argument needs.
    #
    # Discovery runs before BeforeAll, so a value computed there is still $null
    # when the skip is decided: $null.Count is 0 and every header test skips on
    # a machine that has the header. That is what the first run of this file
    # did.
    #
    # The helpers are dot-sourced rather than declared at file scope. A
    # file-scope advanced function called from inside BeforeAll aborts the whole
    # container in Pester 6.1.0, with every test reported failed and no message
    # on any of them. HeaderStatus.ps1 carries the reduction.
    . (Join-Path $PSScriptRoot 'HeaderStatus.ps1')

    [string] $header =
        Join-Path $PSScriptRoot '../../../foundation/abi/include/xmip_module.h'

    $script:HeaderStatus = Get-XmipHeaderStatus -Path $header
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'HeaderStatus.ps1')

    [string] $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    [string] $script:Header =
        Join-Path $script:Root '../../foundation/abi/include/xmip_module.h'

    [hashtable] $script:HeaderStatus = Get-XmipHeaderStatus -Path $script:Header

    $script:Project = Join-Path $script:Root 'src/Xmip.PowerShell/Xmip.PowerShell.csproj'
    $script:Manifest = Join-Path $script:Root 'src/Xmip.PowerShell/Xmip.PowerShell.psd1'

    [string] $stamp = [System.Guid]::NewGuid().ToString('n').Substring(0, 8)
    $script:Output = Join-Path ([System.IO.Path]::GetTempPath()) "xmip-powershell-$stamp"

    & dotnet build $script:Project --output $script:Output --verbosity quiet --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Building $script:Project failed. Nothing below can mean anything."
    }

    Copy-Item -LiteralPath $script:Manifest -Destination $script:Output -Force

    Import-Module (Join-Path $script:Output 'Xmip.PowerShell.psd1') -Force

    $script:Module = Get-Module -Name Xmip.PowerShell
    $script:Declared = Import-PowerShellDataFile -Path $script:Manifest
}

AfterAll {
    Remove-Module -Name Xmip.PowerShell -Force -ErrorAction SilentlyContinue
}

Describe 'The module loads and exports what it says' {
    It 'exports every cmdlet the manifest names' {
        foreach ($name in $script:Declared.CmdletsToExport) {
            $script:Module.ExportedCmdlets.Keys |
                Should -Contain $name -Because "$name is in CmdletsToExport"
        }
    }

    It 'names every cmdlet it exports in the manifest' {
        # The other direction. A cmdlet the manifest does not list works when
        # the module is imported by path and vanishes when it is imported by
        # name from PSModulePath — the same defect the estate module had.
        foreach ($name in $script:Module.ExportedCmdlets.Keys) {
            $script:Declared.CmdletsToExport |
                Should -Contain $name -Because "$name is exported and unlisted"
        }
    }

    It 'uses an approved verb for every cmdlet' {
        [string[]] $approved = @((Get-Verb).Verb)

        foreach ($name in $script:Module.ExportedCmdlets.Keys) {
            $approved | Should -Contain ($name -split '-')[0] -Because "$name"
        }
    }

    It 'requires the PowerShell floor ADR-0021 states' {
        # 7.6.5 Core. A binary module that loads on an older host is a module
        # that will fail somewhere less obvious than the import.
        $script:Declared.PowerShellVersion | Should -Be '7.6.5'
        $script:Declared.CompatiblePSEditions | Should -Contain 'Core'
    }
}

Describe 'The binding agrees with the normative header' {
    It 'finds the header to compare against' -Skip:($script:HeaderStatus.Count -eq 0) {
        $script:HeaderStatus.Count | Should -BeGreaterThan 0
    }

    It 'knows every status the header defines' -Skip:($script:HeaderStatus.Count -eq 0) {
        # A status the header has and the binding does not is a code that
        # reaches an operator as "not a status this build knows" — the exact
        # failure ADR-0012 clause 1 says is a defect in the binding.
        foreach ($constant in $script:HeaderStatus.Keys) {
            [int] $code = $script:HeaderStatus[$constant]
            [PSObject] $answer = ConvertFrom-XmipStatus -Code $code

            [string] $because = "$constant is $code in the header"

            $answer.Name | Should -Not -Be 'Unknown' -Because $because
        }
    }

    It 'gives each status the name the header implies' -Skip:($script:HeaderStatus.Count -eq 0) {
        foreach ($constant in $script:HeaderStatus.Keys) {
            [int] $code = $script:HeaderStatus[$constant]
            [string] $expected = ConvertTo-XmipMemberName -Constant $constant
            [PSObject] $answer = ConvertFrom-XmipStatus -Code $code

            $answer.Name | Should -Be $expected -Because "XMIP_$constant is $code"
        }
    }

    It 'retries exactly what the header says is retryable' -Skip:($script:HeaderStatus.Count -eq 0) {
        # XMIP_STATUS_IS_RETRYABLE in the header: timeout, unavailable,
        # capacity, again. Not Io, which covers faults that repeat. Getting
        # this wrong makes xmip-core-resilience retry something that cannot
        # succeed, or give up on something that would have.
        [string[]] $retryable = @('E_TIMEOUT', 'E_UNAVAILABLE', 'E_CAPACITY', 'E_AGAIN')

        foreach ($constant in $script:HeaderStatus.Keys) {
            [int] $code = $script:HeaderStatus[$constant]
            [bool] $expected = $constant -in $retryable
            [PSObject] $answer = ConvertFrom-XmipStatus -Code $code

            $answer.Retryable | Should -Be $expected -Because "XMIP_$constant"
        }
    }

    It 'calls terminal exactly what the header says is terminal' -Skip:($script:HeaderStatus.Count -eq 0) {
        [string[]] $terminal = @('E_INTERNAL', 'E_PANIC')

        foreach ($constant in $script:HeaderStatus.Keys) {
            [int] $code = $script:HeaderStatus[$constant]
            [bool] $expected = $constant -in $terminal
            [PSObject] $answer = ConvertFrom-XmipStatus -Code $code

            $answer.Terminal | Should -Be $expected -Because "XMIP_$constant"
        }
    }

    It 'speaks the ABI version the header declares' -Skip:($script:HeaderStatus.Count -eq 0) {
        [string] $header = Join-Path $script:Root '../../foundation/abi/include/xmip_module.h'
        [string] $text = Get-Content -LiteralPath $header -Raw

        $text | Should -Match '#define\s+XMIP_ABI_VERSION\s+1u?'
        (Get-XmipAbi).AbiVersion | Should -Be 1
    }

    It 'names the entrypoint the header declares' -Skip:($script:HeaderStatus.Count -eq 0) {
        [string] $header = Join-Path $script:Root '../../foundation/abi/include/xmip_module.h'
        [string] $text = Get-Content -LiteralPath $header -Raw

        $text | Should -Match 'xmip_create_module_v1'
        (Get-XmipAbi).Entrypoint | Should -Be 'xmip_create_module_v1'
    }
}

Describe 'ConvertFrom-XmipStatus' {
    It 'explains success as success' {
        [PSObject] $ok = ConvertFrom-XmipStatus -Code 0

        $ok.Name | Should -Be 'Ok'
        $ok.Retryable | Should -BeFalse
        $ok.Terminal | Should -BeFalse
    }

    It 'says so rather than throwing on a code it does not know' {
        # A number that is not a status is data, not an exception. A module
        # returning something unexpected must not take the operator's session
        # down with it.
        [PSObject] $answer = ConvertFrom-XmipStatus -Code 4711

        $answer.Name | Should -Be 'Unknown'
        $answer.Code | Should -Be 4711
    }

    It 'takes codes from the pipeline, one object out per code' {
        [PSObject[]] $answer = @(0, -1, -21 | ConvertFrom-XmipStatus)

        $answer.Count | Should -Be 3
        $answer[0].Name | Should -Be 'Ok'
        $answer[1].Name | Should -Be 'Invalid'
        $answer[2].Name | Should -Be 'Timeout'
    }

    It 'emits objects rather than text' {
        # ADR-0014's reason for this surface existing. A rendered string
        # cannot be filtered, compared or piped, and a caller that has to
        # parse one is back to scraping.
        [PSObject] $answer = ConvertFrom-XmipStatus -Code -21

        $answer | Should -Not -BeOfType ([string])
        $answer.PSObject.Properties.Name | Should -Contain 'Retryable'
    }
}

Describe 'Get-XmipAbi' {
    It 'names the library the way this platform does' {
        # Section 1 of the header. Getting this wrong means the probe looks
        # for a file that is never there, on whichever platform nobody tested.
        [string] $name = (Get-XmipAbi).ExampleLibraryName

        if ($IsWindows) {
            $name | Should -BeLike '*.dll'
        }
        elseif ($IsMacOS) {
            $name | Should -BeLike 'lib*.dylib'
        }
        else {
            $name | Should -BeLike 'lib*.so'
        }
    }
}

Describe 'Get-XmipModuleDescriptor' {
    It 'writes an error rather than throwing when the library is not there' {
        # An operator probing a path that does not exist gets a record they
        # can inspect, and the pipeline survives to probe the next one.
        [string] $missing = Join-Path ([System.IO.Path]::GetTempPath()) 'no-such-xmip-module.dll'

        { Get-XmipModuleDescriptor -Library $missing -ErrorAction SilentlyContinue -ErrorVariable failure } |
            Should -Not -Throw
    }

    It 'reports the path it was asked about' {
        [string] $missing = Join-Path ([System.IO.Path]::GetTempPath()) 'no-such-xmip-module.dll'

        Get-XmipModuleDescriptor -Library $missing -ErrorAction SilentlyContinue -ErrorVariable failure |
            Out-Null

        $failure.Count | Should -BeGreaterThan 0
        $failure[0].TargetObject | Should -BeLike '*no-such-xmip-module.dll'
    }
}
