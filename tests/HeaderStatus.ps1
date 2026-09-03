#requires -PSEdition Core
#requires -Version 7.6.5

<#
.SYNOPSIS
    Reading the normative C header, for the tests that compare against it.

.DESCRIPTION
    A separate file, dot-sourced into both `BeforeDiscovery` and `BeforeAll`,
    and that is not tidiness — it is the only arrangement that works.

    A helper defined at the top level of a Pester 6.1.0 test file and called
    from inside `BeforeAll` aborts the whole container with *"a 'break' or
    'continue' statement with a label that does not match any enclosing loop
    escaped from your code"*, and every test in the file is reported as failed
    with no message. Reduced to eight lines on 2026-09-03: an advanced function
    at file scope, one `BeforeAll` that calls it, one passing test. Discovery
    alone is fine; the run phase is where it goes.

    Defining the helpers inside both blocks works and duplicates them. Putting
    them here and dot-sourcing costs one line per block and keeps one copy.

    Style: docs/governance/powershell-style.md
#>

function Get-XmipHeaderStatus {
    <#
        .SYNOPSIS
            The status codes the normative header defines, name to value.

        .DESCRIPTION
            Returns an empty hashtable when the header cannot be found, which
            is what a standalone clone sees: xmip-core-abi is a sibling in the
            estate and is deliberately not vendored here. The tests that need
            it skip rather than fail, because a missing sibling is not a defect
            in this repository.

        .PARAMETER Path
            The header to read.
    #>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @{ }
    }

    [hashtable] $status = @{ }

    foreach ($line in (Get-Content -LiteralPath $Path)) {
        if ($line -match '^#define\s+XMIP_(OK|E_[A-Z_]+)\s+\(?(-?\d+)\)?') {
            $status[$Matches[1]] = [int] $Matches[2]
        }
    }

    return $status
}

function ConvertTo-XmipMemberName {
    <#
        .SYNOPSIS
            The C# enum member name a header constant corresponds to.

        .DESCRIPTION
            `XMIP_OK` is `Ok`; `XMIP_E_NOT_FOUND` is `NotFound`. The prefix and
            the underscores go and what is left is Pascal case. That is the
            whole naming rule, and stating it as code is what lets the
            comparison be a test rather than a table somebody maintains.

        .PARAMETER Constant
            The header constant, without its `XMIP_` prefix.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Constant
    )

    [string] $bare = $Constant -replace '^E_', ''

    [string[]] $word = @(
        $bare -split '_' | ForEach-Object {
            $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1).ToLowerInvariant()
        }
    )

    return ($word -join '')
}
