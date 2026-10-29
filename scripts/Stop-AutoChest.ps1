[CmdletBinding()]
param(
    [string]$ConfigPath,
    [ValidateRange(2, 120)]
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"
$modulePath = Join-Path (Split-Path $PSScriptRoot -Parent) "src\WuwaTappoAutoChest.psm1"
Import-Module $modulePath -Force

$config = Import-AutoChestConfig -ConfigPath $ConfigPath
Initialize-AutoChestStorage -Config $config

$worker = Get-AutoChestWorkerProcess -Config $config
if ($null -eq $worker) {
    if (Test-Path -LiteralPath $config.Paths.PidFile) {
        Remove-Item -LiteralPath $config.Paths.PidFile -Force
    }
    if (Test-Path -LiteralPath $config.Paths.StopFile) {
        Remove-Item -LiteralPath $config.Paths.StopFile -Force
    }
    return [PSCustomObject]@{
        stopped = $true
        graceful = $true
        reason = "not_running"
        worker_pid = $null
    }
}

Write-AutoChestJsonAtomic -Path $config.Paths.StopFile -Data @{
    requested_at = (Get-Date).ToString("o")
    requester_pid = $PID
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ($worker.HasExited) {
        break
    }
    Start-Sleep -Milliseconds 250
    $worker.Refresh()
}

$graceful = $worker.HasExited
if (-not $graceful) {
    $verifiedWorker = Get-AutoChestWorkerProcess -Config $config
    if ($null -ne $verifiedWorker) {
        Stop-Process -Id $verifiedWorker.Id -Force
        $verifiedWorker.WaitForExit(5000)
    }
}

if (Test-Path -LiteralPath $config.Paths.PidFile) {
    Remove-Item -LiteralPath $config.Paths.PidFile -Force
}
if (Test-Path -LiteralPath $config.Paths.StopFile) {
    Remove-Item -LiteralPath $config.Paths.StopFile -Force
}

return [PSCustomObject]@{
    stopped = $true
    graceful = $graceful
    worker_pid = [int]$worker.Id
}
