[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$Once,
    [switch]$DryRun,
    [switch]$DisableActivity
)

$ErrorActionPreference = "Stop"
$modulePath = Join-Path (Split-Path $PSScriptRoot -Parent) "src\WuwaTappoAutoChest.psm1"
Import-Module $modulePath -Force

$config = Import-AutoChestConfig -ConfigPath $ConfigPath
Invoke-AutoChestWorker `
    -Config $config `
    -Once:$Once `
    -DryRun:$DryRun `
    -DisableActivity:$DisableActivity
