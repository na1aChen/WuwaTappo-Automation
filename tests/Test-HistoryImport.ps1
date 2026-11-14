[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$importScript = Join-Path $projectRoot "portable\history-import\Import-LegacyHistory.ps1"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "wuwa-history-import-{0}" -f [guid]::NewGuid().ToString("N")
)
$passed = 0
$failed = 0

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Name
    )

    if ($Condition) {
        $script:passed++
        Write-Output "PASS $Name"
    }
    else {
        $script:failed++
        Write-Output "FAIL $Name"
    }
}

function Write-Record {
    param(
        [string]$Path,
        [string]$OperationId,
        [string]$Time,
        [string]$ItemName,
        [switch]$Append
    )

    $record = [ordered]@{
        time = $Time
        operation_id = $OperationId
        side = "single"
        item_name = $ItemName
        item_source = "test"
        asset_id = $null
        popup_label = "重复收集物"
        status = "confirmed"
        result = "opened"
        evidence = "single_button_removed"
    }
    $line = ($record | ConvertTo-Json -Compress -Depth 4) + [Environment]::NewLine
    if ($Append) {
        [System.IO.File]::AppendAllText(
            $Path,
            $line,
            [System.Text.UTF8Encoding]::new($false))
    }
    else {
        [System.IO.File]::WriteAllText(
            $Path,
            $line,
            [System.Text.UTF8Encoding]::new($false))
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $source = Join-Path $tempRoot "old-history.jsonl"
    $target = Join-Path $tempRoot "new-history.jsonl"

    Write-Record `
        -Path $target `
        -OperationId "target-only" `
        -Time "2026-11-01T10:00:00+08:00" `
        -ItemName "新包记录"
    Write-Record `
        -Path $target `
        -OperationId "duplicate" `
        -Time "2026-11-02T10:00:00+08:00" `
        -ItemName "重复记录" `
        -Append

    Write-Record `
        -Path $source `
        -OperationId "duplicate" `
        -Time "2026-11-02T10:00:00+08:00" `
        -ItemName "重复记录"
    Write-Record `
        -Path $source `
        -OperationId "legacy-only" `
        -Time "2026-11-03T10:00:00+08:00" `
        -ItemName "旧版新增" `
        -Append

    & $importScript `
        -SourceHistory $source `
        -TargetHistory $target `
        -NonInteractive `
        -AllowWhileRunning | Out-Null

    $records = @(
        Get-Content -LiteralPath $target -Encoding UTF8 |
            Where-Object { $_ } |
            ForEach-Object { $_ | ConvertFrom-Json }
    )
    $ids = @($records.operation_id | Sort-Object -Unique)
    $backup = @(
        Get-ChildItem -LiteralPath $tempRoot -Filter "history.before-import-*.jsonl" -File
    )

    Assert-True ($records.Count -eq 3) "合并后记录数为 3"
    Assert-True ($ids.Count -eq 3) "按 operation_id 去重"
    Assert-True ($ids.Contains("target-only")) "保留新包独有记录"
    Assert-True ($ids.Contains("legacy-only")) "导入旧版独有记录"
    Assert-True ($backup.Count -eq 1) "导入前自动备份目标历史"
    Assert-True ($records[0].item_name -eq "新包记录") "合并结果按时间排序"
}
finally {
    $fullTempRoot = [System.IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if ($fullTempRoot.StartsWith(
            $systemTemp,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $fullTempRoot)) {
        Remove-Item -LiteralPath $fullTempRoot -Recurse -Force
    }
}

Write-Output ("SUMMARY passed={0} failed={1}" -f $passed, $failed)
if ($failed -ne 0) {
    exit 1
}
