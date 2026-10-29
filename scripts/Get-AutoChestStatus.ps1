[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$Json
)

$ErrorActionPreference = "Stop"
$modulePath = Join-Path (Split-Path $PSScriptRoot -Parent) "src\WuwaTappoAutoChest.psm1"
Import-Module $modulePath -Force

$config = Import-AutoChestConfig -ConfigPath $ConfigPath
$worker = Get-AutoChestWorkerProcess -Config $config
$status = Read-AutoChestStatus -Config $config

$result = [ordered]@{
    running = ($null -ne $worker)
    worker_pid = if ($null -ne $worker) { [int]$worker.Id } else { $null }
    config_path = $config.ConfigPath
    status_file = $config.Paths.StatusFile
    log_file = $config.Paths.LogFile
    status = $status
}

if ($Json) {
    $result | ConvertTo-Json -Depth 20
}
else {
    [PSCustomObject]$result
}
