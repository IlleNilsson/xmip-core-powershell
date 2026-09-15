# Xmip's prompt integration. The binary nested module owns cmdlets and the
# background observer; this script only composes its cached segment with the
# prompt that was already installed.
# Whatever prompt is in force, wherever it lives: posh-git exports its prompt
# from its module rather than defining a global one, so a lookup of
# Function:\global:prompt found nothing and this module fell back to a bare
# "PS >", losing the git segment (the owner, 2026-09-15). A script block keeps
# the session state it was written in, so posh-git's still renders as its own.
$script:PreviousPrompt = (Get-Item Function:\prompt -ErrorAction SilentlyContinue).ScriptBlock

if ($null -eq $script:PreviousPrompt) {
    $script:PreviousPrompt = { "PS $($executionContext.SessionState.Path.CurrentLocation)> " }
}

[Xmip.PowerShell.PromptMonitor]::Start()

$script:XmipPrompt = {
    $segment = [Xmip.PowerShell.PromptMonitor]::Current

    foreach ($part in $segment.Parts) {
        Write-Host -NoNewline $part.Text -ForegroundColor $part.Color
    }

    Write-Host -NoNewline ' '
    & $script:PreviousPrompt
}

Set-Item Function:\global:prompt -Value $script:XmipPrompt

$ExecutionContext.SessionState.Module.OnRemove = {
    [Xmip.PowerShell.PromptMonitor]::Stop()

    $current = Get-Item Function:\global:prompt -ErrorAction SilentlyContinue

    # A function set from a script block keeps that block's module. Ours was
    # this module's, and the shell has already dropped it by the time this
    # runs, so nothing is in force; the previous one is its own provider's
    # again — posh-git's, say — and outlives this removal. Another provider
    # that took the prompt over in the meantime keeps it.
    if ($null -eq $current -or $current.Module.Name -eq 'Xmip.PowerShell') {
        Set-Item Function:\global:prompt -Value $script:PreviousPrompt
    }
}
