# Xmip's prompt integration. The binary nested module owns cmdlets and the
# background observer; this script only composes its cached segment with the
# prompt that was already installed.
$script:PreviousPrompt = (Get-Item Function:\global:prompt -ErrorAction SilentlyContinue).ScriptBlock

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

    $current = (Get-Item Function:\global:prompt -ErrorAction SilentlyContinue).ScriptBlock

    if ([object]::ReferenceEquals($current, $script:XmipPrompt)) {
        Set-Item Function:\global:prompt -Value $script:PreviousPrompt
    }
}
