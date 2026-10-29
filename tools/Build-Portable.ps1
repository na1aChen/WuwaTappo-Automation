[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$Force,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "dist\WuwaTappoAutoChest-Portable"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$distRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "dist"))
if (-not $OutputDirectory.StartsWith($distRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "便携输出目录必须位于 $distRoot 下。"
}
if (Test-Path -LiteralPath $OutputDirectory) {
    if (-not $Force) {
        throw "输出目录已存在：$OutputDirectory。使用 -Force 重建。"
    }
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}

$cscCandidates = @(
    (Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:SystemRoot "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($csc)) {
    throw "找不到 .NET Framework C# 编译器 csc.exe。"
}
$frameworkDirectory = Split-Path -Parent $csc

$runtimeDirectory = Join-Path $OutputDirectory "runtime"
$runtimeState = Join-Path $runtimeDirectory "state"
$runtimeLogs = Join-Path $runtimeDirectory "logs"
$runtimeConfig = Join-Path $runtimeDirectory "config"
New-Item -ItemType Directory -Path $runtimeState -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeLogs -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeConfig -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $projectRoot "gallery") -Destination $runtimeDirectory -Recurse
$catalogPath = Join-Path $projectRoot "gallery\catalog.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$imageHashes = [ordered]@{}
foreach ($catalogItem in $catalog.items) {
    if ([string]::IsNullOrWhiteSpace($catalogItem.local_image)) {
        continue
    }
    $imagePath = Join-Path (Join-Path $projectRoot "gallery") $catalogItem.local_image
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
        continue
    }
    $hash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash
    if ($imageHashes.Contains($hash)) {
        $existing = $imageHashes[$hash]
        if ([string]$existing.asset_id -ne [string]$catalogItem.asset_id -or
            [string]$existing.name_zh -ne [string]$catalogItem.name_zh) {
            $existing.ambiguous = $true
        }
    }
    else {
        $imageHashes[$hash] = [ordered]@{
            asset_id = $catalogItem.asset_id
            asset_name = $catalogItem.asset_name
            name_zh = $catalogItem.name_zh
            name_en = $catalogItem.name_en
            ambiguous = $false
        }
    }
}
$imageIndex = [ordered]@{
    schema_version = 1
    algorithm = "SHA-256"
    item_count = $imageHashes.Count
    items = $imageHashes
}
$imageIndex |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $runtimeDirectory "gallery\image-hashes.json") -Encoding UTF8
$runtimeDocs = Join-Path $runtimeDirectory "docs"
New-Item -ItemType Directory -Path $runtimeDocs -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $runtimeDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\atlas-gallery.md") -Destination $runtimeDocs
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\portable.md") -Destination $runtimeDocs
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\investigation.md") -Destination $runtimeDocs
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\design.md") -Destination $runtimeDocs
Copy-Item -LiteralPath (Join-Path $projectRoot "docs\test-results.md") -Destination $runtimeDocs
Copy-Item -LiteralPath (Join-Path $projectRoot ".env.example") -Destination $runtimeDirectory

$portableConfig = Get-Content -LiteralPath (Join-Path $projectRoot "config\autochest.json") -Raw -Encoding UTF8 |
    ConvertFrom-Json
$portableConfig.app.executablePath = ""
$portableConfig.app.launchIfMissing = $false
$portableConfig |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath (Join-Path $runtimeConfig "autochest.json") -Encoding UTF8

$portableReadme = @'
Wuwa Tappo 自动助手 - 便携版
================================

使用前：
1. 安装并启动 Wuwa Tappo。
2. 在 Wuwa Tappo 中完成登录。
3. 双击本目录中的 WuwaTappoAutoChest.exe。
4. 点击“启动后台”。

程序会自动：
- 在字符数低于 1,000 时按需补充；
- 字符数达到门槛后停止输入；
- 持续监测宝箱；
- 宝箱出现后自动开启。

说明：
- 后台监测使用原生 C#，不需要安装 PowerShell 7。
- 关闭控制窗口不会停止后台任务，可使用“停止后台”按钮停止。
- 自动任务依赖 Wuwa Tappo 保持运行和登录状态。
- 不修改 Wuwa Tappo 安装目录。
- 不包含账号、密码、Cookie 或 Token。

快捷命令：
  WuwaTappoAutoChest.exe --start-background
  WuwaTappoAutoChest.exe --stop-background
  WuwaTappoAutoChest.exe --open-gallery

可选配置：
  runtime\config\autochest.json

离线全图鉴：
  runtime\gallery\index.html
  或在控制窗口点击“打开全图鉴”。

完整说明：
  runtime\README.md
  runtime\docs\atlas-gallery.md

资源版权归 Kuro Games / Irox Games 所有。
'@
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDirectory "README.txt"),
    $portableReadme,
    [System.Text.UTF8Encoding]::new($false)
)

$exePath = Join-Path $OutputDirectory "WuwaTappoAutoChest.exe"
$launcherSource = Join-Path $projectRoot "portable\Launcher.cs"
$uiSource = Join-Path $projectRoot "portable\CodexUi.cs"
$workerSource = Join-Path $projectRoot "portable\NativeWorker.cs"
$attributionSource = Join-Path $projectRoot "portable\Attribution.cs"
$manifestPath = Join-Path $projectRoot "portable\app.manifest"
$compilerArguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:x86",
    "/optimize+",
    "/codepage:65001",
    "/out:$exePath",
    "/win32manifest:$manifestPath",
    "/reference:$(Join-Path $frameworkDirectory 'System.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'System.Core.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'System.Drawing.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'System.Management.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'System.Windows.Forms.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'System.Web.Extensions.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationClient.dll')",
    "/reference:$(Join-Path $frameworkDirectory 'WPF\UIAutomationTypes.dll')",
    $launcherSource,
    $uiSource,
    $workerSource,
    $attributionSource
)

& $csc @compilerArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "便携启动器编译失败，csc 退出码：$LASTEXITCODE"
}

$zipPath = "$OutputDirectory.zip"
$checksumPath = "$zipPath.sha256"
if (-not $SkipZip) {
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $OutputDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText(
        $checksumPath,
        "$checksum  $(Split-Path -Leaf $zipPath)`r`n",
        [System.Text.UTF8Encoding]::new($false)
    )
}

[PSCustomObject]@{
    PortableDirectory = $OutputDirectory
    Executable = $exePath
    Zip = if ($SkipZip) { $null } else { $zipPath }
    Checksum = if ($SkipZip) { $null } else { $checksumPath }
    ExecutableBytes = (Get-Item -LiteralPath $exePath).Length
    PackageBytes = if ($SkipZip) { $null } else { (Get-Item -LiteralPath $zipPath).Length }
}
