#requires -PSEdition Core
#requires -Version 7.6.5

<#
    The operator module had no tests, and ARCHITECTURE.md said it did.

    What is checked here is the PowerShell shape and nothing else: the
    cmdlets answer, by name, by manifest agreement, and by the objects they
    emit. Objects rather than text is the whole reason this surface exists
    (ADR-0014), so the tests assert on properties and never on rendering.

    The binding's agreement with the normative header is not tested here.
    It was, until 2026-09-14: this file parsed `include/xmip_module.h` and
    compared it with what the assembly exposed. Since ADR-0014's amendment of
    2026-09-09 the binding is one project in xmip-core-abi, and
    `Xmip.Abi.Tests` (Header.cs, XmipStatusTests.cs) compares that one
    binding with the header. A second copy of the comparison here tested the
    same assembly against the same header, and would have drifted from the
    first — which is the failure a shared binding exists to prevent.

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

BeforeAll {
    [string] $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

    $script:Project = Join-Path $script:Root 'src/Xmip.PowerShell/Xmip.PowerShell.csproj'
    $script:Manifest = Join-Path $script:Root 'src/Xmip.PowerShell/Xmip.PowerShell.psd1'

    [string] $stamp = [System.Guid]::NewGuid().ToString('n').Substring(0, 8)
    $script:Output = Join-Path ([System.IO.Path]::GetTempPath()) "xmip-powershell-$stamp"

    & dotnet build $script:Project --output $script:Output --verbosity quiet --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Building $script:Project failed. Nothing below can mean anything."
    }

    Copy-Item -LiteralPath $script:Manifest -Destination $script:Output -Force

    # A prompt provider that exports its prompt from a module, as posh-git
    # does, in force before the module is imported.
    New-Module -Name FakePromptProvider -ScriptBlock { function prompt { 'provider> ' } } |
        Import-Module -Global

    Import-Module (Join-Path $script:Output 'Xmip.PowerShell.psd1') -Force

    $script:Module = Get-Module -Name Xmip.PowerShell
    $script:Declared = Import-PowerShellDataFile -Path $script:Manifest
}

AfterAll {
    Remove-Module -Name Xmip.PowerShell -Force -ErrorAction SilentlyContinue
    Remove-Module -Name FakePromptProvider -Force -ErrorAction SilentlyContinue
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

Describe 'The prompt reads its surface from the document beside the module' {
    # ADR-0052 clause 3: the surface a host reads is stated in its
    # configuration, never guessed. The module's document ships beside it and
    # is read through the same choice as the GUI hosts and the executable.
    It 'ships xmip.powershell.toml beside the module' {
        [string] $document = Join-Path $script:Output 'xmip.powershell.toml'

        Test-Path -LiteralPath $document | Should -BeTrue
        $document | Should -BeLike "*$([Xmip.PowerShell.PromptMonitor]::ConfigurationFile)"
    }

    It 'chains to the prompt a provider module exported, as posh-git does' {
        # 2026-09-15: the git segment vanished because the module looked only
        # for a global prompt function. What was in force renders after ours.
        [string] $rendered = & (Get-Item Function:\prompt).ScriptBlock 6>$null

        $rendered | Should -Be 'provider> '
        (Get-Item Function:\prompt).Module.Name | Should -Be 'Xmip.PowerShell'
    }

    It 'reads a snapshot in pwsh, so every assembly it needs lies beside it' {
        # 2026-09-15: the build copied only the Xmip assemblies, and the prompt
        # said [Xmip unavailable] for a document it could not read. Reading a
        # published snapshot here loads the TOML reader and the configuration
        # abstractions in the session, which is what a prompt does.
        [string] $fixture = Join-Path $script:Root `
            '../../foundation/abi/dotnet/Xmip.Surface.Test/Fixture/snapshot.toml'
        [string] $document = Join-Path $script:Output 'xmip.powershell.toml'

        [Xmip.Surface.SnapshotOperator]::new($fixture).Health('xmip:///').Count | Should -Be 5
        $shipped = [Xmip.Surface.TomlDocument]::Read($document)
        [Xmip.Surface.SurfaceChoice]::IsChosen($shipped) |
            Should -BeTrue -Because 'the shipped document follows the roll started as C1'
        $shipped['Xmip:Snapshot'] | Should -BeLike '*C1-snapshot.toml'
    }

    It 'says R P S T F with their letters and no word; the color carries the mood' {
        # The owner, 2026-09-15: Receive, Process, Send, reTries and Failures,
        # in red, yellow and green — space on a console line is precious.
        $seen = [DateTimeOffset]::UtcNow
        [Xmip.Abi.Operate.HealthRecord[]] $records = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///R1/receive/orders', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///P1/process/ok', 'Stressed', 55, 'x', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///S1/send/bill', 'Done', 95, 'x', $seen)
        )
        [Xmip.Surface.ScopeIndex+Count[]] $counts = @()
        $index = [Xmip.Surface.ScopeIndex]::Build($records, $counts, 1, 'test')
        $figures = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, 1, 0, $null)
        $segment = [Xmip.PowerShell.PromptMonitor]::Render($index, $figures)

        $segment.Text | Should -Be '[R12 P11 S10 T1 F0]'
        $segment.Parts[1].Color | Should -Be 'Green' -Because 'receive is fine'
        $segment.Parts[2].Color | Should -Be 'Yellow' -Because 'process is stressed'
        $segment.Parts[3].Color | Should -Be 'Red' -Because 'send is done'
        $segment.Parts[4].Color | Should -Be 'Yellow' -Because 'one retrying'
        $segment.Parts[5].Color | Should -Be 'Green' -Because 'nothing failed'
    }

    It 'shows an unpublished figure as absent, never as zero' {
        $none = [Xmip.Surface.Figures]::None('xmip:///')
        $empty = [Xmip.Surface.ScopeIndex]::Empty('test')

        $segment = [Xmip.PowerShell.PromptMonitor]::Render($empty, $none)
        $segment.Text | Should -Be '[R– P– S– T– F–]'
        $segment.Parts[1].Color | Should -Be 'DarkGray'
    }

    It 'paints every mood by the color name the shared English gives it' {
        # One mood-to-color map, in Xmip.Surface; the console picks its
        # nearest color from the name (ADR-0041, ADR-0052 clause 1).
        foreach ($mood in [System.Enum]::GetValues([Xmip.Abi.Operate.HealthState])) {
            [string] $color = [Xmip.Surface.English]::Color($mood)

            [Xmip.PowerShell.PromptMonitor]::Paint($color) |
                Should -BeOfType ([System.ConsoleColor]) -Because "$mood is $color"
        }

        [Xmip.PowerShell.PromptMonitor]::Paint('green') | Should -Be 'Green'
        [Xmip.PowerShell.PromptMonitor]::Paint('red') | Should -Be 'Red'
    }
}

Describe 'The two acts the boundary carries' {
    # ADR-0027 clause 5: pause and resume, and nothing that stops what it
    # watches. A Start-, Stop- or Restart-XmipScope appearing here is a
    # cmdlet the boundary cannot honor.
    It 'exports Suspend-XmipScope and Resume-XmipScope' {
        foreach ($name in @('Suspend-XmipScope', 'Resume-XmipScope')) {
            $script:Module.ExportedCmdlets.Keys | Should -Contain $name
        }
    }

    It 'exports no start, stop or restart' {
        foreach ($name in @('Start-XmipScope', 'Stop-XmipScope', 'Restart-XmipScope')) {
            $script:Module.ExportedCmdlets.Keys | Should -Not -Contain $name
        }
    }

    It 'supports -WhatIf on both, because both change the estate' {
        foreach ($name in @('Suspend-XmipScope', 'Resume-XmipScope')) {
            $script:Module.ExportedCmdlets[$name].Parameters.Keys | Should -Contain 'WhatIf'
        }
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
