Set-StrictMode -Version Latest

$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:DefaultPollIntervalSeconds = 10
$script:DefaultIdlePollIntervalSeconds = 30
$script:DefaultConfirmationPollMilliseconds = 500
$script:DefaultActionConfirmationTimeoutSeconds = 20
$script:DefaultActionCooldownSeconds = 90
$script:DefaultUnknownOutcomeCooldownSeconds = 900
$script:DefaultActionSettleMilliseconds = 2000
$script:DefaultMaxOpensPerCycle = 2
$script:DefaultActivityEnabled = $false
$script:DefaultActivityVirtualKey = 135
$script:DefaultActivityIntervalMilliseconds = 50
$script:DefaultActivityBurstSize = 20
$script:DefaultActivityCharacterThreshold = 1000
$script:DefaultLogMaxBytes = 5MB
$script:DefaultLogMaxFiles = 5

function ConvertTo-AutoChestHashtable {
    param(
        [AllowNull()]
        $InputObject
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $InputObject.Keys) {
            $result[[string]$key] = ConvertTo-AutoChestHashtable -InputObject $InputObject[$key]
        }
        return $result
    }

    if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        foreach ($property in $InputObject.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-AutoChestHashtable -InputObject $property.Value
        }
        return $result
    }

    if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += ,(ConvertTo-AutoChestHashtable -InputObject $item)
        }
        return ,$items
    }

    return $InputObject
}

function ConvertFrom-AutoChestJson {
    param(
        [Parameter(Mandatory)]
        [string]$Json
    )

    return ConvertTo-AutoChestHashtable -InputObject ($Json | ConvertFrom-Json)
}

function Get-AutoChestHashValue {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Table,
        [Parameter(Mandatory)]
        [string]$Name,
        $DefaultValue
    )

    if ($Table.Contains($Name) -and $null -ne $Table[$Name]) {
        return $Table[$Name]
    }

    return $DefaultValue
}

function ConvertTo-AutoChestPositiveNumber {
    param(
        [Parameter(Mandatory)]
        $Value,
        [Parameter(Mandatory)]
        [string]$Name,
        [switch]$AllowZero
    )

    $number = 0.0
    if (-not [double]::TryParse([string]$Value, [ref]$number)) {
        throw "配置项 $Name 必须是数字。"
    }

    if (($AllowZero -and $number -lt 0) -or (-not $AllowZero -and $number -le 0)) {
        $operator = if ($AllowZero) { "大于或等于 0" } else { "大于 0" }
        throw "配置项 $Name 必须$operator。"
    }

    return $number
}

function Resolve-AutoChestPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Import-AutoChestConfig {
    [CmdletBinding()]
    param(
        [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = if ($env:WUWA_TAPPO_AUTO_CHEST_CONFIG) {
            $env:WUWA_TAPPO_AUTO_CHEST_CONFIG
        }
        else {
            Join-Path $script:ProjectRoot "config\autochest.json"
        }
    }

    $resolvedConfigPath = Resolve-AutoChestPath -Path $ConfigPath -BasePath $script:ProjectRoot
    if (-not (Test-Path -LiteralPath $resolvedConfigPath -PathType Leaf)) {
        throw "找不到配置文件：$resolvedConfigPath。请先复制 config/autochest.example.json。"
    }

    try {
        $raw = ConvertFrom-AutoChestJson -Json (
            Get-Content -LiteralPath $resolvedConfigPath -Raw -Encoding UTF8
        )
    }
    catch {
        throw "配置文件不是有效 JSON：$resolvedConfigPath。$($_.Exception.Message)"
    }

    $app = Get-AutoChestHashValue -Table $raw -Name "app" -DefaultValue @{}
    $automation = Get-AutoChestHashValue -Table $raw -Name "automation" -DefaultValue @{}
    $activity = Get-AutoChestHashValue -Table $raw -Name "activity" -DefaultValue @{}
    $storage = Get-AutoChestHashValue -Table $raw -Name "storage" -DefaultValue @{}
    $logging = Get-AutoChestHashValue -Table $raw -Name "logging" -DefaultValue @{}

    $processName = [string](Get-AutoChestHashValue -Table $app -Name "processName" -DefaultValue "WuwaTappo")
    if ($processName.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        $processName = $processName.Substring(0, $processName.Length - 4)
    }
    if ([string]::IsNullOrWhiteSpace($processName)) {
        throw "配置项 app.processName 不能为空。"
    }

    $windowTitle = [string](Get-AutoChestHashValue -Table $app -Name "windowTitle" -DefaultValue "Wuwa Tappo")
    $launchIfMissing = [bool](Get-AutoChestHashValue -Table $app -Name "launchIfMissing" -DefaultValue $false)
    $executablePath = [string](Get-AutoChestHashValue -Table $app -Name "executablePath" -DefaultValue "")
    if ([string]::IsNullOrWhiteSpace($executablePath) -and $env:WUWA_TAPPO_EXE) {
        $executablePath = $env:WUWA_TAPPO_EXE
    }
    if (-not [string]::IsNullOrWhiteSpace($executablePath)) {
        $executablePath = Resolve-AutoChestPath -Path $executablePath -BasePath $script:ProjectRoot
    }

    $pollIntervalSeconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "pollIntervalSeconds" -DefaultValue $script:DefaultPollIntervalSeconds) `
        -Name "automation.pollIntervalSeconds"
    $idlePollIntervalSeconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "idlePollIntervalSeconds" -DefaultValue $script:DefaultIdlePollIntervalSeconds) `
        -Name "automation.idlePollIntervalSeconds"
    $confirmationPollMilliseconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "confirmationPollMilliseconds" -DefaultValue $script:DefaultConfirmationPollMilliseconds) `
        -Name "automation.confirmationPollMilliseconds"
    $confirmationTimeoutSeconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "actionConfirmationTimeoutSeconds" -DefaultValue $script:DefaultActionConfirmationTimeoutSeconds) `
        -Name "automation.actionConfirmationTimeoutSeconds"
    $actionCooldownSeconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "actionCooldownSeconds" -DefaultValue $script:DefaultActionCooldownSeconds) `
        -Name "automation.actionCooldownSeconds"
    $unknownOutcomeCooldownSeconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "unknownOutcomeCooldownSeconds" -DefaultValue $script:DefaultUnknownOutcomeCooldownSeconds) `
        -Name "automation.unknownOutcomeCooldownSeconds"
    $actionSettleMilliseconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "actionSettleMilliseconds" -DefaultValue $script:DefaultActionSettleMilliseconds) `
        -Name "automation.actionSettleMilliseconds" `
        -AllowZero
    $activityEnabled = [bool](Get-AutoChestHashValue -Table $activity -Name "enabled" -DefaultValue $script:DefaultActivityEnabled)
    $activityVirtualKey = [int](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $activity -Name "virtualKey" -DefaultValue $script:DefaultActivityVirtualKey) `
        -Name "activity.virtualKey")
    if ($activityVirtualKey -lt 1 -or $activityVirtualKey -gt 255) {
        throw "配置项 activity.virtualKey 必须在 1 到 255 之间。"
    }
    $activityIntervalMilliseconds = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $activity -Name "intervalMilliseconds" -DefaultValue $script:DefaultActivityIntervalMilliseconds) `
        -Name "activity.intervalMilliseconds" `
        -AllowZero
    $activityBurstSize = [int](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $activity -Name "burstSize" -DefaultValue $script:DefaultActivityBurstSize) `
        -Name "activity.burstSize")
    if ($activityBurstSize -lt 1 -or $activityBurstSize -gt 1000) {
        throw "配置项 activity.burstSize 必须在 1 到 1000 之间。"
    }
    $activityCharacterThreshold = [long](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $activity -Name "characterThreshold" -DefaultValue $script:DefaultActivityCharacterThreshold) `
        -Name "activity.characterThreshold")
    $maxOpensPerCycle = [int](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "maxOpensPerCycle" -DefaultValue $script:DefaultMaxOpensPerCycle) `
        -Name "automation.maxOpensPerCycle")
    $maxRuntimeMinutes = ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $automation -Name "maxRuntimeMinutes" -DefaultValue 0) `
        -Name "automation.maxRuntimeMinutes" `
        -AllowZero
    $dryRun = [bool](Get-AutoChestHashValue -Table $automation -Name "dryRun" -DefaultValue $false)

    $stateDirectory = [string](Get-AutoChestHashValue -Table $storage -Name "stateDirectory" -DefaultValue "state")
    $logDirectory = [string](Get-AutoChestHashValue -Table $storage -Name "logDirectory" -DefaultValue "logs")
    if ([string]::IsNullOrWhiteSpace($stateDirectory)) {
        throw "配置项 storage.stateDirectory 不能为空。"
    }
    if ([string]::IsNullOrWhiteSpace($logDirectory)) {
        throw "配置项 storage.logDirectory 不能为空。"
    }

    $stateDirectory = Resolve-AutoChestPath -Path $stateDirectory -BasePath $script:ProjectRoot
    $logDirectory = Resolve-AutoChestPath -Path $logDirectory -BasePath $script:ProjectRoot
    $logFileName = [string](Get-AutoChestHashValue -Table $logging -Name "fileName" -DefaultValue "autochest.jsonl")
    $logMaxBytes = [long](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $logging -Name "maxFileBytes" -DefaultValue $script:DefaultLogMaxBytes) `
        -Name "logging.maxFileBytes")
    $logMaxFiles = [int](ConvertTo-AutoChestPositiveNumber `
        -Value (Get-AutoChestHashValue -Table $logging -Name "maxFiles" -DefaultValue $script:DefaultLogMaxFiles) `
        -Name "logging.maxFiles")

    $paths = @{
        StateDirectory = $stateDirectory
        LogDirectory = $logDirectory
        StatusFile = Join-Path $stateDirectory "status.json"
        OperationStateFile = Join-Path $stateDirectory "operation-state.json"
        PidFile = Join-Path $stateDirectory "worker.pid"
        StopFile = Join-Path $stateDirectory "stop.requested"
        LockFile = Join-Path $stateDirectory "worker.lock"
        LogFile = Join-Path $logDirectory $logFileName
        WatchScript = Join-Path $script:ProjectRoot "scripts\Watch-AutoChest.ps1"
    }

    return [PSCustomObject]@{
        SchemaVersion = 1
        ConfigPath = $resolvedConfigPath
        ProjectRoot = $script:ProjectRoot
        App = [PSCustomObject]@{
            ProcessName = $processName
            WindowTitle = $windowTitle
            LaunchIfMissing = $launchIfMissing
            ExecutablePath = $executablePath
        }
        Automation = [PSCustomObject]@{
            PollIntervalSeconds = $pollIntervalSeconds
            IdlePollIntervalSeconds = $idlePollIntervalSeconds
            ConfirmationPollMilliseconds = [int]$confirmationPollMilliseconds
            ActionConfirmationTimeoutSeconds = $confirmationTimeoutSeconds
            ActionCooldownSeconds = $actionCooldownSeconds
            UnknownOutcomeCooldownSeconds = $unknownOutcomeCooldownSeconds
            ActionSettleMilliseconds = [int]$actionSettleMilliseconds
            MaxOpensPerCycle = $maxOpensPerCycle
            MaxRuntimeMinutes = $maxRuntimeMinutes
            DryRun = $dryRun
        }
        Activity = [PSCustomObject]@{
            Enabled = $activityEnabled
            VirtualKey = $activityVirtualKey
            IntervalMilliseconds = [int]$activityIntervalMilliseconds
            BurstSize = $activityBurstSize
            CharacterThreshold = $activityCharacterThreshold
        }
        Logging = [PSCustomObject]@{
            MaxFileBytes = $logMaxBytes
            MaxFiles = $logMaxFiles
        }
        Paths = $paths
    }
}

function Initialize-AutoChestStorage {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config
    )

    foreach ($directory in @($Config.Paths.StateDirectory, $Config.Paths.LogDirectory)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
    }
}

function Write-AutoChestJsonAtomic {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        $Data
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $temporaryPath = "$Path.$PID.tmp"
    $json = $Data | ConvertTo-Json -Depth 16
    $utf8 = [System.Text.UTF8Encoding]::new($false)

    try {
        [System.IO.File]::WriteAllText($temporaryPath, $json, $utf8)
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-AutoChestLog {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [ValidateSet("DEBUG", "INFO", "WARN", "ERROR")]
        [string]$Level,
        [Parameter(Mandatory)]
        [string]$Event,
        [hashtable]$Data = @{}
    )

    try {
        $logFile = $Config.Paths.LogFile
        $logDirectory = Split-Path -Parent $logFile
        if (-not (Test-Path -LiteralPath $logDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        }

        if ((Test-Path -LiteralPath $logFile -PathType Leaf) -and
            (Get-Item -LiteralPath $logFile).Length -ge $Config.Logging.MaxFileBytes) {
            $oldestLog = "$logFile.$($Config.Logging.MaxFiles)"
            if (Test-Path -LiteralPath $oldestLog) {
                Remove-Item -LiteralPath $oldestLog -Force -ErrorAction SilentlyContinue
            }
            for ($index = $Config.Logging.MaxFiles - 1; $index -ge 1; $index--) {
                $source = "$logFile.$index"
                $destination = "$logFile.$($index + 1)"
                if (Test-Path -LiteralPath $source) {
                    Move-Item -LiteralPath $source -Destination $destination -Force
                }
            }
            Move-Item -LiteralPath $logFile -Destination "$logFile.1" -Force
        }

        $entry = [ordered]@{
            time = (Get-Date).ToString("o")
            level = $Level
            event = $Event
            worker_pid = $PID
            data = $Data
        }
        $line = ($entry | ConvertTo-Json -Compress -Depth 10) + [Environment]::NewLine
        [System.IO.File]::AppendAllText(
            $logFile,
            $line,
            [System.Text.UTF8Encoding]::new($false)
        )
    }
    catch {
        # 日志失败不能中断主循环；调用方仍可通过状态文件观察运行状态。
    }
}

function Write-AutoChestStatus {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Status
    )

    $Status.updated_at = (Get-Date).ToString("o")
    Write-AutoChestJsonAtomic -Path $Config.Paths.StatusFile -Data $Status
}

function Read-AutoChestStatus {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config
    )

    if (-not (Test-Path -LiteralPath $Config.Paths.StatusFile -PathType Leaf)) {
        return $null
    }

    try {
        return ConvertFrom-AutoChestJson -Json (
            Get-Content -LiteralPath $Config.Paths.StatusFile -Raw -Encoding UTF8
        )
    }
    catch {
        return $null
    }
}

function New-AutoChestOperationState {
    return [ordered]@{
        schema_version = 1
        pending = $null
        sides = [ordered]@{
            left = [ordered]@{ cooldown_until = $null; last_result = $null; last_operation_id = $null }
            right = [ordered]@{ cooldown_until = $null; last_result = $null; last_operation_id = $null }
            single = [ordered]@{ cooldown_until = $null; last_result = $null; last_operation_id = $null }
        }
        operations = @()
    }
}

function Read-AutoChestOperationState {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config
    )

    $defaultState = New-AutoChestOperationState
    $path = $Config.Paths.OperationStateFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return $defaultState
    }

    try {
        $state = ConvertFrom-AutoChestJson -Json (
            Get-Content -LiteralPath $path -Raw -Encoding UTF8
        )
    }
    catch {
        Write-AutoChestLog -Config $Config -Level WARN -Event "state_read_failed" -Data @{
            reason_code = "invalid_json"
        }
        return $defaultState
    }

    if (-not $state.Contains("sides") -or $state.sides -isnot [System.Collections.IDictionary]) {
        $state.sides = $defaultState.sides
    }
    foreach ($side in @("left", "right", "single")) {
        if (-not $state.sides.Contains($side)) {
            $state.sides[$side] = $defaultState.sides[$side]
        }
    }
    if (-not $state.Contains("pending")) {
        $state.pending = $null
    }
    if (-not $state.Contains("operations")) {
        $state.operations = @()
    }
    if (-not $state.Contains("schema_version")) {
        $state.schema_version = 1
    }

    return $state
}

function Write-AutoChestOperationState {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State
    )

    Write-AutoChestJsonAtomic -Path $Config.Paths.OperationStateFile -Data $State
}

function Get-AutoChestSideState {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State,
        [Parameter(Mandatory)]
        [string]$Side
    )

    if (-not $State.sides.Contains($Side)) {
        $State.sides[$Side] = [ordered]@{
            cooldown_until = $null
            last_result = $null
            last_operation_id = $null
        }
    }

    return $State.sides[$Side]
}

function Test-AutoChestActionAllowed {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State,
        [Parameter(Mandatory)]
        [string]$Side,
        [datetimeoffset]$Now = [datetimeoffset]::Now
    )

    if ($null -ne $State.pending) {
        return [PSCustomObject]@{
            Allowed = $false
            Reason = "pending_operation"
            RemainingSeconds = 0
        }
    }

    $sideState = Get-AutoChestSideState -State $State -Side $Side
    $cooldownUntil = $null
    if ($sideState.cooldown_until) {
        try {
            $cooldownUntil = [datetimeoffset]::Parse([string]$sideState.cooldown_until)
        }
        catch {
            $cooldownUntil = $null
        }
    }

    if ($null -ne $cooldownUntil -and $cooldownUntil -gt $Now) {
        return [PSCustomObject]@{
            Allowed = $false
            Reason = "cooldown"
            RemainingSeconds = [math]::Ceiling(($cooldownUntil - $Now).TotalSeconds)
        }
    }

    return [PSCustomObject]@{
        Allowed = $true
        Reason = "ready"
        RemainingSeconds = 0
    }
}

function Add-AutoChestOperationRecord {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Operation
    )

    $operations = @($State.operations)
    if ($operations.Count -ge 50) {
        $operations = @($operations | Select-Object -Last 49)
    }
    $State.operations = @($operations + @($Operation))
}

function Start-AutoChestOperation {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State,
        [Parameter(Mandatory)]
        [string]$Side,
        [Parameter(Mandatory)]
        [string]$ButtonClass,
        [Parameter(Mandatory)]
        [string]$ButtonRect,
        [Parameter(Mandatory)]
        [double]$UnknownCooldownSeconds,
        [datetimeoffset]$Now = [datetimeoffset]::Now
    )

    $operationId = [guid]::NewGuid().ToString("N")
    $operation = [ordered]@{
        operation_id = $operationId
        side = $Side
        button_class = $ButtonClass
        button_rect = $ButtonRect
        detected_at = $Now.ToString("o")
        attempted_at = $null
        confirmed_at = $null
        status = "submitting"
        evidence = $null
    }
    $State.pending = $operation
    $sideState = Get-AutoChestSideState -State $State -Side $Side
    $sideState.cooldown_until = $Now.AddSeconds($UnknownCooldownSeconds).ToString("o")
    return $operation
}

function Complete-AutoChestOperation {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$Operation,
        [Parameter(Mandatory)]
        [ValidateSet("confirmed", "unknown", "failed")]
        [string]$Result,
        [Parameter(Mandatory)]
        [double]$CooldownSeconds,
        [string]$Evidence,
        [datetimeoffset]$Now = [datetimeoffset]::Now
    )

    $Operation.status = $Result
    $Operation.confirmed_at = $Now.ToString("o")
    $Operation.evidence = $Evidence
    $side = [string]$Operation.side
    $sideState = Get-AutoChestSideState -State $State -Side $side
    $sideState.cooldown_until = $Now.AddSeconds($CooldownSeconds).ToString("o")
    $sideState.last_result = $Result
    $sideState.last_operation_id = [string]$Operation.operation_id
    Add-AutoChestOperationRecord -State $State -Operation $Operation
    $State.pending = $null
}

function Restore-AutoChestPendingOperation {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [System.Collections.IDictionary]$State
    )

    if ($null -eq $State.pending) {
        return $false
    }

    $pending = $State.pending
    $side = [string]$pending.side
    if ([string]::IsNullOrWhiteSpace($side)) {
        $side = "single"
    }
    Complete-AutoChestOperation `
        -State $State `
        -Operation $pending `
        -Result "unknown" `
        -CooldownSeconds $Config.Automation.UnknownOutcomeCooldownSeconds `
        -Evidence "recovered_pending_after_restart"
    Write-AutoChestOperationState -Config $Config -State $State
    Write-AutoChestLog -Config $Config -Level WARN -Event "pending_operation_recovered" -Data @{
        side = $side
        operation_id = [string]$pending.operation_id
    }
    return $true
}

function Initialize-AutoChestUIAutomation {
    try {
        Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
        Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop
    }
    catch {
        throw "无法加载 Windows UI Automation 程序集：$($_.Exception.Message)"
    }
}

function Get-AutoChestTargetWindow {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config
    )

    Initialize-AutoChestUIAutomation
    $processes = @(Get-Process -Name $Config.App.ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 })
    if ($processes.Count -eq 0) {
        return $null
    }

    $selected = $null
    if (-not [string]::IsNullOrWhiteSpace($Config.App.WindowTitle)) {
        $selected = $processes |
            Where-Object { $_.MainWindowTitle -eq $Config.App.WindowTitle } |
            Select-Object -First 1
    }
    if ($null -eq $selected) {
        $selected = $processes | Select-Object -First 1
    }

    try {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle(
            [IntPtr]$selected.MainWindowHandle
        )
        if ($null -eq $window) {
            return $null
        }
    }
    catch {
        return $null
    }

    return [PSCustomObject]@{
        Process = $selected
        Window = $window
        Handle = [long]$selected.MainWindowHandle
        Title = [string]$selected.MainWindowTitle
    }
}

function Get-AutoChestAncestorSide {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    $current = $Element
    for ($depth = 0; $depth -lt 12; $depth++) {
        try {
            $className = [string]$current.Current.ClassName
            if ($className -match "reward-anchor--left") {
                return "left"
            }
            if ($className -match "reward-anchor--right") {
                return "right"
            }
            $current = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($current)
            if ($null -eq $current) {
                break
            }
        }
        catch {
            break
        }
    }

    return $null
}

function Resolve-AutoChestButtonSides {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Buttons
    )

    $list = @($Buttons)
    if ($list.Count -eq 0) {
        return @()
    }

    if ($list.Count -eq 1) {
        $side = [string]$list[0].AncestorSide
        if ($side -notin @("left", "right")) {
            $side = "single"
        }
        $list[0].Side = $side
        return $list
    }

    $unknown = @($list | Where-Object { [string]$_.AncestorSide -notin @("left", "right") })
    if ($unknown.Count -gt 0) {
        $usedSides = @($list | ForEach-Object { [string]$_.AncestorSide } | Where-Object { $_ -in @("left", "right") })
        $availableSides = @(@("left", "right") | Where-Object { $_ -notin $usedSides })
        $orderedUnknown = @($unknown | Sort-Object { [double]$_.Left })

        foreach ($button in $orderedUnknown) {
            if ($availableSides.Count -gt 0) {
                $button.Side = $availableSides[0]
                $availableSides = @($availableSides | Select-Object -Skip 1)
            }
            else {
                $button.Side = "ambiguous"
            }
        }
    }

    foreach ($button in $list) {
        if ([string]$button.AncestorSide -in @("left", "right")) {
            $button.Side = [string]$button.AncestorSide
        }
    }

    return $list
}

function Get-AutoChestRewardButtons {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Target
    )

    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
    )

    try {
        $elements = $Target.Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $buttonCondition
        )
    }
    catch {
        throw "读取 WebView UI Automation 树失败：$($_.Exception.Message)"
    }

    $buttons = @()
    foreach ($element in $elements) {
        try {
            $current = $element.Current
            if ($current.ClassName -notmatch "(^|\s)reward-btn(\s|$)") {
                continue
            }
            $rect = $current.BoundingRectangle
            $buttons += [PSCustomObject]@{
                Element = $element
                ClassName = [string]$current.ClassName
                AutomationId = [string]$current.AutomationId
                Name = [string]$current.Name
                Enabled = [bool]$current.IsEnabled
                Offscreen = [bool]$current.IsOffscreen
                Left = [double]$rect.Left
                Top = [double]$rect.Top
                Width = [double]$rect.Width
                Height = [double]$rect.Height
                AncestorSide = Get-AutoChestAncestorSide -Element $element
                Side = $null
            }
        }
        catch {
            continue
        }
    }

    $resolved = @(Resolve-AutoChestButtonSides -Buttons $buttons)
    return @($resolved | Sort-Object @{ Expression = { if ($_.Side -eq "left") { 0 } elseif ($_.Side -eq "right") { 1 } else { 2 } } }, Left)
}

function Get-AutoChestButtonStatusData {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Buttons
    )

    return @($Buttons | ForEach-Object {
        [ordered]@{
            side = [string]$_.Side
            class_name = [string]$_.ClassName
            enabled = [bool]$_.Enabled
            offscreen = [bool]$_.Offscreen
            rect = [ordered]@{
                left = [math]::Round([double]$_.Left, 1)
                top = [math]::Round([double]$_.Top, 1)
                width = [math]::Round([double]$_.Width, 1)
                height = [math]::Round([double]$_.Height, 1)
            }
        }
    })
}

function Get-AutoChestCharacterCount {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Target
    )

    $textCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text
    )

    try {
        $elements = $Target.Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $textCondition
        )
    }
    catch {
        return $null
    }

    foreach ($element in $elements) {
        try {
            $current = $element.Current
            if ($current.Name -notmatch "^\s*\d[\d,]*\s*$") {
                continue
            }

            $insidePetDock = $false
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
            for ($depth = 0; $depth -lt 6 -and $null -ne $parent; $depth++) {
                if ([string]$parent.Current.ClassName -match "pet-dock-content") {
                    $insidePetDock = $true
                    break
                }
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            }
            if (-not $insidePetDock) {
                continue
            }

            $normalized = ([string]$current.Name).Replace(",", "").Trim()
            $value = 0L
            if ([long]::TryParse($normalized, [ref]$value)) {
                return $value
            }
        }
        catch {
            continue
        }
    }

    return $null
}

function Initialize-AutoChestInputInjection {
    if ("WuwaTappoAutoChestNativeInput" -as [type]) {
        return
    }

    Add-Type @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class WuwaTappoAutoChestNativeInput
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    private static void SendKey(byte virtualKey)
    {
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = virtualKey;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = virtualKey;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static int SendKeyBurst(byte virtualKey, int count, double intervalMilliseconds)
    {
        if (count <= 0)
            return 0;

        intervalMilliseconds = Math.Max(0.0, intervalMilliseconds);
        long intervalTicks = (long)(Stopwatch.Frequency * intervalMilliseconds / 1000.0);
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int index = 0; index < count; index++)
        {
            SendKey(virtualKey);

            long targetTicks = (index + 1) * intervalTicks;
            while (stopwatch.ElapsedTicks < targetTicks)
            {
                if (targetTicks - stopwatch.ElapsedTicks > Stopwatch.Frequency / 2000)
                    Thread.Sleep(0);
                else
                    Thread.SpinWait(10);
            }
        }

        return count;
    }
}
"@
}

function Send-AutoChestActivityKey {
    param(
        [Parameter(Mandatory)]
        [ValidateRange(1, 255)]
        [int]$VirtualKey
    )

    Initialize-AutoChestInputInjection
    $key = [byte]$VirtualKey
    [WuwaTappoAutoChestNativeInput]::SendKeyBurst($key, 1, 0.0) | Out-Null
}

function Send-AutoChestActivityKeyBurst {
    param(
        [Parameter(Mandatory)]
        [ValidateRange(1, 255)]
        [int]$VirtualKey,
        [Parameter(Mandatory)]
        [ValidateRange(1, 1000)]
        [int]$Count,
        [Parameter(Mandatory)]
        [ValidateRange(0.0, 60000.0)]
        [double]$IntervalMilliseconds
    )

    Initialize-AutoChestInputInjection
    return [WuwaTappoAutoChestNativeInput]::SendKeyBurst(
        [byte]$VirtualKey,
        $Count,
        $IntervalMilliseconds
    )
}

function Invoke-AutoChestActivityPulse {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [int]$MaxPresses
    )

    $target = Get-AutoChestTargetWindow -Config $Config
    if ($null -eq $target) {
        return [PSCustomObject]@{
            Reason = "app_window_not_found"
            PressesSent = 0
            CountBefore = $null
            CountAfter = $null
        }
    }

    $countBefore = Get-AutoChestCharacterCount -Target $target
    if ($null -eq $countBefore) {
        return [PSCustomObject]@{
            Reason = "character_count_unavailable"
            PressesSent = 0
            CountBefore = $null
            CountAfter = $null
        }
    }

    $currentCount = $countBefore
    if (Test-Path -LiteralPath $Config.Paths.StopFile -PathType Leaf) {
        return [PSCustomObject]@{
            Reason = "stop_requested"
            PressesSent = 0
            CountBefore = $countBefore
            CountAfter = $currentCount
        }
    }

    $sends = 0
    try {
        $sends = Send-AutoChestActivityKeyBurst `
            -VirtualKey $Config.Activity.VirtualKey `
            -Count $MaxPresses `
            -IntervalMilliseconds $Config.Activity.IntervalMilliseconds
    }
    catch {
        return [PSCustomObject]@{
            Reason = "input_injection_failed"
            PressesSent = $sends
            CountBefore = $countBefore
            CountAfter = $currentCount
        }
    }

    $target = Get-AutoChestTargetWindow -Config $Config
    if ($null -eq $target) {
        return [PSCustomObject]@{
            Reason = "app_window_not_found"
            PressesSent = $sends
            CountBefore = $countBefore
            CountAfter = $currentCount
        }
    }

    $buttons = @(Get-AutoChestRewardButtons -Target $target)
    if ($buttons.Count -gt 0) {
        return [PSCustomObject]@{
            Reason = "reward_ready"
            PressesSent = $sends
            CountBefore = $countBefore
            CountAfter = $currentCount
        }
    }

    $nextCount = Get-AutoChestCharacterCount -Target $target
    if ($null -eq $nextCount) {
        return [PSCustomObject]@{
            Reason = "character_count_unavailable"
            PressesSent = $sends
            CountBefore = $countBefore
            CountAfter = $currentCount
        }
    }
    if ($nextCount -gt $currentCount) {
        $currentCount = $nextCount
    }

    return [PSCustomObject]@{
        Reason = "pulse_complete"
        PressesSent = $sends
        CountBefore = $countBefore
        CountAfter = $currentCount
    }
}

function Invoke-AutoChestButton {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Button
    )

    if (-not $Button.Enabled -or $Button.Offscreen) {
        throw "宝箱按钮当前不可安全调用。"
    }

    try {
        $pattern = $Button.Element.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern
        )
    }
    catch {
        throw "宝箱按钮不支持 InvokePattern。"
    }

    if ($null -eq $pattern) {
        throw "宝箱按钮不支持 InvokePattern。"
    }

    $pattern.Invoke()
}

function Test-AutoChestRewardPopupPresent {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Target
    )

    try {
        $elements = $Target.Window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition
        )
        foreach ($element in $elements) {
            try {
                if ([string]$element.Current.ClassName -match "chest-reward-popup") {
                    return $true
                }
            }
            catch {
                continue
            }
        }
    }
    catch {
        return $false
    }

    return $false
}

function Wait-AutoChestConfirmation {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [string]$Side
    )

    $deadline = (Get-Date).AddSeconds($Config.Automation.ActionConfirmationTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Config.Paths.StopFile -PathType Leaf) {
            return [PSCustomObject]@{ Result = "unknown"; Evidence = "stop_requested_during_confirmation" }
        }

        try {
            $target = Get-AutoChestTargetWindow -Config $Config
            if ($null -eq $target) {
                return [PSCustomObject]@{ Result = "unknown"; Evidence = "target_window_disappeared" }
            }

            $buttons = @(Get-AutoChestRewardButtons -Target $target)
            $matchingButton = if ($Side -eq "single") {
                $null
            }
            else {
                $buttons | Where-Object { $_.Side -eq $Side } | Select-Object -First 1
            }
            $popupPresent = Test-AutoChestRewardPopupPresent -Target $target

            if ($popupPresent) {
                return [PSCustomObject]@{ Result = "confirmed"; Evidence = "reward_popup" }
            }
            if ($Side -eq "single" -and $buttons.Count -eq 0) {
                return [PSCustomObject]@{ Result = "confirmed"; Evidence = "single_button_removed" }
            }
            if ($Side -ne "single" -and $null -eq $matchingButton) {
                return [PSCustomObject]@{ Result = "confirmed"; Evidence = "side_button_removed" }
            }
        }
        catch {
            # UI 树在动画刷新期间可能短暂不可读，继续等待。
        }

        Start-Sleep -Milliseconds $Config.Automation.ConfirmationPollMilliseconds
    }

    return [PSCustomObject]@{ Result = "unknown"; Evidence = "confirmation_timeout" }
}

function Wait-AutoChestDelay {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [Parameter(Mandatory)]
        [double]$Seconds
    )

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $Config.Paths.StopFile -PathType Leaf) {
            return $false
        }
        $remaining = ($deadline - (Get-Date)).TotalMilliseconds
        $sleepMilliseconds = [int][math]::Min(250, [math]::Max(1, $remaining))
        [System.Threading.Thread]::Sleep($sleepMilliseconds)
    }
    return $true
}

function Get-AutoChestWorkerProcess {
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config
    )

    if (-not (Test-Path -LiteralPath $Config.Paths.PidFile -PathType Leaf)) {
        return $null
    }

    $workerPid = 0
    try {
        $rawPidContent = (Get-Content -LiteralPath $Config.Paths.PidFile -Raw).Trim()
        try {
            $pidData = ConvertFrom-AutoChestJson -Json $rawPidContent
            if ($pidData.Contains("worker_pid")) {
                $workerPid = [int]$pidData.worker_pid
            }
        }
        catch {
            $workerPid = [int]$rawPidContent
        }
    }
    catch {
        return $null
    }

    if ($workerPid -le 0) {
        return $null
    }

    $process = Get-Process -Id $workerPid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }
    if ($process.ProcessName -notin @("pwsh", "powershell")) {
        return $null
    }

    try {
        $cim = Get-CimInstance Win32_Process -Filter "ProcessId = $workerPid" -ErrorAction Stop
        if ($cim.CommandLine -notmatch "Watch-AutoChest\.ps1") {
            $encodedMatch = [regex]::Match(
                [string]$cim.CommandLine,
                "-EncodedCommand\s+([A-Za-z0-9+/=]+)",
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
            if (-not $encodedMatch.Success) {
                return $null
            }
            try {
                $decodedCommand = [System.Text.Encoding]::Unicode.GetString(
                    [Convert]::FromBase64String($encodedMatch.Groups[1].Value)
                )
            }
            catch {
                return $null
            }
            if ($decodedCommand -notmatch "Watch-AutoChest\.ps1") {
                return $null
            }
        }
    }
    catch {
        return $null
    }

    return $process
}

function Invoke-AutoChestWorker {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSCustomObject]$Config,
        [switch]$Once,
        [switch]$DryRun,
        [switch]$DisableActivity
    )

    Initialize-AutoChestStorage -Config $Config
    $dryRunEnabled = $DryRun.IsPresent -or $Config.Automation.DryRun
    $activityEnabled = $Config.Activity.Enabled -and -not $dryRunEnabled -and -not $DisableActivity.IsPresent
    $startedAt = Get-Date
    $openedThisCycle = 0
    $runtimeSeconds = if ($Config.Automation.MaxRuntimeMinutes -gt 0) {
        $Config.Automation.MaxRuntimeMinutes * 60
    }
    else {
        [double]::PositiveInfinity
    }

    $lockStream = $null
    $status = [ordered]@{
        schema_version = 1
        worker_pid = $PID
        phase = "starting"
        started_at = $startedAt.ToString("o")
        updated_at = $null
        dry_run = $dryRunEnabled
        app_found = $false
        app_process_id = $null
        window_handle = $null
        last_scan_at = $null
        reward_button_count = 0
        buttons = @()
        character_count = $null
        activity_enabled = $activityEnabled
        activity_keys_sent = 0
        activity_last_result = $null
        last_action = $null
        last_error = $null
        message = "worker starting"
    }

    try {
        try {
            $lockStream = [System.IO.File]::Open(
                $Config.Paths.LockFile,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        }
        catch {
            $status.phase = "blocked"
            $status.last_error = "worker_already_running"
            $status.message = "another worker holds the lock"
            Write-AutoChestStatus -Config $Config -Status $status
            throw "已有另一个自动开宝箱工作进程在运行。"
        }

        Write-AutoChestJsonAtomic -Path $Config.Paths.PidFile -Data @{
            worker_pid = $PID
            started_at = $startedAt.ToString("o")
        }
        Write-AutoChestStatus -Config $Config -Status $status
        Write-AutoChestLog -Config $Config -Level INFO -Event "worker_started" -Data @{
            dry_run = $dryRunEnabled
            activity_enabled = $activityEnabled
            config_path = $Config.ConfigPath
        }

        $operationState = Read-AutoChestOperationState -Config $Config
        if (Restore-AutoChestPendingOperation -Config $Config -State $operationState) {
            $status.last_error = "pending_operation_recovered"
            $status.message = "previous operation outcome is unknown; cooldown applied"
        }

        $lastLoggedButtonCount = -1
        $lastAppState = $null
        $lastAppErrorAt = [datetime]::MinValue

        while ($true) {
            if (Test-Path -LiteralPath $Config.Paths.StopFile -PathType Leaf) {
                $status.phase = "stopping"
                $status.message = "stop request received"
                Write-AutoChestStatus -Config $Config -Status $status
                break
            }

            if (((Get-Date) - $startedAt).TotalSeconds -ge $runtimeSeconds) {
                $status.phase = "stopping"
                $status.message = "maximum runtime reached"
                Write-AutoChestStatus -Config $Config -Status $status
                break
            }

            $openedThisCycle = 0
            try {
                $target = Get-AutoChestTargetWindow -Config $Config
                if ($null -eq $target) {
                    $status.phase = "blocked"
                    $status.app_found = $false
                    $status.app_process_id = $null
                    $status.window_handle = $null
                    $status.last_scan_at = (Get-Date).ToString("o")
                    $status.reward_button_count = 0
                    $status.buttons = @()
                    $status.last_error = "app_window_not_found"
                    $status.message = "start Wuwa Tappo or verify its window title"
                    Write-AutoChestStatus -Config $Config -Status $status
                    if ($lastAppState -ne "missing" -or ((Get-Date) - $lastAppErrorAt).TotalSeconds -ge 60) {
                        Write-AutoChestLog -Config $Config -Level WARN -Event "app_window_not_found"
                        $lastAppErrorAt = Get-Date
                    }
                    $lastAppState = "missing"
                    if ($Once) {
                        break
                    }
                    if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.IdlePollIntervalSeconds)) {
                        continue
                    }
                    continue
                }

                $status.app_found = $true
                $status.app_process_id = [int]$target.Process.Id
                $status.window_handle = [long]$target.Handle
                $status.last_scan_at = (Get-Date).ToString("o")
                $status.last_error = $null
                $lastAppState = "found"

                $buttons = @(Get-AutoChestRewardButtons -Target $target)
                $status.character_count = Get-AutoChestCharacterCount -Target $target
                $status.reward_button_count = $buttons.Count
                $status.buttons = @(Get-AutoChestButtonStatusData -Buttons $buttons)

                if ($buttons.Count -eq 0) {
                    $status.phase = "waiting_for_chest"
                    $status.message = if ($activityEnabled) {
                        "no reward button yet; accumulating normal character count"
                    }
                    else {
                        "no enabled reward button is currently rendered"
                    }
                    Write-AutoChestStatus -Config $Config -Status $status
                    if ($lastLoggedButtonCount -ne 0) {
                        Write-AutoChestLog -Config $Config -Level INFO -Event "waiting_for_chest" -Data @{
                            app_process_id = [int]$target.Process.Id
                            character_count = $status.character_count
                            activity_enabled = $activityEnabled
                        }
                        $lastLoggedButtonCount = 0
                    }
                    if ($Once) {
                        break
                    }

                    if ($activityEnabled) {
                        if ($null -ne $status.character_count -and
                            $status.character_count -ge $Config.Activity.CharacterThreshold) {
                            $status.phase = "waiting_for_chest"
                            $status.activity_last_result = "character_threshold_reached"
                            $status.message = "character threshold reached; activity injection paused"
                            Write-AutoChestStatus -Config $Config -Status $status
                            if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.IdlePollIntervalSeconds)) {
                                continue
                            }
                            continue
                        }

                        $activityResult = Invoke-AutoChestActivityPulse `
                            -Config $Config `
                            -MaxPresses $Config.Activity.BurstSize
                        $status.activity_keys_sent = [int]$status.activity_keys_sent + [int]$activityResult.PressesSent
                        $status.character_count = $activityResult.CountAfter
                        $status.activity_last_result = $activityResult.Reason
                        Write-AutoChestLog -Config $Config -Level INFO -Event "activity_pulse" -Data @{
                            presses_sent = [int]$activityResult.PressesSent
                            character_count_before = $activityResult.CountBefore
                            character_count_after = $activityResult.CountAfter
                            result = $activityResult.Reason
                        }

                        if ($activityResult.Reason -eq "input_injection_failed") {
                            $activityEnabled = $false
                            $status.activity_enabled = $false
                            $status.last_error = "activity_disabled"
                            $status.message = "activity was disabled because Windows input injection failed"
                            Write-AutoChestLog -Config $Config -Level WARN -Event "activity_disabled" -Data @{
                                reason_code = $activityResult.Reason
                            }
                        }
                        elseif ($activityResult.Reason -eq "reward_ready") {
                            $status.message = "reward became available during activity accumulation"
                        }

                        Write-AutoChestStatus -Config $Config -Status $status
                        if ($activityEnabled) {
                            continue
                        }
                    }

                    if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.IdlePollIntervalSeconds)) {
                        continue
                    }
                    continue
                }

                if ($lastLoggedButtonCount -ne $buttons.Count) {
                    Write-AutoChestLog -Config $Config -Level INFO -Event "reward_buttons_detected" -Data @{
                        button_count = $buttons.Count
                        buttons = $status.buttons
                    }
                    $lastLoggedButtonCount = $buttons.Count
                }

                if ($dryRunEnabled) {
                    $status.phase = "candidate_detected"
                    $status.message = "dry-run detected reward button(s); no click performed"
                    Write-AutoChestStatus -Config $Config -Status $status
                    if ($Once) {
                        break
                    }
                    if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.PollIntervalSeconds)) {
                        continue
                    }
                    continue
                }

                for ($actionIndex = 0; $actionIndex -lt $Config.Automation.MaxOpensPerCycle; $actionIndex++) {
                    $target = Get-AutoChestTargetWindow -Config $Config
                    if ($null -eq $target) {
                        break
                    }
                    if (Test-AutoChestRewardPopupPresent -Target $target) {
                        $status.phase = "cooldown"
                        $status.message = "reward popup is still settling; next action deferred"
                        Write-AutoChestStatus -Config $Config -Status $status
                        break
                    }
                    $buttons = @(Get-AutoChestRewardButtons -Target $target)
                    $candidate = $null
                    foreach ($button in $buttons) {
                        if (-not $button.Enabled -or $button.Offscreen -or $button.Side -eq "ambiguous") {
                            continue
                        }
                        $allowed = Test-AutoChestActionAllowed -State $operationState -Side ([string]$button.Side)
                        if ($allowed.Allowed) {
                            $candidate = $button
                            break
                        }
                    }

                    if ($null -eq $candidate) {
                        $status.phase = "cooldown"
                        $status.message = "reward button exists but no side is currently eligible"
                        Write-AutoChestStatus -Config $Config -Status $status
                        break
                    }

                    $side = [string]$candidate.Side
                    $rectText = "{0},{1},{2},{3}" -f `
                        [math]::Round($candidate.Left, 1), `
                        [math]::Round($candidate.Top, 1), `
                        [math]::Round($candidate.Width, 1), `
                        [math]::Round($candidate.Height, 1)
                    $operation = Start-AutoChestOperation `
                        -State $operationState `
                        -Side $side `
                        -ButtonClass $candidate.ClassName `
                        -ButtonRect $rectText `
                        -UnknownCooldownSeconds $Config.Automation.UnknownOutcomeCooldownSeconds
                    Write-AutoChestOperationState -Config $Config -State $operationState

                    $status.phase = "submitting"
                    $status.message = "invoking reward button"
                    $status.last_action = [ordered]@{
                        operation_id = [string]$operation.operation_id
                        side = $side
                        status = "submitting"
                        time = (Get-Date).ToString("o")
                    }
                    Write-AutoChestStatus -Config $Config -Status $status
                    Write-AutoChestLog -Config $Config -Level INFO -Event "chest_open_submitting" -Data @{
                        operation_id = [string]$operation.operation_id
                        side = $side
                        button_class = [string]$candidate.ClassName
                        button_rect = $rectText
                    }

                    try {
                        Invoke-AutoChestButton -Button $candidate
                        $operation.attempted_at = (Get-Date).ToString("o")
                        Write-AutoChestOperationState -Config $Config -State $operationState
                        $status.phase = "confirming"
                        $status.message = "waiting for reward UI confirmation"
                        Write-AutoChestStatus -Config $Config -Status $status
                        $confirmation = Wait-AutoChestConfirmation -Config $Config -Side $side
                    }
                    catch {
                        $operation.attempted_at = (Get-Date).ToString("o")
                        $confirmation = [PSCustomObject]@{
                            Result = "unknown"
                            Evidence = "invoke_failed"
                        }
                        Write-AutoChestLog -Config $Config -Level ERROR -Event "chest_open_invoke_failed" -Data @{
                            operation_id = [string]$operation.operation_id
                            side = $side
                            reason_code = $_.Exception.Message
                        }
                    }

                    if ($confirmation.Result -eq "confirmed") {
                        Complete-AutoChestOperation `
                            -State $operationState `
                            -Operation $operation `
                            -Result "confirmed" `
                            -CooldownSeconds $Config.Automation.ActionCooldownSeconds `
                            -Evidence $confirmation.Evidence
                        $openedThisCycle++
                        $status.last_action = [ordered]@{
                            operation_id = [string]$operation.operation_id
                            side = $side
                            status = "confirmed"
                            evidence = [string]$confirmation.Evidence
                            time = (Get-Date).ToString("o")
                        }
                        Write-AutoChestLog -Config $Config -Level INFO -Event "chest_open_confirmed" -Data @{
                            operation_id = [string]$operation.operation_id
                            side = $side
                            evidence = [string]$confirmation.Evidence
                        }
                    }
                    else {
                        Complete-AutoChestOperation `
                            -State $operationState `
                            -Operation $operation `
                            -Result "unknown" `
                            -CooldownSeconds $Config.Automation.UnknownOutcomeCooldownSeconds `
                            -Evidence $confirmation.Evidence
                        $status.last_action = [ordered]@{
                            operation_id = [string]$operation.operation_id
                            side = $side
                            status = "unknown"
                            evidence = [string]$confirmation.Evidence
                            time = (Get-Date).ToString("o")
                        }
                        Write-AutoChestLog -Config $Config -Level WARN -Event "chest_open_unconfirmed" -Data @{
                            operation_id = [string]$operation.operation_id
                            side = $side
                            evidence = [string]$confirmation.Evidence
                        }
                    }

                    Write-AutoChestOperationState -Config $Config -State $operationState
                    $status.phase = if ($openedThisCycle -gt 0) { "cooldown" } else { "blocked" }
                    $status.message = if ($openedThisCycle -gt 0) {
                        "chest opened and cooldown applied"
                    }
                    else {
                        "operation result is unknown; no immediate retry"
                    }
                    Write-AutoChestStatus -Config $Config -Status $status

                    if ($confirmation.Result -ne "confirmed") {
                        break
                    }
                    if ($actionIndex + 1 -lt $Config.Automation.MaxOpensPerCycle -and
                        $Config.Automation.ActionSettleMilliseconds -gt 0) {
                        if (-not (Wait-AutoChestDelay `
                            -Config $Config `
                            -Seconds ($Config.Automation.ActionSettleMilliseconds / 1000.0))) {
                            break
                        }
                    }
                }

                if ($Once) {
                    break
                }
                if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.PollIntervalSeconds)) {
                    continue
                }
            }
            catch {
                $status.phase = "blocked"
                $status.last_error = "scan_error"
                $status.message = $_.Exception.Message
                Write-AutoChestStatus -Config $Config -Status $status
                Write-AutoChestLog -Config $Config -Level ERROR -Event "scan_error" -Data @{
                    reason_code = $_.Exception.GetType().Name
                }
                if ($Once) {
                    break
                }
                if (-not (Wait-AutoChestDelay -Config $Config -Seconds $Config.Automation.IdlePollIntervalSeconds)) {
                    continue
                }
            }
        }
    }
    finally {
        if ($null -ne $lockStream) {
            $lockStream.Dispose()
        }
        try {
            $status.phase = "stopped"
            $status.message = "worker stopped"
            Write-AutoChestStatus -Config $Config -Status $status
        }
        catch {
            # 状态目录可能已不可访问，退出时不再抛出次生异常。
        }
        if (Test-Path -LiteralPath $Config.Paths.PidFile -PathType Leaf) {
            Remove-Item -LiteralPath $Config.Paths.PidFile -Force -ErrorAction SilentlyContinue
        }
        Write-AutoChestLog -Config $Config -Level INFO -Event "worker_stopped" -Data @{
            opened_this_cycle = $openedThisCycle
        }
    }
}

Export-ModuleMember -Function @(
    "Import-AutoChestConfig",
    "Initialize-AutoChestStorage",
    "Write-AutoChestJsonAtomic",
    "Write-AutoChestLog",
    "Write-AutoChestStatus",
    "Read-AutoChestStatus",
    "New-AutoChestOperationState",
    "Read-AutoChestOperationState",
    "Write-AutoChestOperationState",
    "Test-AutoChestActionAllowed",
    "Start-AutoChestOperation",
    "Complete-AutoChestOperation",
    "Restore-AutoChestPendingOperation",
    "Initialize-AutoChestUIAutomation",
    "Get-AutoChestTargetWindow",
    "Resolve-AutoChestButtonSides",
    "Get-AutoChestRewardButtons",
    "Get-AutoChestButtonStatusData",
    "Get-AutoChestCharacterCount",
    "Initialize-AutoChestInputInjection",
    "Send-AutoChestActivityKey",
    "Send-AutoChestActivityKeyBurst",
    "Invoke-AutoChestActivityPulse",
    "Invoke-AutoChestButton",
    "Test-AutoChestRewardPopupPresent",
    "Wait-AutoChestConfirmation",
    "Get-AutoChestWorkerProcess",
    "Invoke-AutoChestWorker"
)
