[CmdletBinding()]
param(
    [string]$SourceHistory,
    [string]$TargetHistory,
    [switch]$NonInteractive,
    [switch]$AllowWhileRunning
)

$ErrorActionPreference = "Stop"
$packageRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($TargetHistory)) {
    $TargetHistory = Join-Path $packageRoot "runtime\state\history.jsonl"
}

function Select-HistoryFile {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "选择旧版 Wuwa Tappo 自动助手的 history.jsonl"
    $dialog.Filter = "history.jsonl|history.jsonl|JSON Lines (*.jsonl)|*.jsonl|所有文件 (*.*)|*.*"
    $dialog.CheckFileExists = $true
    $dialog.Multiselect = $false
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return ""
    }
    return $dialog.FileName
}

function Read-HistoryFile {
    param(
        [string]$Path,
        [System.Collections.IDictionary]$Records,
        [ref]$InvalidCount
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        try {
            $record = $line | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            $InvalidCount.Value++
            continue
        }

        $operationId = [string]$record.operation_id
        if ([string]::IsNullOrWhiteSpace($operationId)) {
            $operationId = "legacy:" +
                [string]$record.time + "|" +
                [string]$record.side + "|" +
                [string]$record.item_name + "|" +
                [string]$record.status
        }

        if (-not $Records.Contains($operationId)) {
            $parsedTime = [DateTimeOffset]::MinValue
            [void][DateTimeOffset]::TryParse(
                [string]$record.time,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None,
                [ref]$parsedTime)
            $Records[$operationId] = [pscustomobject]@{
                Id = $operationId
                Time = $parsedTime
                Line = $line
            }
        }
    }
}

if (-not $AllowWhileRunning) {
    $running = @(
        Get-Process -Name "WuwaTappoAutoChest" -ErrorAction SilentlyContinue
    )
    if ($running.Count -gt 0) {
        throw "请先停止自动助手后台并关闭控制面板，再导入历史。"
    }
}

if ([string]::IsNullOrWhiteSpace($SourceHistory)) {
    $SourceHistory = Select-HistoryFile
}
if ([string]::IsNullOrWhiteSpace($SourceHistory)) {
    Write-Output "已取消，没有修改历史文件。"
    exit 0
}

$SourceHistory = [System.IO.Path]::GetFullPath($SourceHistory)
$TargetHistory = [System.IO.Path]::GetFullPath($TargetHistory)
if (-not (Test-Path -LiteralPath $SourceHistory -PathType Leaf)) {
    throw "找不到旧版历史文件：$SourceHistory"
}
if ($SourceHistory.Equals(
        $TargetHistory,
        [StringComparison]::OrdinalIgnoreCase)) {
    Write-Output "选择的已经是新包历史文件，无需导入。"
    exit 0
}

$targetDirectory = Split-Path $TargetHistory -Parent
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
$backupPath = ""
if (Test-Path -LiteralPath $TargetHistory -PathType Leaf) {
    $backupPath = Join-Path $targetDirectory (
        "history.before-import-{0}.jsonl" -f (Get-Date -Format "yyyyMMdd-HHmmss")
    )
    Copy-Item -LiteralPath $TargetHistory -Destination $backupPath -Force
}

$records = [ordered]@{}
$invalid = 0
Read-HistoryFile `
    -Path $TargetHistory `
    -Records $records `
    -InvalidCount ([ref]$invalid)
$targetCount = $records.Count
Read-HistoryFile `
    -Path $SourceHistory `
    -Records $records `
    -InvalidCount ([ref]$invalid)

$ordered = @($records.Values | Sort-Object Time, Id)
$newLine = [Environment]::NewLine
$content = (($ordered | ForEach-Object { $_.Line }) -join $newLine) + $newLine
$tempPath = "$TargetHistory.import.tmp"
[System.IO.File]::WriteAllText(
    $tempPath,
    $content,
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::Copy($tempPath, $TargetHistory, $true)
[System.IO.File]::Delete($tempPath)

$added = $ordered.Count - $targetCount
Write-Output "导入完成。"
Write-Output "合并前新包记录：$targetCount"
Write-Output "合并后唯一记录：$($ordered.Count)"
Write-Output "新增记录：$added"
Write-Output "无效 JSON 行：$invalid"
if (-not [string]::IsNullOrWhiteSpace($backupPath)) {
    Write-Output "原历史备份：$backupPath"
}
Write-Output "目标历史：$TargetHistory"

