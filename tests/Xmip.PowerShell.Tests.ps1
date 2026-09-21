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
        # for a global prompt function. 2026-09-18: ours sits where posh-git
        # puts a repository's state, after what was in force and before its
        # closing ">", not in front of the whole prompt.
        # The same day: nothing to say is nothing on the line, as posh-git
        # outside a repository, so the segment shows once a snapshot is read.
        [string] $fixture = Join-Path $script:Root `
            '../../foundation/abi/dotnet/Xmip.Surface.Test/Fixture/snapshot.toml'
        [Xmip.PowerShell.PromptMonitor]::Follow(
            (Join-Path ([System.IO.Path]::GetTempPath()) 'xmip-no-such-snapshot.toml'))
        Start-Sleep -Milliseconds 500
        [string] $silent = -join @(& (Get-Item Function:\prompt).ScriptBlock)
        $silent | Should -Be 'provider> '

        [Xmip.PowerShell.PromptMonitor]::Follow($fixture)
        [string] $plain = ''

        foreach ($attempt in 1..40) {
            [string] $rendered = -join @(& (Get-Item Function:\prompt).ScriptBlock)
            $plain = $rendered -replace '\e\[[0-9;]*m', ''
            if ($plain -like '*`[R*') { break }
            Start-Sleep -Milliseconds 100
        }

        $plain | Should -Match '^provider \[R.+\]> $'
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

    It 'follows the snapshot the session names, over the one the document ships' {
        # 2026-09-18: the owner rolled cluster CC1 and the prompt sat on C1,
        # the file the shipped document names. Start-XmipTest now says which
        # file its roll publishes, through this.
        [string] $fixture = Join-Path $script:Root `
            '../../foundation/abi/dotnet/Xmip.Surface.Test/Fixture/snapshot.toml'

        [Xmip.PowerShell.PromptMonitor]::Follow($fixture)

        [string] $said = ''
        foreach ($attempt in 1..40) {
            $said = [Xmip.PowerShell.PromptMonitor]::Current.Text
            if ($said -like '`[R*') { break }
            Start-Sleep -Milliseconds 100
        }

        $said | Should -BeLike '`[R* P* S* T* F*]'
    }

    It 'says a rate once a publisher has published twice, and a dash before that' {
        # The owner, 2026-09-20: the number has to mean something over time.
        # The interval is the reader's own, between the two publications it
        # saw, because a published snapshot carries no clock for its counts.
        [string] $stamp = [System.Guid]::NewGuid().ToString('n').Substring(0, 8)
        [string] $moving = Join-Path ([System.IO.Path]::GetTempPath()) "xmip-rate-$stamp.toml"

        function Write-Publication {
            param([int] $Streams, [int] $Journeys)

            @"
source = "rate test"
node = "xmip:///W9"

[[records]]
scope = "xmip:///W9/receive/a"
state = "fine"
severity = 0
evidence = ""
observed_unix_nanos = 1789111688000000000

[[counts]]
counted = "streams"
value = $Streams

[[counts]]
counted = "journeys"
value = $Journeys
"@ | Set-Content -LiteralPath $moving -Encoding utf8
        }

        try {
            Write-Publication -Streams 1000 -Journeys 200
            [Xmip.PowerShell.PromptMonitor]::Follow($moving)

            [string] $first = ''
            foreach ($attempt in 1..40) {
                $first = [Xmip.PowerShell.PromptMonitor]::Current.Text
                if ($first -like '`[W9*') { break }
                Start-Sleep -Milliseconds 100
            }

            $first | Should -Be '[W9 ≡ R– P– S–]' -Because 'one publication is no interval'

            Start-Sleep -Seconds 1
            Write-Publication -Streams 3000 -Journeys 400

            [string] $second = ''
            foreach ($attempt in 1..40) {
                $second = [Xmip.PowerShell.PromptMonitor]::Current.Text
                if ($second -like '*R:*') { break }
                Start-Sleep -Milliseconds 100
            }

            $second | Should -BeLike '`[W9 ≡ R:* P:* S–]'
        }
        finally {
            [Xmip.PowerShell.PromptMonitor]::Stop()
            Remove-Item -LiteralPath $moving -Force -ErrorAction SilentlyContinue
        }
    }

    It 'says every figure as a rate, never spells the unit, and the color carries the mood' {
        # The owner, 2026-09-15: Receive, Process, Send, reTries and Failures,
        # in red, yellow and green — space on a console line is precious. And
        # 2026-09-20: every letter is what is moving now, per second, and the
        # line does not spell /s out five times. A total is bounded by uptime
        # and means nothing over time; a rate is bounded by throughput and can
        # say stalled.
        $seen = [DateTimeOffset]::UtcNow
        [Xmip.Abi.Operate.HealthRecord[]] $records = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///R1/receive/orders', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///P1/process/ok', 'Stressed', 55, 'x', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///S1/send/bill', 'Done', 95, 'x', $seen)
        )
        [Xmip.Surface.ScopeIndex+Count[]] $counts = @()
        $index = [Xmip.Surface.ScopeIndex]::Build($records, $counts, 1, 'test')
        $figures = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, 1, 0, $null)
        # FigureFlow is Streams, Journeys, Messages, then Retrying and Failed:
        # what R, P, S, T and F are each moving per second.
        $flow = [Xmip.Surface.FigureFlow]::new(
            [double] 12, [double] 11, [double] 10, [double] 1, [double] 2)
        $segment = [Xmip.PowerShell.SegmentRender]::Render($index, $figures, $flow)

        # 2026-09-18: T and F are there only when there are any; F0 is not said.
        $segment.Text | Should -Be '[R:12 P:11 S:10 T:1]'
        $segment.Parts[0].Color | Should -Be 'Yellow' -Because 'the brackets are posh-git yellow'
        $segment.Parts[1].Color | Should -Be 'Cyan' -Because 'receive is fine, in posh-git cyan'
        $segment.Parts[2].Color | Should -Be 'Yellow' -Because 'process is stressed'
        $segment.Parts[3].Color | Should -Be 'Red' -Because 'send is done'
        $segment.Parts[4].Color | Should -Be 'Yellow' -Because 'one retrying'

        $failing = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, 0, 2, $null)
        $failed = [Xmip.PowerShell.SegmentRender]::Render($index, $failing, $flow)
        $failed.Text | Should -Be '[R:12 P:11 S:10 F:2]'
        $failed.Parts[4].Color | Should -Be 'Red' -Because 'two failed'

        # The owner, 2026-09-18: posh-git's look. Every stage fine and nothing
        # retrying or failed is square, and says so with posh-git's own sign.
        [Xmip.Abi.Operate.HealthRecord[]] $fine = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///R1/receive/orders', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///P1/process/ok', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///S1/send/bill', 'Fine', 0, '', $seen)
        )
        $allFine = [Xmip.Surface.ScopeIndex]::Build($fine, $counts, 2, 'test')
        $calm = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, 0, 0, $null)
        $square = [Xmip.PowerShell.SegmentRender]::Render($allFine, $calm, $flow)
        $square.Text | Should -Be '[≡ R:12 P:11 S:10]' -Because 'three nodes share no name'
        $square.Parts[1].Color | Should -Be 'Cyan' -Because 'the sign is posh-git cyan'

        # posh-git's order, [main ≡ +0 ~1 -0]: what the prompt is at, its sign
        # when square, then the counts. At one node it is the node's name; at
        # a cluster, the cluster's; the Playground's `node` segment names nothing.
        [Xmip.Abi.Operate.HealthRecord[]] $oneNode = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/node/R1/receive/a', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/node/R1/send/b', 'Fine', 0, '', $seen)
        )
        $atNode = [Xmip.Surface.ScopeIndex]::Build($oneNode, $counts, 3, 'test')
        [Xmip.PowerShell.SegmentRender]::At($atNode) | Should -Be 'R1'
        [Xmip.PowerShell.SegmentRender]::Render($atNode, $calm, $flow).Text |
            Should -Be '[R1 ≡ R:12 P:11 S:10]'

        # A roll of one test shares its scenario too; the prompt is at the
        # cluster still, never at round-trip (the owner's RoundTrip, 2026-09-19).
        [Xmip.Abi.Operate.HealthRecord[]] $oneTest = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/round-trip/process/tcp/json', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/round-trip/process/udp/xml', 'Fine', 0, '', $seen)
        )
        $atOneTest = [Xmip.Surface.ScopeIndex]::Build($oneTest, $counts, 5, 'test')
        [Xmip.PowerShell.SegmentRender]::At($atOneTest) | Should -Be 'C1'

        [Xmip.Abi.Operate.HealthRecord[]] $twoNodes = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/node/R1/receive/a', 'Fine', 0, '', $seen)
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///C1/node/S1/send/b', 'Done', 95, 'x', $seen)
        )
        $atCluster = [Xmip.Surface.ScopeIndex]::Build($twoNodes, $counts, 4, 'test')
        $troubled = [Xmip.PowerShell.SegmentRender]::Render($atCluster, $calm, $flow)
        $troubled.Text | Should -Be '[C1 R:12 P:11 S:10]' -Because 'not square, no sign'
        $troubled.Parts[1].Color | Should -Be 'Red' -Because 'the name wears the worst stage'
        [Xmip.PowerShell.SegmentRender]::Render($allFine, $failing, $flow).Text |
            Should -Be '[R:12 P:11 S:10 F:2]' -Because 'a failure is not square'

        $quiet = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, $null, $null, $null)
        [Xmip.PowerShell.SegmentRender]::Render($index, $quiet, $flow).Text |
            Should -Be '[R:12 P:11 S:10]' -Because 'no T or F published is no T or F'
    }

    It 'shows a rate it cannot compute as a dash, and a stalled one as zero' {
        # The owner, 2026-09-20: 0 means stalled, so "not known yet" must not
        # be written 0 — it is the dash an unpublished figure already gets.
        $empty = [Xmip.Surface.ScopeIndex]::Empty('test')
        $none = [Xmip.Surface.Figures]::None('xmip:///')

        $nothing = [Xmip.PowerShell.SegmentRender]::Render(
            $empty, $none, [Xmip.Surface.FigureFlow]::Unknown)
        $nothing.Text | Should -Be '[R– P– S–]'
        $nothing.Parts[1].Color | Should -Be 'DarkGray'

        # One publication is no interval: the figures are there and the rate
        # is not.
        $first = [Xmip.Surface.Figures]::new('xmip:///', 5000, 640, 660, 0, $null, $null, $null)
        $opening = [Xmip.Surface.FigureFlow]::Between($first, $null, [TimeSpan]::Zero)
        [Xmip.PowerShell.SegmentRender]::Render($empty, $first, $opening).Text |
            Should -Be '[R– P– S–]' -Because 'not known yet is not stalled'

        # Two publications and nothing moved: stalled, and it says so.
        $stalled = [Xmip.Surface.FigureFlow]::Between(
            $first, $first, [TimeSpan]::FromSeconds(2))
        [Xmip.PowerShell.SegmentRender]::Render($empty, $first, $stalled).Text |
            Should -Be '[R:0 P:0 S:0]'

        # And two that moved: the rate is per second over the interval, and
        # not one letter on the line spells the unit (the owner, 2026-09-20:
        # *the Xmip Prompt does not need /s spelled out*).
        $quiet = [Xmip.Surface.Figures]::new('xmip:///', 5000, 640, 660, 0, 0, 0, $null)
        $later = [Xmip.Surface.Figures]::new('xmip:///', 5600, 700, 720, 0, 3, 1, $null)
        $moving = [Xmip.Surface.FigureFlow]::Between(
            $later, $quiet, [TimeSpan]::FromSeconds(5))
        [Xmip.PowerShell.SegmentRender]::Render($empty, $later, $moving).Text |
            Should -Be '[R:120 P:12 S:12 T:0.6 F:0.2]' -Because 'T and F are rates too'

        # T and F are gated on the total and drawn as the rate, so a run that
        # retried once carries T for as long as it rolls even when nothing has
        # retried since. The owner, 2026-09-20, had never seen either letter in
        # a test that had both, and a rate gate is why: it would show for one
        # prompt and go.
        $settled = [Xmip.Surface.FigureFlow]::Between(
            $later, $later, [TimeSpan]::FromSeconds(5))
        [Xmip.PowerShell.SegmentRender]::Render($empty, $later, $settled).Text |
            Should -Be '[R:0 P:0 S:0 T:0 F:0]' -Because 'the trouble is there and has stopped arriving'

        # One publication is no interval for T and F either: the totals put the
        # letters on the line and the rate is the dash every figure gets.
        $opened = [Xmip.Surface.FigureFlow]::Between($later, $null, [TimeSpan]::Zero)
        [Xmip.PowerShell.SegmentRender]::Render($empty, $later, $opened).Text |
            Should -Be '[R– P– S– T– F–]' -Because 'not known yet is not none'
    }

    It 'keeps a rate on the same ladder as a count and never writes a trickle as zero' {
        [Xmip.PowerShell.SegmentRender]::Rate(0) | Should -Be '0'
        [Xmip.PowerShell.SegmentRender]::Rate(0.3) | Should -Be '0.3'
        [Xmip.PowerShell.SegmentRender]::Rate(9.94) | Should -Be '9.9'
        [Xmip.PowerShell.SegmentRender]::Rate(240) | Should -Be '240'
        [Xmip.PowerShell.SegmentRender]::Rate(1234) | Should -Be '1.2K'
        [Xmip.PowerShell.SegmentRender]::Rate(1234567) | Should -Be '1.2M'
    }

    # `Short` was the count formatter and went with the last count on the line
    # (2026-09-20). `Rate` carries the same K, M and G ladder and is tested
    # above; a second formatter with no caller is a second answer waiting to
    # disagree with the first.

    It 'paints a short number hotter where it rises and icier where it falls' {
        # The owner, 2026-09-18: 5.3K is 5.3K for a long while, so the color
        # of the number says which way it is going; the letter keeps the mood.
        # Since 2026-09-20 the number is a rate, so the color says whether the
        # rate is climbing — acceleration, which no digit on the line shows.
        [Xmip.PowerShell.SegmentRender]::Trend(5400, 5300) | Should -Be 'DarkYellow'
        [Xmip.PowerShell.SegmentRender]::Trend(6000, 5000) | Should -Be 'Magenta'
        [Xmip.PowerShell.SegmentRender]::Trend(5300, 5400) | Should -Be 'DarkCyan'
        [Xmip.PowerShell.SegmentRender]::Trend(4000, 5000) | Should -Be 'Blue'
        [Xmip.PowerShell.SegmentRender]::Trend(5300, 5300) | Should -BeNullOrEmpty
        [Xmip.PowerShell.SegmentRender]::Trend(5300, $null) | Should -BeNullOrEmpty
        [Xmip.PowerShell.SegmentRender]::Trend(900, 100) |
            Should -BeNullOrEmpty -Because 'a number written in full shows its own movement'

        $seen = [DateTimeOffset]::UtcNow
        [Xmip.Abi.Operate.HealthRecord[]] $records = @(
            [Xmip.Abi.Operate.HealthRecord]::new('xmip:///R1/receive/a', 'Fine', 0, '', $seen)
        )
        [Xmip.Surface.ScopeIndex+Count[]] $counts = @()
        $index = [Xmip.Surface.ScopeIndex]::Build($records, $counts, 1, 'test')
        $figures = [Xmip.Surface.Figures]::None('xmip:///')
        $then = [Xmip.Surface.FigureFlow]::new([double] 5300, [double] 60, [double] 60)
        $now = [Xmip.Surface.FigureFlow]::new([double] 5400, [double] 60, [double] 60)
        $segment = [Xmip.PowerShell.SegmentRender]::Render($index, $figures, $now, $then)

        $segment.Text | Should -BeLike '*R:5.4K P:60 S:60*'
        ($segment.Parts | Where-Object Text -EQ 'R:').Color | Should -Be 'Cyan'
        ($segment.Parts | Where-Object Text -EQ '5.4K').Color | Should -Be 'DarkYellow'
    }

    It 'says there is another cluster when the session named more than one' {
        # ADR-0052, amendment 2026-09-20: Start-XmipTest followed whichever
        # roll started last, and with two rolling the segment read as the whole
        # estate. The prompt still reads one publication — a mood over two
        # clusters is at a scope in neither tree — and now says how many it is
        # not showing. Nothing beside is nothing on the line.
        $seen = [DateTimeOffset]::UtcNow
        $leaf = [Xmip.Abi.Operate.HealthRecord]
        [Xmip.Abi.Operate.HealthRecord[]] $records = @(
            $leaf::new('xmip:///C1/node/R1/receive/a', 'Fine', 0, '', $seen)
            $leaf::new('xmip:///C1/node/S1/send/b', 'Done', 95, 'x', $seen)
        )
        [Xmip.Surface.ScopeIndex+Count[]] $counts = @()
        $index = [Xmip.Surface.ScopeIndex]::Build($records, $counts, 1, 'test')
        $figures = [Xmip.Surface.Figures]::new('xmip:///', 12, 10, 11, 4096, 0, 0, $null)
        $flow = [Xmip.Surface.FigureFlow]::new([double] 12, [double] 11, [double] 10)

        [Xmip.PowerShell.SegmentRender]::Render($index, $figures, $flow, $null, 0).Text |
            Should -Be '[C1 R:12 P:11 S:10]' -Because 'one cluster says nothing of others'

        $ofTwo = [Xmip.PowerShell.SegmentRender]::Render($index, $figures, $flow, $null, 1)
        $ofTwo.Text | Should -Be '[C1+1 R:12 P:11 S:10]'
        ($ofTwo.Parts | Where-Object Text -EQ 'C1').Color |
            Should -Be 'Red' -Because 'the name still wears the worst stage'
        ($ofTwo.Parts | Where-Object Text -EQ '+1 ').Color |
            Should -Be 'DarkGray' -Because 'what is not shown is gray, as an unpublished figure is'
    }

    It 'follows one roll and counts the others the session named beside it' {
        [string] $fixture = Join-Path $script:Root `
            '../../foundation/abi/dotnet/Xmip.Surface.Test/Fixture/cluster.toml'
        [string] $beside = Join-Path $script:Root `
            '../../foundation/abi/dotnet/Xmip.Surface.Test/Fixture/cluster-c2.toml'

        # Named by the session, never counted from files on disk: a surface is
        # stated (ADR-0052 clause 3). The followed one among them is not an
        # "other", however the caller lists them.
        [Xmip.PowerShell.PromptMonitor]::Follow($fixture, @($fixture, $beside))

        [string] $said = ''
        foreach ($attempt in 1..40) {
            $said = [Xmip.PowerShell.PromptMonitor]::Current.Text
            if ($said -like '`[C1+1*') { break }
            Start-Sleep -Milliseconds 100
        }

        $said | Should -BeLike '`[C1+1 *]'

        [Xmip.PowerShell.PromptMonitor]::Follow($fixture)

        foreach ($attempt in 1..40) {
            $said = [Xmip.PowerShell.PromptMonitor]::Current.Text
            if ($said -like '`[C1 *') { break }
            Start-Sleep -Milliseconds 100
        }

        $said | Should -BeLike '`[C1 *]' -Because 'one roll alone says nothing of others'
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
