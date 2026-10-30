[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,
    [switch]$ExpectEmptyState
)

$ErrorActionPreference = "Stop"
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

function Read-PeInfo {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 256 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "不是有效的 MZ/PE 文件：$Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 96 -ge $bytes.Length) {
        throw "PE 头偏移无效：$Path"
    }
    if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) {
        throw "PE 签名无效：$Path"
    }

    [pscustomobject]@{
        Machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
        OptionalMagic = [BitConverter]::ToUInt16($bytes, $peOffset + 24)
        Subsystem = [BitConverter]::ToUInt16($bytes, $peOffset + 92)
        Bytes = $bytes
    }
}

$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$mainExe = Join-Path $PackageDirectory "WuwaTappoAutoChest.exe"
$cdpExe = Join-Path $PackageDirectory "WuwaTappoAutoChest-CdpHost.exe"
$readme = Join-Path $PackageDirectory "README.txt"
$runtimeConfig = Join-Path $PackageDirectory "runtime\config\autochest.json"
$runtimeDocs = Join-Path $PackageDirectory "runtime\docs\portable.md"

Assert-True (Test-Path -LiteralPath $mainExe -PathType Leaf) "主程序存在"
Assert-True (Test-Path -LiteralPath $cdpExe -PathType Leaf) "CDP Host 存在"
Assert-True (Test-Path -LiteralPath $readme -PathType Leaf) "根目录使用说明存在"
Assert-True (Test-Path -LiteralPath $runtimeConfig -PathType Leaf) "便携配置存在"
Assert-True (Test-Path -LiteralPath $runtimeDocs -PathType Leaf) "便携文档存在"

if (Test-Path -LiteralPath $cdpExe -PathType Leaf) {
    $pe = Read-PeInfo -Path $cdpExe
    Assert-True ($pe.Machine -eq 0x014C) "CDP Host 为 x86 PE"
    Assert-True ($pe.OptionalMagic -eq 0x010B) "CDP Host 为 PE32"
    Assert-True ($pe.Subsystem -eq 2) "CDP Host 为 Windows GUI 子系统"

    $manifestText = [System.Text.Encoding]::UTF8.GetString($pe.Bytes)
    Assert-True (
        $manifestText.Contains('level="requireAdministrator"')
    ) "CDP Host 嵌入管理员权限清单"
    Assert-True (
        -not $manifestText.Contains('level="asInvoker"')
    ) "CDP Host 未嵌入普通权限清单"
    Assert-True (
        $manifestText.Contains('PerMonitorV2')
    ) "CDP Host 包含 PerMonitorV2 DPI 感知"
}

if (Test-Path -LiteralPath $readme -PathType Leaf) {
    $readmeText = Get-Content -LiteralPath $readme -Raw -Encoding UTF8
    Assert-True (
        $readmeText.Contains("WuwaTappoAutoChest-CdpHost.exe")
    ) "README.txt 说明 CDP Host"
    Assert-True (
        $readmeText.Contains("UAC")
    ) "README.txt 说明 UAC"
}

if (Test-Path -LiteralPath $runtimeConfig -PathType Leaf) {
    $config = Get-Content -LiteralPath $runtimeConfig -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-True (
        [string]::IsNullOrWhiteSpace([string]$config.app.executablePath)
    ) "便携配置未写入本机应用路径"
    Assert-True (
        [string]$config.app.processName -eq "WuwaTappo"
    ) "便携配置目标进程正确"
}

if ($ExpectEmptyState) {
    $stateDirectory = Join-Path $PackageDirectory "runtime\state"
    $stateFiles = @(
        Get-ChildItem -LiteralPath $stateDirectory -File -Force -ErrorAction SilentlyContinue
    )
    $logDirectory = Join-Path $PackageDirectory "runtime\logs"
    $logFiles = @(
        Get-ChildItem -LiteralPath $logDirectory -File -Force -ErrorAction SilentlyContinue
    )
    Assert-True ($stateFiles.Count -eq 0) "分享包状态目录为空"
    Assert-True ($logFiles.Count -eq 0) "分享包日志目录为空"
}

Write-Output ("SUMMARY passed={0} failed={1}" -f $passed, $failed)
if ($failed -ne 0) {
    exit 1
}
