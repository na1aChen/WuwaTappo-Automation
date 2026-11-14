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

function Read-Text {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ""
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8
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
$hostExe = Join-Path $PackageDirectory "WuwaTappoAutoChest-CdpHost.exe"
$readme = Join-Path $PackageDirectory "README.txt"
$historyImportCmd = Join-Path $PackageDirectory "Import-LegacyHistory.cmd"
$historyImportScript = Join-Path $PackageDirectory "Import-LegacyHistory.ps1"
$runtimeDirectory = Join-Path $PackageDirectory "runtime"
$runtimeConfig = Join-Path $runtimeDirectory "config\autochest.json"
$nativeDirectory = Join-Path $runtimeDirectory "native"
$discoverScript = Join-Path $nativeDirectory "discover.js"
$stateScript = Join-Path $nativeDirectory "state.js"
$boostScript = Join-Path $nativeDirectory "boost.js"
$nativeDesign = Join-Path $runtimeDirectory "docs\native-count-refill-design.md"
$retryDesign = Join-Path $runtimeDirectory "docs\retry-recovery-design.md"
$thirdPartyNotice = Join-Path $runtimeDirectory "docs\third-party-notices.md"

Assert-True (Test-Path -LiteralPath $mainExe -PathType Leaf) "主程序存在"
Assert-True (Test-Path -LiteralPath $hostExe -PathType Leaf) "原生计数 Host 存在"
Assert-True (Test-Path -LiteralPath $readme -PathType Leaf) "根目录使用说明存在"
Assert-True (Test-Path -LiteralPath $historyImportCmd -PathType Leaf) "旧历史导入入口存在"
Assert-True (Test-Path -LiteralPath $historyImportScript -PathType Leaf) "旧历史导入脚本存在"
Assert-True (Test-Path -LiteralPath $runtimeConfig -PathType Leaf) "便携配置存在"
Assert-True (Test-Path -LiteralPath $discoverScript -PathType Leaf) "页面能力发现脚本存在"
Assert-True (Test-Path -LiteralPath $stateScript -PathType Leaf) "页面只读快照脚本存在"
Assert-True (Test-Path -LiteralPath $boostScript -PathType Leaf) "页面原生补数脚本存在"
Assert-True (Test-Path -LiteralPath $nativeDesign -PathType Leaf) "原生补数设计文档存在"
Assert-True (Test-Path -LiteralPath $retryDesign -PathType Leaf) "恢复设计文档存在"
Assert-True (Test-Path -LiteralPath $thirdPartyNotice -PathType Leaf) "第三方许可说明存在"

if (Test-Path -LiteralPath $mainExe -PathType Leaf) {
    $mainBytes = [System.IO.File]::ReadAllBytes($mainExe)
    $mainText = [System.Text.Encoding]::Unicode.GetString($mainBytes)
    Assert-True (
        $mainText.Contains("by Phrolova2405")
    ) "主程序包含作者水印"
}

if (Test-Path -LiteralPath $hostExe -PathType Leaf) {
    $pe = Read-PeInfo -Path $hostExe
    $hostText = [System.Text.Encoding]::Unicode.GetString($pe.Bytes)
    Assert-True ($pe.Machine -eq 0x014C) "Host 为 x86 PE"
    Assert-True ($pe.OptionalMagic -eq 0x010B) "Host 为 PE32"
    Assert-True ($pe.Subsystem -eq 2) "Host 为 Windows GUI 子系统"

    $manifestText = [System.Text.Encoding]::UTF8.GetString($pe.Bytes)
    Assert-True (
        $manifestText.Contains('level="requireAdministrator"')
    ) "Host 嵌入管理员权限清单"
    Assert-True (
        -not $manifestText.Contains('level="asInvoker"')
    ) "Host 未嵌入普通权限清单"
    Assert-True (
        $manifestText.Contains('PerMonitorV2')
    ) "Host 包含 PerMonitorV2 DPI 感知"
    Assert-True (
        $hostText.Contains("by Phrolova2405")
    ) "Host 包含作者水印"
}

$discoverText = Read-Text $discoverScript
Assert-True (
    $discoverText.Contains("location.hostname !== 'tauri.localhost'")
) "能力发现仅用于 Tauri 主页面"
Assert-True (
    $discoverText.Contains("await import(commonPath)") -and
    $discoverText.Contains("await import(clickPath)")
) "能力发现通过页面模块验证实际导出"
Assert-True (
    -not $discoverText.Contains("document.cookie") -and
    -not $discoverText.Contains("Authorization")
) "能力发现不读取 Cookie 或授权头"

$stateText = Read-Text $stateScript
Assert-True (
    $stateText.Contains("crypto.subtle.digest('SHA-256'") -and
    $stateText.Contains("accountKey")
) "页面快照只返回账号不可逆摘要"
Assert-True (
    -not $stateText.Contains("document.cookie") -and
    -not $stateText.Contains("Authorization") -and
    -not $stateText.Contains("fetch(")
) "页面快照不读取凭据或发送 HTTP 请求"

$boostText = Read-Text $boostScript
Assert-True (
    $boostText.Contains("store.incrementLocalClickCount()")
) "补数调用页面现有增量函数"
Assert-True (
    $boostText.Contains("saveClicks(undefined, {force:true})")
) "补数调用页面现有强制保存函数"
Assert-True (
    $boostText.Contains("delta > 2000")
) "单批大小有硬上限"
Assert-True (
    $boostText.Contains("__COUNT_MODE__") -and
    $boostText.Contains("event-count-mode-not-supported")
) "补数拒绝未知计数模式"
Assert-True (
    $boostText.Contains("post-save-state-not-confirmed")
) "保存后必须重新验证页面状态"
Assert-True (
    $boostText.Contains("increment-not-confirmed") -and
    $boostText.Contains("partialIsSafe")
) "增量中断返回可安全重基准的现场"
Assert-True (
    -not $boostText.Contains("before.local !==") -and
    -not $boostText.Contains("before.consumed !==")
) "预检不把页面本地归并字段当作硬冲突"
Assert-True (
    -not $boostText.Contains("document.cookie") -and
    -not $boostText.Contains("Authorization") -and
    -not $boostText.Contains("fetch(")
) "补数脚本不读取凭据或发送 HTTP 请求"

$readmeText = Read-Text $readme
Assert-True (
    $readmeText.Contains("预检冲突") -and
    $readmeText.Contains("只读复核")
) "README.txt 说明安全恢复边界"
Assert-True (
    $readmeText.Contains("WebSocket") -and
    $readmeText.Contains("3 次会话")
) "README.txt 说明会话级恢复上限"
Assert-True (
    $readmeText.Contains("Import-LegacyHistory.cmd") -and
    $readmeText.Contains("operation_id")
) "README.txt 说明旧历史迁移"

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

$readmeText = Read-Text $readme
Assert-True (
    $readmeText.Contains("WuwaTappoAutoChest-CdpHost.exe")
) "README.txt 说明原生计数 Host"
Assert-True (
    $readmeText.Contains("--native-refill")
) "README.txt 提供命令行补数入口"
Assert-True (
    $readmeText.Contains("UAC")
) "README.txt 说明 UAC 边界"

if ($ExpectEmptyState) {
    $stateDirectory = Join-Path $runtimeDirectory "state"
    $stateFiles = @(
        Get-ChildItem -LiteralPath $stateDirectory -File -Force -ErrorAction SilentlyContinue
    )
    $logDirectory = Join-Path $runtimeDirectory "logs"
    $logFiles = @(
        Get-ChildItem -LiteralPath $logDirectory -File -Force -ErrorAction SilentlyContinue
    )
    Assert-True ($stateFiles.Count -eq 0) "分享包状态目录为空"
    Assert-True ($logFiles.Count -eq 0) "分享包日志目录为空"
}

$textFiles = @(
    Get-ChildItem -LiteralPath $PackageDirectory -File -Recurse -Force |
        Where-Object {
            $_.FullName -notmatch '\\gallery\\images\\' -and
            $_.Length -le 8MB -and
            $_.Extension -in @(".txt", ".md", ".json", ".js", ".ps1", ".env")
        }
)
$localUserHits = @()
$secretHits = @()
foreach ($file in $textFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($text -match 'C:\\Users\\') {
        $localUserHits += $file.FullName
    }
    if ($text -match 'Bearer\s+[A-Za-z0-9._~-]{20,}' -or
        $text -match 'eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}') {
        $secretHits += $file.FullName
    }
}
Assert-True ($localUserHits.Count -eq 0) "分享包文本不含本机用户目录"
Assert-True ($secretHits.Count -eq 0) "分享包文本不含长 Bearer 或 JWT"

Write-Output ("SUMMARY passed={0} failed={1}" -f $passed, $failed)
if ($failed -ne 0) {
    exit 1
}
