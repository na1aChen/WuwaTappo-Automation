[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$Foreground,
    [switch]$Once,
    [switch]$DryRun,
    [switch]$DisableActivity,
    [ValidateRange(5, 120)]
    [int]$StartupTimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$modulePath = Join-Path $projectRoot "src\WuwaTappoAutoChest.psm1"
Import-Module $modulePath -Force

$config = Import-AutoChestConfig -ConfigPath $ConfigPath
Initialize-AutoChestStorage -Config $config

if ($Foreground -or $Once) {
    Invoke-AutoChestWorker `
        -Config $config `
        -Once:$Once `
        -DryRun:$DryRun `
        -DisableActivity:$DisableActivity
    return
}

$existingWorker = Get-AutoChestWorkerProcess -Config $config
if ($null -ne $existingWorker) {
    $status = Read-AutoChestStatus -Config $config
    return [PSCustomObject]@{
        started = $false
        reason = "already_running"
        worker_pid = [int]$existingWorker.Id
        status = $status
    }
}

if (Test-Path -LiteralPath $config.Paths.StopFile) {
    Remove-Item -LiteralPath $config.Paths.StopFile -Force
}

$appProcess = @(Get-Process -Name $config.App.ProcessName -ErrorAction SilentlyContinue)
if ($appProcess.Count -eq 0 -and $config.App.LaunchIfMissing) {
    if ([string]::IsNullOrWhiteSpace($config.App.ExecutablePath) -or
        -not (Test-Path -LiteralPath $config.App.ExecutablePath -PathType Leaf)) {
        throw "配置启用了 launchIfMissing，但 app.executablePath 无效。"
    }
    Start-Process -FilePath $config.App.ExecutablePath -WindowStyle Hidden | Out-Null
}

$pwshPath = (Get-Process -Id $PID).Path
if ([string]::IsNullOrWhiteSpace($pwshPath)) {
    $pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
}

$argumentList = @(
    "-NoLogo",
    "-NoProfile",
    "-NonInteractive",
    "-File",
    "`"$($config.Paths.WatchScript)`"",
    "-ConfigPath",
    "`"$($config.ConfigPath)`""
)
if ($DryRun) {
    $argumentList += "-DryRun"
}
if ($DisableActivity) {
    $argumentList += "-DisableActivity"
}

$worker = Start-Process `
    -FilePath $pwshPath `
    -ArgumentList $argumentList `
    -WindowStyle Hidden `
    -PassThru

$deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    if ($worker.HasExited) {
        break
    }

    $status = Read-AutoChestStatus -Config $config
    if ($null -ne $status -and
        $status.worker_pid -eq $worker.Id) {
        $ready = $true
        break
    }

    Start-Sleep -Milliseconds 250
}

if (-not $ready) {
    $status = Read-AutoChestStatus -Config $config
    $errorMessage = if ($worker.HasExited) {
        "后台工作进程启动后立即退出。请检查状态和日志。"
    }
    else {
        "后台工作进程在 $StartupTimeoutSeconds 秒内没有写出就绪状态。"
    }
    throw [System.InvalidOperationException]::new(
        $errorMessage,
        [System.Exception]::new(($status | ConvertTo-Json -Compress -Depth 10))
    )
}

return [PSCustomObject]@{
    started = $true
    worker_pid = [int]$worker.Id
    status = Read-AutoChestStatus -Config $config
}
