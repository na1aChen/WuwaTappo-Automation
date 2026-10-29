[CmdletBinding()]
param(
    [switch]$Integration
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$modulePath = Join-Path $projectRoot "src\WuwaTappoAutoChest.psm1"
Import-Module $modulePath -Force

$script:Passed = 0
$script:Failed = 0

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($Condition) {
        $script:Passed++
        Write-Output "PASS $Name"
    }
    else {
        $script:Failed++
        Write-Output "FAIL $Name"
    }
}

function Assert-Equal {
    param(
        $Actual,
        $Expected,
        [Parameter(Mandatory)]
        [string]$Name
    )

    Assert-True -Condition ($Actual -eq $Expected) -Name "$Name (actual=$Actual, expected=$Expected)"
}

$testRoot = Join-Path $env:TEMP ("wuwatappo-autochest-tests-{0}" -f [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

try {
    $stateDirectory = Join-Path $testRoot "state"
    $logDirectory = Join-Path $testRoot "logs"
    $configPath = Join-Path $testRoot "autochest.json"
    $configData = [ordered]@{
        app = [ordered]@{
            processName = "WuwaTappo"
            windowTitle = "Wuwa Tappo"
            launchIfMissing = $false
            executablePath = "E:\invalid-for-unit-test\WuwaTappo.exe"
        }
        automation = [ordered]@{
            pollIntervalSeconds = 1
            idlePollIntervalSeconds = 1
            confirmationPollMilliseconds = 100
            actionConfirmationTimeoutSeconds = 1
            actionCooldownSeconds = 5
            unknownOutcomeCooldownSeconds = 10
            maxOpensPerCycle = 2
            maxRuntimeMinutes = 0
            dryRun = $true
        }
        activity = [ordered]@{
            enabled = $false
            virtualKey = 135
            intervalMilliseconds = 1000
            burstSize = 2
            characterThreshold = 1000
        }
        storage = [ordered]@{
            stateDirectory = $stateDirectory
            logDirectory = $logDirectory
        }
        logging = [ordered]@{
            fileName = "test.jsonl"
            maxFileBytes = 1048576
            maxFiles = 3
        }
    }
    $configData | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8

    $config = Import-AutoChestConfig -ConfigPath $configPath
    Initialize-AutoChestStorage -Config $config

    Assert-Equal -Actual $config.App.ProcessName -Expected "WuwaTappo" -Name "配置加载 processName"
    Assert-Equal -Actual $config.Automation.PollIntervalSeconds -Expected 1 -Name "配置加载 pollIntervalSeconds"
    Assert-Equal -Actual $config.Automation.DryRun -Expected $true -Name "配置加载 dryRun"
    Assert-Equal -Actual $config.Activity.VirtualKey -Expected 135 -Name "配置加载活动虚拟键"
    Assert-Equal -Actual $config.Activity.Enabled -Expected $false -Name "配置加载活动开关"
    Assert-Equal -Actual $config.Activity.CharacterThreshold -Expected 1000 -Name "配置加载字符数补足门槛"
    Assert-Equal `
        -Actual $config.Paths.StateDirectory `
        -Expected ([System.IO.Path]::GetFullPath($stateDirectory)) `
        -Name "状态目录解析"

    $single = @([PSCustomObject]@{
        AncestorSide = $null
        Left = 100
        Side = $null
    })
    $resolvedSingle = @(Resolve-AutoChestButtonSides -Buttons $single)
    Assert-Equal -Actual $resolvedSingle[0].Side -Expected "single" -Name "单按钮槽位识别"

    $knownPair = @(
        [PSCustomObject]@{ AncestorSide = "right"; Left = 500; Side = $null },
        [PSCustomObject]@{ AncestorSide = "left"; Left = 100; Side = $null }
    )
    $resolvedPair = @(Resolve-AutoChestButtonSides -Buttons $knownPair)
    Assert-True -Condition (($resolvedPair | Where-Object Side -eq "left").Count -eq 1) -Name "父节点识别左槽位"
    Assert-True -Condition (($resolvedPair | Where-Object Side -eq "right").Count -eq 1) -Name "父节点识别右槽位"

    $unknownPair = @(
        [PSCustomObject]@{ AncestorSide = $null; Left = 600; Side = $null },
        [PSCustomObject]@{ AncestorSide = $null; Left = 200; Side = $null }
    )
    $resolvedUnknown = @(Resolve-AutoChestButtonSides -Buttons $unknownPair)
    Assert-Equal -Actual ($resolvedUnknown | Where-Object Left -eq 200).Side -Expected "left" -Name "坐标回退左槽位"
    Assert-Equal -Actual ($resolvedUnknown | Where-Object Left -eq 600).Side -Expected "right" -Name "坐标回退右槽位"

    $operationState = New-AutoChestOperationState
    $now = [datetimeoffset]::Now
    $operation = Start-AutoChestOperation `
        -State $operationState `
        -Side "left" `
        -ButtonClass "reward-btn" `
        -ButtonRect "1,2,3,4" `
        -UnknownCooldownSeconds 10 `
        -Now $now
    $pendingCheck = Test-AutoChestActionAllowed -State $operationState -Side "right" -Now $now
    Assert-Equal -Actual $pendingCheck.Allowed -Expected $false -Name "pending 阻止并发操作"
    Assert-Equal -Actual $pendingCheck.Reason -Expected "pending_operation" -Name "pending 原因码"

    Complete-AutoChestOperation `
        -State $operationState `
        -Operation $operation `
        -Result "unknown" `
        -CooldownSeconds 20 `
        -Evidence "unit_test" `
        -Now $now
    $leftCooldown = Test-AutoChestActionAllowed -State $operationState -Side "left" -Now $now
    Assert-Equal -Actual $leftCooldown.Allowed -Expected $false -Name "未知结果触发冷却"
    $leftReadyLater = Test-AutoChestActionAllowed -State $operationState -Side "left" -Now $now.AddSeconds(21)
    Assert-Equal -Actual $leftReadyLater.Allowed -Expected $true -Name "冷却结束后恢复"
    Assert-Equal -Actual $operationState.pending -Expected $null -Name "操作完成后清除 pending"

    $pendingState = New-AutoChestOperationState
    Start-AutoChestOperation `
        -State $pendingState `
        -Side "single" `
        -ButtonClass "reward-btn" `
        -ButtonRect "5,6,7,8" `
        -UnknownCooldownSeconds 30 | Out-Null
    Write-AutoChestOperationState -Config $config -State $pendingState
    $restoredState = Read-AutoChestOperationState -Config $config
    $restored = Restore-AutoChestPendingOperation -Config $config -State $restoredState
    Assert-Equal -Actual $restored -Expected $true -Name "恢复 pending 记录"
    Assert-Equal -Actual $restoredState.pending -Expected $null -Name "恢复后清除 pending"
    Assert-Equal -Actual $restoredState.sides.single.last_result -Expected "unknown" -Name "恢复按未知结果处理"

    $status = [ordered]@{
        worker_pid = $PID
        phase = "test"
        reward_button_count = 0
    }
    Write-AutoChestStatus -Config $config -Status $status
    $persistedStatus = Read-AutoChestStatus -Config $config
    Assert-Equal -Actual $persistedStatus.phase -Expected "test" -Name "状态文件原子读写"

    Write-AutoChestLog -Config $config -Level INFO -Event "unit_test_log" -Data @{
        button_count = 0
    }
    Assert-True -Condition (Test-Path -LiteralPath $config.Paths.LogFile) -Name "结构化日志写入"
    $logContent = Get-Content -LiteralPath $config.Paths.LogFile -Raw
    Assert-True -Condition ($logContent -match '"event":"unit_test_log"') -Name "日志事件可读取"
    Assert-True -Condition ($logContent -notmatch "Authorization|Bearer |infoToken|Cookie") -Name "日志未包含敏感字段"

    if ($Integration) {
        $realConfig = Import-AutoChestConfig -ConfigPath (Join-Path $projectRoot "config\autochest.json")
        $target = Get-AutoChestTargetWindow -Config $realConfig
        Assert-True -Condition ($null -ne $target) -Name "真实应用窗口可识别"
        if ($null -ne $target) {
            $buttons = @(Get-AutoChestRewardButtons -Target $target)
            Assert-True -Condition ($buttons.Count -ge 0) -Name "真实 UI Automation 只读扫描完成"
            $characterCount = Get-AutoChestCharacterCount -Target $target
            Assert-True -Condition ($null -ne $characterCount) -Name "真实字符数节点可读取"
            Write-Output "INFO integration_reward_button_count=$($buttons.Count)"
            Write-Output "INFO integration_character_count=$characterCount"
            Write-Output "INFO integration_app_process_id=$($target.Process.Id)"
            Write-Output "INFO integration_window_handle=$($target.Handle)"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $resolvedTemp = [System.IO.Path]::GetFullPath($env:TEMP)
        if ($resolvedTestRoot.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Output "SUMMARY passed=$script:Passed failed=$script:Failed"
if ($script:Failed -gt 0) {
    throw "$($script:Failed) 个测试失败。"
}
