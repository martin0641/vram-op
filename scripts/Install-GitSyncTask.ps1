param(
    [string]$RepoPath = "C:\git\vram-op",
    [string]$TaskName = "VRAM Op Git Sync",
    [int]$IntervalMinutes = 5
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $RepoPath "scripts\Run-Sync-VramOp.vbs"
if (-not (Test-Path $scriptPath)) {
    throw "Sync launcher not found at $scriptPath"
}

$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).Date.AddMinutes(10) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$action = New-ScheduledTaskAction `
    -Execute "wscript.exe" `
    -Argument "//B //Nologo `"$scriptPath`" `"$RepoPath`"" `
    -WorkingDirectory $RepoPath
$settings = New-ScheduledTaskSettingsSet `
    -Hidden `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 4) `
    -AllowStartIfOnBatteries `
    -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Silently fast-forwards or pushes the VRAM Vue repo through GitHub." `
    -Force | Out-Null

Write-Host "Installed hidden git sync task '$TaskName' for $RepoPath."
