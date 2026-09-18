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

# Where posh-git puts a repository's state: after the path and before the
# closing ">", "D:\Repos\Xmip [main] [R:12 P:11 S:10]> ". The owner asked
# for the same kind of output as posh-git and the segment stood in front of
# the whole prompt until 2026-09-18. The prompt in force is rendered first;
# ours goes in before its trailing ">" and whatever escape codes close it. A
# prompt that ends some other way keeps ours in front, as before. Colored
# with escape codes, because a returned string is what a prompt composes.
$script:XmipPrompt = {
    [string] $text = ''
    $parts = [Xmip.PowerShell.PromptMonitor]::Current.Parts

    # Nothing to say is nothing on the line, as posh-git outside a repository.
    if ($parts.Count -eq 0) {
        return & $script:PreviousPrompt
    }

    foreach ($part in $parts) {
        $text += $PSStyle.Foreground.FromConsoleColor($part.Color) + $part.Text
    }

    $text += $PSStyle.Reset
    [string] $previous = -join @(& $script:PreviousPrompt)

    if ($previous -match '(?s)^(?<before>.*?)(?<close>\s*>+\s*(?:\e\[[0-9;]*m)*)$') {
        return $Matches['before'] + ' ' + $text + $Matches['close']
    }

    $text + ' ' + $previous
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
