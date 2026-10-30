[CmdletBinding()]
param(
    [string]$ExecutablePath,
    [string]$ImageIndexPath,
    [string]$LevelDbLogPath
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot "dist\WuwaTappoAutoChest-UiTest\WuwaTappoAutoChest.exe"
}
if ([string]::IsNullOrWhiteSpace($LevelDbLogPath)) {
    $LevelDbLogPath = Join-Path $env:LOCALAPPDATA (
        "com.kuro.wuwatappo.cn\EBWebView\Default\Local Storage\leveldb\000003.log"
    )
}
if ([string]::IsNullOrWhiteSpace($ImageIndexPath)) {
    $ImageIndexPath = Join-Path $projectRoot (
        "dist\WuwaTappoAutoChest-UiTest\runtime\gallery\image-hashes.json"
    )
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$executableBytes = [IO.File]::ReadAllBytes($resolvedExecutable)
if ($executableBytes.Length -ge 0x40 -and
    [BitConverter]::ToUInt16($executableBytes, 0x3c) -lt $executableBytes.Length - 4) {
    $peOffset = [int][BitConverter]::ToUInt32($executableBytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($executableBytes, $peOffset + 4)
    if ($machine -eq 0x014c -and [Environment]::Is64BitProcess) {
        $powerShell32 = Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
        & $powerShell32 -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File $PSCommandPath `
            -ExecutablePath $ExecutablePath `
            -ImageIndexPath $ImageIndexPath `
            -LevelDbLogPath $LevelDbLogPath
        exit $LASTEXITCODE
    }
}

$assembly = [Reflection.Assembly]::LoadFile($resolvedExecutable)
$workerType = $assembly.GetType("WuwaTappoPortable.NativeWorker", $true)
$flags = [Reflection.BindingFlags]"Static,NonPublic"

$parseMethod = $workerType.GetMethod("TryParseAssetIdArray", $flags)
if ($null -eq $parseMethod) {
    throw "TryParseAssetIdArray was not found."
}

$syntheticPayload = "bag_new_asset_ids" + [char]7 + [char]1 + "[2928,1459]"
$syntheticBytes = [System.Text.Encoding]::UTF8.GetBytes($syntheticPayload)
$syntheticArguments = @($syntheticBytes, 17, $null)
$syntheticOk = $parseMethod.Invoke($null, $syntheticArguments)
$syntheticIds = @($syntheticArguments[2] | ForEach-Object { [int]$_ })
if (-not $syntheticOk -or ($syntheticIds -join ",") -ne "2928,1459") {
    throw "Synthetic asset ID parsing failed."
}
Write-Output "PASS synthetic_asset_id_parse=$($syntheticIds -join ',')"

$readMethod = $workerType.GetMethod("TryReadLatestNewAssetIds", $flags)
if ($null -eq $readMethod) {
    throw "TryReadLatestNewAssetIds was not found."
}
if (-not (Test-Path -LiteralPath $LevelDbLogPath -PathType Leaf)) {
    Write-Output "SKIP online_leveldb_not_found=$LevelDbLogPath"
    exit 0
}

$levelDbArguments = @($LevelDbLogPath, $null)
$levelDbOk = $readMethod.Invoke($null, $levelDbArguments)
$levelDbIds = @($levelDbArguments[1] | ForEach-Object { [int]$_ })
if (-not $levelDbOk) {
    throw "The online bag_new_asset_ids value could not be read."
}
Write-Output "PASS online_asset_ids=$($levelDbIds -join ',')"

if (-not (Test-Path -LiteralPath $ImageIndexPath -PathType Leaf)) {
    Write-Output "SKIP image_hash_index_not_found=$ImageIndexPath"
    exit 0
}

$imageIndex = Get-Content -LiteralPath $ImageIndexPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$hashToName = New-Object 'System.Collections.Generic.Dictionary[string,string]'
foreach ($property in $imageIndex.items.PSObject.Properties) {
    $hashToName[$property.Name] = [string]$property.Value.name_zh
}
Write-Output "PASS image_hash_index_count=$($hashToName.Count)"

$cacheDirectory = Join-Path $env:LOCALAPPDATA (
    "com.kuro.wuwatappo.cn\EBWebView\Default\Cache\Cache_Data"
)
if (-not (Test-Path -LiteralPath $cacheDirectory -PathType Container)) {
    Write-Output "SKIP online_cache_not_found=$cacheDirectory"
    exit 0
}

$matchedCacheName = ""
$matchedCacheFile = ""
foreach ($cacheFile in Get-ChildItem -LiteralPath $cacheDirectory -File |
    Where-Object { $_.Name -like "f_*" } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 300) {
    try {
        $cacheHash = (Get-FileHash -LiteralPath $cacheFile.FullName -Algorithm SHA256).Hash
        if ($hashToName.ContainsKey($cacheHash)) {
            $matchedCacheName = $hashToName[$cacheHash]
            $matchedCacheFile = $cacheFile.Name
            break
        }
    }
    catch {
    }
}
if ([string]::IsNullOrWhiteSpace($matchedCacheName)) {
    throw "No online cache image matched the catalog hash index."
}
Write-Output "PASS online_cache_image=$matchedCacheFile NAME=$matchedCacheName"

$instanceFlags = [Reflection.BindingFlags]"Instance,NonPublic"
$worker = [Activator]::CreateInstance($workerType, $true)
$runtimeDirectory = Split-Path (Split-Path $ImageIndexPath -Parent) -Parent
$workerType.GetField("runtimeDirectory", $instanceFlags).SetValue(
    $worker,
    $runtimeDirectory
)
$workerType.GetMethod("LoadAssetNameLookup", $instanceFlags).Invoke($worker, @())
$workerType.GetMethod("LoadAssetImageLookup", $instanceFlags).Invoke($worker, @())
$loadedAssetIdCount = $workerType.GetField(
    "assetIdLookup",
    $instanceFlags
).GetValue($worker).Count
$loadedImageCount = $workerType.GetField(
    "imageHashLookup",
    $instanceFlags
).GetValue($worker).Count
if ($loadedAssetIdCount -le 0 -or $loadedImageCount -le 0) {
    Write-Output "INFO_native_asset_ids=$loadedAssetIdCount"
    Write-Output "INFO_native_images=$loadedImageCount"
    throw "Native worker lookup indexes were not loaded."
}
Write-Output "PASS native_worker_asset_ids=$loadedAssetIdCount native_worker_images=$loadedImageCount"
$workerType.GetField("webViewCacheDirectory", $instanceFlags).SetValue(
    $worker,
    $cacheDirectory
)
$resolveMethod = $workerType.GetMethod("ResolveRewardItemName", $instanceFlags)
$resolveArguments = @($null, 0, "", $null)
$fallbackName = $resolveMethod.Invoke($worker, $resolveArguments)
$expectedFallback = [string]::Concat(
    [char]0x672A, [char]0x8BC6, [char]0x522B, [char]0x7269, [char]0x54C1
)
if ($fallbackName -ne $expectedFallback) {
    throw "Null reward popup fallback failed."
}
Write-Output "PASS null_popup_fallback=$fallbackName"
$findMethod = $workerType.GetMethod("FindRewardCacheItemName", $instanceFlags)
$before = [System.Collections.Generic.Dictionary[string,string]](
    New-Object 'System.Collections.Generic.Dictionary[string,string]'
)
$workerCacheName = $findMethod.Invoke(
    $worker,
    @($before, [DateTime]::UtcNow.AddHours(-6))
)
if ([string]::IsNullOrWhiteSpace($workerCacheName)) {
    throw "Native worker could not match a recent online cache image."
}
Write-Output "PASS native_worker_cache_lookup=$workerCacheName"
