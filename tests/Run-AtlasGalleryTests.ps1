[CmdletBinding()]
param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$galleryRoot = Join-Path $projectRoot "gallery"
$catalogPath = Join-Path $galleryRoot "catalog.json"
$htmlPath = Join-Path $galleryRoot "index.html"

if ($Rebuild) {
    & (Join-Path $projectRoot "tools\Build-AtlasGallery.ps1") `
        -OutputDirectory $galleryRoot | Out-Null
}

$passed = 0
$failed = 0

function Assert-GalleryTrue {
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

if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    throw "找不到图鉴目录：$catalogPath。请先运行 tools/Build-AtlasGallery.ps1。"
}
if (-not (Test-Path -LiteralPath $htmlPath -PathType Leaf)) {
    throw "找不到图鉴页面：$htmlPath。请先运行 tools/Build-AtlasGallery.ps1。"
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$items = @($catalog.items)
Assert-GalleryTrue -Condition ($items.Count -eq 899) -Name "图鉴总数为 899"
Assert-GalleryTrue -Condition (@($items | Where-Object category -eq "character").Count -eq 260) -Name "角色图鉴为 260"
Assert-GalleryTrue -Condition (@($items | Where-Object category -eq "accessory").Count -eq 135) -Name "配件图鉴为 135"
Assert-GalleryTrue -Condition (@($items | Where-Object category -eq "emote").Count -eq 504) -Name "表情图鉴为 504"
Assert-GalleryTrue -Condition (@($items.asset_id | Sort-Object -Unique).Count -eq $items.Count) -Name "图鉴编号无重复"
Assert-GalleryTrue -Condition (@($items | Where-Object { [string]::IsNullOrWhiteSpace($_.name_zh) }).Count -eq 0) -Name "中文名称完整"

$missingImages = @()
foreach ($item in $items) {
    $imagePath = Join-Path $galleryRoot ([string]$item.local_image)
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
        $missingImages += $imagePath
    }
}
Assert-GalleryTrue -Condition ($missingImages.Count -eq 0) -Name "本地图片文件完整"

Add-Type -AssemblyName System.Drawing
$invalidImages = 0
$checkedImages = 0
foreach ($item in $items) {
    $imagePath = Join-Path $galleryRoot ([string]$item.local_image)
    try {
        $image = [System.Drawing.Image]::FromFile($imagePath)
        if ($image.Width -ne 560 -or $image.Height -ne 560) {
            $invalidImages++
        }
        $image.Dispose()
    }
    catch {
        $invalidImages++
    }
    $checkedImages++
}
Assert-GalleryTrue -Condition ($checkedImages -eq 899) -Name "检查 899 张本地图片"
Assert-GalleryTrue -Condition ($invalidImages -eq 0) -Name "全部图片可解码且为 560x560"

$html = Get-Content -LiteralPath $htmlPath -Raw -Encoding UTF8
Assert-GalleryTrue -Condition ($html -match 'id="catalog-data"') -Name "页面包含内嵌图鉴数据"
Assert-GalleryTrue -Condition ($html -match 'id="search"') -Name "页面包含搜索控件"
Assert-GalleryTrue -Condition ($html -match 'id="rarity"') -Name "页面包含稀有度筛选"
Assert-GalleryTrue -Condition ($html -match 'data-category="character"') -Name "页面包含角色筛选"
Assert-GalleryTrue -Condition ($html -match 'data-category="accessory"') -Name "页面包含配件筛选"
Assert-GalleryTrue -Condition ($html -match 'data-category="emote"') -Name "页面包含表情筛选"

Write-Output "SUMMARY passed=$passed failed=$failed"
if ($failed -gt 0) {
    throw "$failed 个图鉴测试失败。"
}
