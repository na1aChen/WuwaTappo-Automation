[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$SkipImages,
    [switch]$ForceDownload
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "gallery"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$apiBase = "https://api-mc-wuwatappo.kurogames-global.com"
$activityUrl = "$apiBase/api/common/activity-config"
$atlasUrl = "$apiBase/api/common/atlas"
$headers = @{ "User-Agent" = "Mozilla/5.0 WuwaTappoAtlasGallery/1.0" }

function Join-AtlasName {
    param([string[]]$Parts)

    return (@($Parts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "・")
}

function Get-AtlasDictionaryValue {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Dictionary,
        [string]$Key
    )

    if ([string]::IsNullOrWhiteSpace($Key) -or -not $Dictionary.Contains($Key)) {
        return ""
    }
    return [string]$Dictionary[$Key]
}

function Resolve-AtlasDisplayName {
    param(
        [Parameter(Mandatory)]
        [string]$Category,
        [Parameter(Mandatory)]
        [string]$AssetName,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Dictionary
    )

    $fileKey = $AssetName -replace "\.png$", ""
    foreach ($nameKey in @($AssetName, $fileKey)) {
        $directName = Get-AtlasDictionaryValue -Dictionary $Dictionary["$Category"] -Key $nameKey
        if (-not [string]::IsNullOrWhiteSpace($directName)) {
            return $directName
        }
    }

    if ($Category -eq "character") {
        $match = [regex]::Match($fileKey, "^role_(\d+)_(\d+)$")
        if ($match.Success) {
            return Join-AtlasName @(
                (Get-AtlasDictionaryValue -Dictionary $Dictionary["character"] -Key $match.Groups[1].Value),
                (Get-AtlasDictionaryValue -Dictionary $Dictionary["series"] -Key $match.Groups[2].Value)
            )
        }
    }
    elseif ($Category -eq "accessory") {
        $match = [regex]::Match($fileKey, "^accessory_(\d+)$")
        if ($match.Success) {
            return Get-AtlasDictionaryValue -Dictionary $Dictionary["accessory"] -Key $match.Groups[1].Value
        }
    }
    elseif ($Category -eq "emote") {
        $match = [regex]::Match($fileKey, "^emote_(\d+)_([a-z0-9-]+)$")
        if ($match.Success) {
            return Join-AtlasName @(
                (Get-AtlasDictionaryValue -Dictionary $Dictionary["character"] -Key $match.Groups[1].Value),
                (Get-AtlasDictionaryValue -Dictionary $Dictionary["emote"] -Key $match.Groups[2].Value)
            )
        }
    }

    return $fileKey
}

function Get-SafeImageName {
    param([string]$AssetName)

    $name = ($AssetName -replace "\.png$", "") -replace "[^A-Za-z0-9._-]", "_"
    if ([string]::IsNullOrWhiteSpace($name)) {
        return "unknown.png"
    }
    return "$name.png"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$imageRoot = Join-Path $OutputDirectory "images"
New-Item -ItemType Directory -Path $imageRoot -Force | Out-Null

Write-Output "读取活动配置..."
$activity = Invoke-RestMethod -Uri $activityUrl -Headers $headers -TimeoutSec 30
if ($activity.code -ne 0 -or -not $activity.data.assets_host) {
    throw "活动配置中缺少 assets_host。"
}
$assetsHost = ([string]$activity.data.assets_host).TrimEnd("/")

Write-Output "读取资源索引..."
$appearanceHost = Invoke-RestMethod `
    -Uri "$assetsHost/app/json-host/appearance_asset_host.json" `
    -Headers $headers `
    -TimeoutSec 30
$appearance = (
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri ([string]$appearanceHost.url) `
        -Headers $headers `
        -TimeoutSec 30
).Content | ConvertFrom-Json -AsHashtable

Write-Output "读取中英文名称字典..."
$dictionaryHost = Invoke-RestMethod `
    -Uri "$assetsHost/app/json-host/dict_host.json" `
    -Headers $headers `
    -TimeoutSec 30
$dictionaryZh = (
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri ([string]$dictionaryHost.url.zh) `
        -Headers $headers `
        -TimeoutSec 30
).Content | ConvertFrom-Json -AsHashtable
$dictionaryEn = (
    Invoke-WebRequest `
        -UseBasicParsing `
        -Uri ([string]$dictionaryHost.url.en) `
        -Headers $headers `
        -TimeoutSec 30
).Content | ConvertFrom-Json -AsHashtable

Write-Output "读取公开图鉴目录..."
$atlasResponse = Invoke-RestMethod -Uri $atlasUrl -Headers $headers -TimeoutSec 30
if ($atlasResponse.code -ne 0) {
    throw "图鉴接口返回错误：$($atlasResponse.message)"
}

$items = @()
$missingAssetMappings = @()
foreach ($entry in @($atlasResponse.data)) {
    $assetType = [string]$entry.asset_type
    $assetMapKey = switch ($assetType) {
        "character_skin_atlas" { "character_skin_atlas" }
        "accessory_atlas" { "accessory_atlas" }
        "emote_atlas" { "emote_atlas" }
        default { $null }
    }
    if ([string]::IsNullOrWhiteSpace($assetMapKey)) {
        continue
    }

    $category = switch ($assetMapKey) {
        "character_skin_atlas" { "character" }
        "accessory_atlas" { "accessory" }
        "emote_atlas" { "emote" }
    }
    $assetId = [string]$entry.asset_id
    $assetEntry = $null
    if ($appearance[$assetMapKey].Contains($assetId)) {
        $assetEntry = $appearance[$assetMapKey][$assetId]
    }
    else {
        $missingAssetMappings += "$assetMapKey/$assetId"
    }

    $assetName = [string]$entry.asset_name
    $remoteImage = ""
    if ($null -ne $assetEntry -and $assetEntry.Contains("url")) {
        $remoteImage = "$assetsHost/$(([string]$assetEntry.url).TrimStart('/'))"
    }

    $displayZh = Resolve-AtlasDisplayName `
        -Category $category `
        -AssetName $assetName `
        -Dictionary $dictionaryZh
    $displayEn = Resolve-AtlasDisplayName `
        -Category $category `
        -AssetName $assetName `
        -Dictionary $dictionaryEn
    if ([string]::IsNullOrWhiteSpace($displayZh)) {
        $displayZh = $assetName -replace "\.png$", ""
    }
    if ([string]::IsNullOrWhiteSpace($displayEn)) {
        $displayEn = $displayZh
    }

    $safeImageName = Get-SafeImageName -AssetName $assetName
    $relativeImage = "images/$category/$safeImageName"
    $items += [ordered]@{
        category = $category
        category_label = switch ($category) {
            "character" { "角色" }
            "accessory" { "配件" }
            "emote" { "表情" }
        }
        asset_id = [long]$entry.asset_id
        asset_code = [string]$entry.asset_code
        asset_name = $assetName
        asset_type = $assetType
        rarity = ([string]$entry.rarity).ToLowerInvariant()
        name_zh = $displayZh
        name_en = $displayEn
        local_image = $relativeImage
        remote_image = $remoteImage
    }
}

$downloadFailures = @()
if (-not $SkipImages) {
    foreach ($category in @("character", "accessory", "emote")) {
        New-Item -ItemType Directory -Path (Join-Path $imageRoot $category) -Force | Out-Null
    }

    $downloadIndex = 0
    foreach ($item in $items) {
        $downloadIndex++
        if ([string]::IsNullOrWhiteSpace([string]$item.remote_image)) {
            continue
        }
        $localPath = Join-Path $OutputDirectory ([string]$item.local_image)
        if (-not $ForceDownload -and
            (Test-Path -LiteralPath $localPath -PathType Leaf) -and
            (Get-Item -LiteralPath $localPath).Length -gt 0) {
            continue
        }

        $downloaded = $false
        for ($attempt = 1; $attempt -le 2 -and -not $downloaded; $attempt++) {
            try {
                Invoke-WebRequest `
                    -UseBasicParsing `
                    -Uri ([string]$item.remote_image) `
                    -Headers $headers `
                    -OutFile $localPath `
                    -TimeoutSec 30
                $downloaded = (Test-Path -LiteralPath $localPath -PathType Leaf) -and
                    (Get-Item -LiteralPath $localPath).Length -gt 0
            }
            catch {
                if ($attempt -lt 2) {
                    Start-Sleep -Milliseconds 400
                }
            }
        }
        if (-not $downloaded) {
            $downloadFailures += [ordered]@{
                asset_id = $item.asset_id
                asset_name = $item.asset_name
                url = $item.remote_image
            }
        }
        if ($downloadIndex % 25 -eq 0 -or $downloadIndex -eq $items.Count) {
            Write-Output "图片进度：$downloadIndex/$($items.Count)，失败 $($downloadFailures.Count)"
        }
    }
}

$counts = [ordered]@{
    character = @($items | Where-Object category -eq "character").Count
    accessory = @($items | Where-Object category -eq "accessory").Count
    emote = @($items | Where-Object category -eq "emote").Count
}
$generatedAt = (Get-Date).ToString("o")
$catalog = [ordered]@{
    schema_version = 1
    generated_at = $generatedAt
    source = $atlasUrl
    assets_host = $assetsHost
    counts = $counts
    items = $items
}
$catalogJson = $catalog | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDirectory "catalog.json"),
    $catalogJson,
    [System.Text.UTF8Encoding]::new($false)
)

$manifest = [ordered]@{
    generated_at = $generatedAt
    source = $atlasUrl
    assets_host = $assetsHost
    counts = $counts
    image_download_enabled = (-not $SkipImages)
    download_failures = $downloadFailures
    missing_asset_mappings = $missingAssetMappings
}
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDirectory "manifest.json"),
    ($manifest | ConvertTo-Json -Depth 10),
    [System.Text.UTF8Encoding]::new($false)
)

$catalogInlineJson = $catalog | ConvertTo-Json -Depth 10 -Compress
$html = @'
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Wuwa Tappo 全图鉴</title>
  <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Ccircle cx='32' cy='32' r='28' fill='%2370d6c5'/%3E%3C/svg%3E">
  <style>
    :root {
      color-scheme: dark;
      --bg: #11151b;
      --panel: #1a2029;
      --panel2: #222a35;
      --text: #edf3f8;
      --muted: #9aa8b8;
      --line: #34404e;
      --accent: #70d6c5;
      --common: #8fa0b3;
      --uncommon: #51c878;
      --rare: #4ea4ff;
      --epic: #c884ff;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      color: var(--text);
      background:
        radial-gradient(circle at 15% 0%, #203446 0, transparent 34rem),
        radial-gradient(circle at 90% 10%, #2b273c 0, transparent 30rem),
        var(--bg);
      font-family: "Microsoft YaHei UI", "Segoe UI", sans-serif;
    }
    header {
      position: sticky;
      top: 0;
      z-index: 20;
      padding: 20px clamp(16px, 4vw, 54px) 14px;
      background: color-mix(in srgb, var(--bg) 88%, transparent);
      backdrop-filter: blur(14px);
      border-bottom: 1px solid var(--line);
    }
    .title-row { display: flex; align-items: end; justify-content: space-between; gap: 18px; }
    h1 { margin: 0; font-size: clamp(22px, 3vw, 36px); letter-spacing: .02em; }
    .sub { color: var(--muted); margin-top: 6px; font-size: 13px; }
    .controls {
      display: grid;
      grid-template-columns: minmax(220px, 1fr) auto auto;
      gap: 10px;
      margin-top: 16px;
    }
    input, select, button {
      min-height: 40px;
      border: 1px solid var(--line);
      border-radius: 10px;
      color: var(--text);
      background: var(--panel);
      padding: 8px 12px;
      font: inherit;
    }
    input:focus, select:focus, button:focus { outline: 2px solid var(--accent); outline-offset: 1px; }
    .tabs { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
    .tab { cursor: pointer; }
    .tab.active { border-color: var(--accent); background: #1c3b3a; color: var(--accent); }
    main { padding: 22px clamp(16px, 4vw, 54px) 60px; }
    .meta { color: var(--muted); margin: 0 0 16px; }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
      gap: 14px;
    }
    .card {
      overflow: hidden;
      border: 1px solid var(--line);
      border-radius: 16px;
      background: linear-gradient(150deg, var(--panel2), var(--panel));
      break-inside: avoid;
      content-visibility: auto;
      contain-intrinsic-size: 250px;
    }
    .image-box {
      display: grid;
      place-items: center;
      height: 180px;
      padding: 12px;
      background:
        linear-gradient(45deg, #161c24 25%, transparent 25%),
        linear-gradient(-45deg, #161c24 25%, transparent 25%),
        linear-gradient(45deg, transparent 75%, #161c24 75%),
        linear-gradient(-45deg, transparent 75%, #161c24 75%),
        #131920;
      background-size: 20px 20px;
      background-position: 0 0, 0 10px, 10px -10px, -10px 0;
    }
    .image-box img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      image-rendering: auto;
    }
    .card-body { padding: 11px 12px 13px; }
    .name { font-weight: 700; line-height: 1.35; min-height: 38px; }
    .name-en { color: var(--muted); font-size: 12px; margin-top: 3px; min-height: 17px; }
    .tags { display: flex; justify-content: space-between; gap: 8px; margin-top: 9px; color: var(--muted); font-size: 12px; }
    .rarity { font-weight: 700; text-transform: uppercase; }
    .rarity.common { color: var(--common); }
    .rarity.uncommon { color: var(--uncommon); }
    .rarity.rare { color: var(--rare); }
    .rarity.epic { color: var(--epic); }
    .empty { color: var(--muted); text-align: center; padding: 80px 20px; }
    footer { color: var(--muted); font-size: 12px; margin-top: 26px; line-height: 1.7; }
    a { color: var(--accent); }
    @media (max-width: 720px) {
      .controls { grid-template-columns: 1fr; }
      .title-row { align-items: start; flex-direction: column; }
      .grid { grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); }
      .image-box { height: 150px; }
    }
  </style>
</head>
<body>
  <header>
    <div class="title-row">
      <div>
        <h1>Wuwa Tappo 全图鉴</h1>
        <div class="sub">基于官方公开资源清单 · 本地只读浏览 · 不修改账号</div>
      </div>
      <div class="sub" id="total"></div>
    </div>
    <div class="controls">
      <input id="search" type="search" placeholder="搜索名称、资源名或编号…" autocomplete="off">
      <select id="rarity">
        <option value="">全部稀有度</option>
        <option value="epic">Epic</option>
        <option value="rare">Rare</option>
        <option value="uncommon">Uncommon</option>
        <option value="common">Common</option>
      </select>
      <select id="sort">
        <option value="category">按类别</option>
        <option value="rarity">按稀有度</option>
        <option value="name">按名称</option>
        <option value="id">按编号</option>
      </select>
    </div>
    <nav class="tabs" id="tabs">
      <button class="tab active" data-category="">全部</button>
      <button class="tab" data-category="character">角色</button>
      <button class="tab" data-category="accessory">配件</button>
      <button class="tab" data-category="emote">表情</button>
    </nav>
  </header>
  <main>
    <p class="meta" id="meta"></p>
    <section class="grid" id="grid"></section>
    <div class="empty" id="empty" hidden>没有符合筛选条件的条目</div>
    <footer>
      图片来源：库洛官方 Wuwa Tappo CDN。资源版权归 Kuro Games / Irox Games 所有。<br>
      本页面仅用于个人离线浏览，不代表账号已拥有对应收集物。
    </footer>
  </main>
  <script type="application/json" id="catalog-data">__CATALOG_JSON__</script>
  <script>
    const catalog = JSON.parse(document.getElementById("catalog-data").textContent);
    const state = { category: "", rarity: "", query: "", sort: "category" };
    const rarityOrder = { epic: 0, rare: 1, uncommon: 2, common: 3 };
    const categoryOrder = { character: 0, accessory: 1, emote: 2 };
    const grid = document.getElementById("grid");
    const empty = document.getElementById("empty");
    const meta = document.getElementById("meta");
    const total = document.getElementById("total");

    total.textContent = `共 ${catalog.items.length.toLocaleString()} 项`;
    document.getElementById("search").addEventListener("input", event => {
      state.query = event.target.value.trim().toLowerCase();
      render();
    });
    document.getElementById("rarity").addEventListener("change", event => {
      state.rarity = event.target.value;
      render();
    });
    document.getElementById("sort").addEventListener("change", event => {
      state.sort = event.target.value;
      render();
    });
    document.getElementById("tabs").addEventListener("click", event => {
      const button = event.target.closest("[data-category]");
      if (!button) return;
      state.category = button.dataset.category;
      document.querySelectorAll(".tab").forEach(tab => tab.classList.toggle("active", tab === button));
      render();
    });

    function compare(a, b) {
      if (state.sort === "id") return b.asset_id - a.asset_id;
      if (state.sort === "name") return a.name_zh.localeCompare(b.name_zh, "zh-CN");
      if (state.sort === "rarity") {
        return (rarityOrder[a.rarity] ?? 9) - (rarityOrder[b.rarity] ?? 9) || b.asset_id - a.asset_id;
      }
      return (categoryOrder[a.category] ?? 9) - (categoryOrder[b.category] ?? 9) ||
        a.name_zh.localeCompare(b.name_zh, "zh-CN") || b.asset_id - a.asset_id;
    }

    function createCard(item) {
      const card = document.createElement("article");
      card.className = "card";

      const imageBox = document.createElement("div");
      imageBox.className = "image-box";
      const image = document.createElement("img");
      image.loading = "lazy";
      image.decoding = "async";
      image.alt = item.name_zh;
      image.src = item.local_image || item.remote_image;
      image.addEventListener("error", () => {
        if (item.remote_image && image.src !== item.remote_image) image.src = item.remote_image;
      }, { once: true });
      imageBox.appendChild(image);

      const body = document.createElement("div");
      body.className = "card-body";
      const name = document.createElement("div");
      name.className = "name";
      name.textContent = item.name_zh;
      const nameEn = document.createElement("div");
      nameEn.className = "name-en";
      nameEn.textContent = item.name_en !== item.name_zh ? item.name_en : "";
      const tags = document.createElement("div");
      tags.className = "tags";
      const category = document.createElement("span");
      category.textContent = `${item.category_label} · #${item.asset_id}`;
      const rarity = document.createElement("span");
      rarity.className = `rarity ${item.rarity}`;
      rarity.textContent = item.rarity;
      tags.append(category, rarity);
      body.append(name, nameEn, tags);
      card.append(imageBox, body);
      return card;
    }

    function render() {
      const filtered = catalog.items.filter(item => {
        if (state.category && item.category !== state.category) return false;
        if (state.rarity && item.rarity !== state.rarity) return false;
        if (!state.query) return true;
        return [
          item.name_zh,
          item.name_en,
          item.asset_name,
          String(item.asset_id),
          item.asset_code
        ].some(value => String(value).toLowerCase().includes(state.query));
      }).sort(compare);

      const fragment = document.createDocumentFragment();
      for (const item of filtered) fragment.appendChild(createCard(item));
      grid.replaceChildren(fragment);
      empty.hidden = filtered.length > 0;
      meta.textContent = `显示 ${filtered.length.toLocaleString()} / ${catalog.items.length.toLocaleString()} 项 · 数据生成于 ${new Date(catalog.generated_at).toLocaleString("zh-CN")}`;
    }
    render();
  </script>
</body>
</html>
'@
$html = $html.Replace("__CATALOG_JSON__", $catalogInlineJson)
$htmlPath = Join-Path $OutputDirectory "index.html"
[System.IO.File]::WriteAllText(
    $htmlPath,
    $html,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Output "图鉴生成完成：$htmlPath"
Write-Output "角色：$($counts.character)，配件：$($counts.accessory)，表情：$($counts.emote)，总计：$($items.Count)"
if ($missingAssetMappings.Count -gt 0) {
    Write-Warning "有 $($missingAssetMappings.Count) 个图鉴条目缺少图片元数据。"
}
if ($downloadFailures.Count -gt 0) {
    Write-Warning "有 $($downloadFailures.Count) 张图片下载失败，页面会尝试使用远端地址。"
}

[PSCustomObject]@{
    OutputDirectory = $OutputDirectory
    Html = $htmlPath
    Catalog = Join-Path $OutputDirectory "catalog.json"
    Manifest = Join-Path $OutputDirectory "manifest.json"
    CharacterCount = $counts.character
    AccessoryCount = $counts.accessory
    EmoteCount = $counts.emote
    TotalCount = $items.Count
    DownloadFailures = $downloadFailures.Count
    MissingAssetMappings = $missingAssetMappings.Count
}
