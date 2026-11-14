# Wuwa Tappo 自动字符数与开宝箱工具 by Phrolova2405

这是一个 Windows 后台辅助程序，用于自动发现并开启 Wuwa Tappo 自身界面中已经可用的宝箱。

常驻 Worker 通过 `F24` 键输入走 Wuwa Tappo 自身的正常计数机制，自动累积字符数和活跃时间，不会直接访问内部 HTTP API 或读取登录凭据。新原生测试包另带一个显式的一次性 Host：它通过仅限 `127.0.0.1` 的 WebView2 CDP 动态发现并调用当前页面自己的增量计数和保存函数，避免持续写入 Windows 全局键鼠队列。两者都不伪造服务器写请求；登录、验证码和 30 分钟宝箱生成规则仍由 Wuwa Tappo 自己处理。

## 功能

- 后台静默监测 `WuwaTappo.exe` 主窗口；
- 字符数低于 10,000 时持续自动补充，达到目标后停止输入；
- 支持手动输入本次增加量，执行一次性补足并在完成后停止；
- 检测到宝箱可开启后立即停止活动注入；
- 通过 Windows UI Automation 查找真实的 `button.reward-btn`；
- 识别左、右宝箱槽位；
- 使用按钮的 `InvokePattern` 开启宝箱；
- 等待按钮消失或奖励弹窗出现；
- 通过奖励图片 SHA-256 与本地图鉴映射真实物品名称；
- 状态消息显示本次实际开出的物品；
- 自动开箱历史持久化到便携目录，跨 Worker 和 App 重启保留；
- 单实例运行，防止两个工作进程同时操作；
- 点击前写入持久化 pending 记录；
- 左右槽位独立冷却；
- 结果未知时不立即重试；
- 提供启动、停止、状态查询、结构化日志和运行超时；
- 支持无副作用的 `DryRun` 模式。
- 附带不改账号的离线全图鉴浏览器。

## 离线全图鉴

当前已从官方公开资源接口生成包含 **899 项**的本地图鉴：

```text
角色 260 + 配件 135 + 表情 504
```

直接双击打开：

```text
gallery/index.html
```

页面包含全部本地图片，可搜索并按类别、稀有度筛选，不需要登录或联网，也不会修改 Wuwa Tappo 账号。详细说明见 [docs/atlas-gallery.md](docs/atlas-gallery.md)。

## Windows 便携版

推荐使用最终恢复包，可直接打包发送给朋友，且不覆盖原 Hybrid：

```text
dist/WuwaTappoAutoChest-Portable-Native-Recovery/
dist/WuwaTappoAutoChest-Portable-Native-Recovery.zip
```

原 Native 包继续保留，便于回退和对照：

```text
dist/WuwaTappoAutoChest-Portable-Native/
dist/WuwaTappoAutoChest-Portable-Native.zip
```

朋友解压后启动并登录 Wuwa Tappo，再双击：

```text
WuwaTappoAutoChest.exe
```

发布 ZIP 的历史目录为空，不会包含制作者本机的开箱记录。朋友从旧版本升级时：

1. 先停止自动助手后台并关闭控制面板；
2. 双击新目录中的 `Import-LegacyHistory.cmd`；
3. 选择旧版本目录下的 `runtime\state\history.jsonl`；
4. 工具按 `operation_id` 去重合并，并自动备份新包原历史。

这解决的是不同便携目录之间无法自动共享历史的问题；不联网，也不会上传历史。

控制面板支持启动后台、停止后台、查看状态和打开离线图鉴。关闭控制面板不会停止后台任务。

便携版使用原生 C# 后台进程，只依赖 Windows 自带的 .NET Framework 4.8 和 UI Automation，不需要 PowerShell 7，也不会再启动 PowerShell Worker。采用 x86 便携构建并加入按可见行渲染、UIA 属性缓存和自适应扫描后，实测 Worker 约 43.3 MB，Worker 加控制面板约 78–88 MB，空闲 CPU 约 0.7% 单核。必须发送完整 ZIP，不能只发送 EXE。详细说明见 [docs/portable.md](docs/portable.md)。

自动补次数采用每 500 个 F24 合并为一次 `SendInput` 的批量方式。一次性和旧自动模式共享这条输入路径；达标后的空闲占用约 0.62% 单核，Worker 内存约 41 MB。

控制面板采用 Codex 风格深色界面，状态区和历史记录区为纯黑背景，指标卡和按钮使用响应式流式布局，支持窗口缩放和 DPI 缩放。最小推荐尺寸为 740 × 780。

### 页面原生一次性补次数

当前 Wuwa Tappo 1.0.4 / WebView2 157 已实测可以通过宿主参数开放随机回环调试端口。新 Host 不再逐次派发页面鼠标事件，而是先检查当前构建的页面能力和唯一导出，再调用页面自己的增量函数与强制保存函数。

便携目录还包含独立的管理员辅助程序：

```text
WuwaTappoAutoChest-CdpHost.exe
```

它用于 F24 长任务中途失效时的一次性补次数。使用步骤：

1. 在主控制面板点击“停止后台”；
2. 关闭主控制面板；
3. 双击 `WuwaTappoAutoChest-CdpHost.exe`，在 UAC 提示中选择“是”；
4. 输入本次增加量并启动；
5. 完成、取消或失败后，等待 Wuwa Tappo 自动以无调试端口模式重新打开。

命令行入口：

```powershell
WuwaTappoAutoChest-CdpHost.exe --native-refill 1000
WuwaTappoAutoChest-CdpHost.exe --native-refill 50000
WuwaTappoAutoChest-CdpHost.exe --native-refill 500000
```

Host 会关闭并重启 Wuwa Tappo，通过随机 `127.0.0.1` 调试端口动态发现页面模块。它不读取或保存 Cookie、Token、Authorization、账号资料或请求头，也不产生系统级鼠标移动或点击。页面只向 Host 返回当前账号的 SHA-256 摘要，用于检测任务期间是否切换账号。

每批最多 1000 次。同一批先校验增量，再调用保存函数；保存后重新读取页面状态，确认可用次数和累计次数都达到预期。保存失败只重试保存，不重放已经增加的计数。

安全保护包括：

- 检测到主自动助手仍在运行时拒绝启动；
- 调试接口只接受回环地址和本次随机端口；
- CDP 请求和页面脚本有超时；
- 页面能力不唯一、登录变化或计数不一致时立即停止；
- 保存后未确认先做 5 次只读复核；核心计数稳定未前进时最多安全重基准 5 次；
- WebSocket 断开时最多恢复 3 次会话，每次按页面绝对计数重新对账；
- 无论成功、取消还是异常，都会在 `finally` 阶段关闭调试端口并重启 Wuwa Tappo；
- 强制结束 CDP Host 进程属于异常中断，此时应人工关闭并重新打开 Wuwa Tappo。

当前 1.0.4 最终恢复版已完成 500,000 和 1,000,000 次真实补数验收：

- 500,000 次耗时约 262.8 秒，命中一次安全回滚重基准；
- 1,000,000 次耗时约 525.1 秒，命中两次安全回滚重基准；
- 两轮结束时可用次数和累计次数均精确达到绝对目标；
- Host 工作集约 46 MB，结束后调试端口关闭并恢复普通启动；
- 没有结果未知后重发整批，也没有重复计数。

证据见 [docs/test-results.md](docs/test-results.md)。

### UIA 无鼠标输入恢复

F24 批量输入仍是主路径。新 Worker 在一次批次 1.2 秒没有增长时，会对 `.pet-dock-content` 调用 UI Automation `InvokePattern`，用它重新激活页面输入处理，然后继续 F24。该调用不移动系统鼠标、不抢前台焦点，也不会连续盲目触发。

实测：

- 单次 `InvokePattern`：字符数增加 1；
- 1000 个 F24：字符数精确增加 1000，实际调用耗时约 2.7 秒；
- 连续快速调用 `InvokePattern` 会被页面合并，因此不会采用“一毫秒一个 UIA 点击”的伪高速方案。

## 自动化边界

以下内容有意不做：

- 不自动输入账号、密码、短信或验证码；
- 不绕过 Geetest、风控或登录过期；
- 常驻 Worker 不直接修改字符数、宝箱计时或存档；
- 一次性 Host 只调用当前页面已有的计数与保存能力，不伪造服务器请求；
- 不直接调用 `/api/frontend/chests/open`；
- 不猜测或拼接未公开的服务器补数 HTTP 接口；
- 不导入或复制浏览器 Cookie、Authorization 或 Token；
- 不开启应用尚未渲染出的宝箱。

CDP Host 是单独的显式一次性入口，不会随主助手自动启动。

## 环境要求

- Windows 10 或 Windows 11 x64；
- PowerShell 7（`pwsh`，仅源码测试和重新构建需要；便携 EXE 不需要）；
- 已安装并至少能手动正常运行的 Wuwa Tappo；
- 已由用户手动完成登录；
- Wuwa Tappo 主窗口存在。默认不自动启动应用，以避免触发 UAC 或重复授权。

当前验证环境：

```text
Windows 11 专业版 10.0.26100
Wuwa Tappo 1.0.4 UAT CN
Tauri + WebView2 157.0.4341.46
```

恢复版把批次错误拆成写入前冲突、增量中断、保存后未确认和保存失败。`store-state-changed` 与可证明已保存的增量中断会按最新页面快照重算剩余量，连续 5 次失败才停止；保存后未确认只做只读复核，不重新增加整批。详细设计见 [docs/retry-recovery-design.md](docs/retry-recovery-design.md)。

## 快速开始

1. 启动并登录在线版 Wuwa Tappo；
2. 打开 `dist/WuwaTappoAutoChest-Portable-Native-Recovery/WuwaTappoAutoChest.exe`；
3. 点击“启动后台”；
4. 控制面板显示运行状态、字符数、阶段、真实物品名和自动开箱历史；
5. 小批量补次数可在“一次性补次数”中输入 1,000–1,000,000，点击“开始一次补足”；
6. F24 失效或需要一次补足时，停止后台，再运行同目录的 `WuwaTappoAutoChest-CdpHost.exe`；
7. 关闭控制面板不会停止后台，再次打开 EXE 可查看历史或停止任务。

命令行入口：

```powershell
.\dist\WuwaTappoAutoChest-Portable-Native-Recovery\WuwaTappoAutoChest.exe --start-background
.\dist\WuwaTappoAutoChest-Portable-Native-Recovery\WuwaTappoAutoChest.exe --stop-background
```

源码级只读诊断仍保留在 `scripts/`，但发送给朋友时使用便携目录中的原生 EXE 即可。

## 配置说明

| 配置 | 默认值 | 说明 |
|---|---:|---|
| `app.processName` | `WuwaTappo` | 目标进程名，不含 `.exe` |
| `app.windowTitle` | `Wuwa Tappo` | 主窗口标题 |
| `app.processId` | `0` | 可选的进程 ID 快速绑定；`0` 表示自动匹配在线版 |
| `app.webViewLocalStorageDirectory` | 空 | 在线版 WebView2 `leveldb` 目录；空值使用默认中文版路径 |
| `app.launchIfMissing` | `false` | 是否在工作进程启动时尝试启动应用 |
| `app.executablePath` | 当前安装路径 | 应用可执行文件 |
| `automation.pollIntervalSeconds` | `1` | 有按钮但暂不可操作时的轮询间隔 |
| `automation.idlePollIntervalSeconds` | `5` | 无按钮时的轮询间隔 |
| `automation.confirmationPollMilliseconds` | `250` | 点击后的界面确认轮询间隔 |
| `automation.actionConfirmationTimeoutSeconds` | `20` | 单次点击的结果确认超时 |
| `automation.actionCooldownSeconds` | `3` | 确认成功后的同侧冷却；仅防瞬间重复 |
| `automation.unknownOutcomeCooldownSeconds` | `900` | 结果未知后的同侧冷却 |
| `automation.actionSettleMilliseconds` | `500` | 两个槽位操作之间的最小稳定等待 |
| `automation.maxOpensPerCycle` | `2` | 每轮最多尝试的槽位数 |
| `automation.maxRuntimeMinutes` | `0` | 最大运行时间；`0` 表示不限制 |
| `automation.dryRun` | `false` | 配置级只读模式 |
| `activity.enabled` | `false` | 是否启用旧的持续自动计数；一次性补次数不受此开关限制 |
| `activity.virtualKey` | `135` | 注入的 Windows 虚拟键，135 为 F24 |
| `activity.keyMode` | `chord` | `single` 仅用 virtualKey，`chord` 同时按 F21–F24，`cycle` 依次循环 |
| `activity.intervalMilliseconds` | `0` | 每键附加等待；0 表示按系统吞吐执行 |
| `activity.burstSize` | `1000` | 每轮目标上限，内部按 500 键合并并动态校验计数 |
| `activity.characterThreshold` | `10000` | 自动补足到该字符数后暂停活动注入 |
| `storage.stateDirectory` | `state` | 状态目录 |
| `storage.logDirectory` | `logs` | 日志目录 |

相对路径相对于本工程根目录。可以用环境变量 `WUWA_TAPPO_AUTO_CHEST_CONFIG` 覆盖默认配置文件，用 `WUWA_TAPPO_EXE` 覆盖应用路径。见 [.env.example](.env.example)。

`.env` 不会自动加载；如需使用，请在启动 PowerShell 前设置进程环境变量。

## 运行状态

主要阶段：

```text
starting
waiting_for_chest
candidate_detected
submitting
confirming
cooldown
blocked
stopping
stopped
```

`blocked` 不一定代表进程退出。例如 `app_window_not_found` 时，工作进程仍会继续等待应用出现。

自动开箱历史保存在：

```text
runtime/state/history.jsonl
```

记录包含时间、槽位、物品名称、映射来源和结果。文件位于便携目录内部，移动整个目录后历史不会丢失；控制面板显示最近 100 条。

## 防重复机制

1. 通过独占锁文件确保单实例。
2. 使用应用自己的真实按钮，保留登录、字符数和单次开启锁。
3. 调用按钮前先原子写入 `pending` 操作记录。
4. 成功后对相应侧设置冷却。
5. 超时或调用异常按“结果未知”处理，并设置较长冷却。
6. 进程中断后恢复 pending 记录时，同样按未知结果处理，不自动重放。

因此程序可能暂时少开启一次宝箱，但设计目标是优先避免重复提交。

## 日志

日志为 JSON Lines：

```text
logs/autochest.jsonl
```

常见事件：

| 事件 | 含义 |
|---|---|
| `worker_started` | 后台工作进程启动 |
| `waiting_for_chest` | 当前没有可开启按钮 |
| `reward_buttons_detected` | 发现宝箱按钮 |
| `chest_open_submitting` | 已写入 pending 并准备调用按钮 |
| `chest_open_confirmed` | 按钮消失或奖励弹窗出现 |
| `chest_open_unconfirmed` | 结果未知，已进入长冷却 |
| `manual_refill_requested` | 收到一次性补次数请求 |
| `manual_refill_started` | 已计算基准值和目标值并开始补足 |
| `manual_refill_progress` | 约每 10 秒记录字符数、发送数、内存和 Worker CPU |
| `manual_refill_completed` | 一次性补次数完成 |
| `manual_refill_cancelled` | 取消完成 |
| `manual_refill_failed` | 输入注入或运行异常导致失败 |
| `manual_refill_needs_focus` | 自动恢复后仍无法增加字符数 |
| `activity_uia_recovery` | F24 停滞后通过 UIA 重新激活页面 |
| `activity_uia_recovery_failed` | UIA 恢复调用失败 |
| `scan_error` | UI Automation 或环境异常 |
| `worker_stopped` | 工作进程退出 |

日志默认单文件上限 5 MiB，保留 5 个历史文件。日志不会记录账号、手机号、Cookie、Token 或请求头。

## 测试

### 自动测试（含真实应用只读集成）

```powershell
pwsh -NoProfile -File .\tests\Run-Tests.ps1 -Integration
```

### 在线奖励名称映射测试

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tests\Test-NativeRewardLookup.ps1
```

### 便携包与原生计数 Host 结构测试

```powershell
pwsh -NoProfile -File .\tools\Build-Portable.ps1 `
  -OutputDirectory .\dist\WuwaTappoAutoChest-Portable-Native-Recovery `
  -Force

pwsh -NoProfile -File .\tests\Test-CdpHostPackage.ps1 `
  -PackageDirectory .\dist\WuwaTappoAutoChest-Portable-Native-Recovery `
  -ExpectEmptyState

pwsh -NoProfile -File .\tests\Test-NativeRefillPackage.ps1 `
  -PackageDirectory .\dist\WuwaTappoAutoChest-Portable-Native-Recovery `
  -ExpectEmptyState

pwsh -NoProfile -File .\tests\Test-CdpHostPathWalk.ps1

node .\tests\Test-BoostRecovery.mjs
```

### 后台生命周期测试

```powershell
# 以 DryRun 启动
pwsh -NoProfile -File .\scripts\Start-AutoChest.ps1 `
  -ConfigPath .\config\autochest.json `
  -DryRun

# 检查
pwsh -NoProfile -File .\scripts\Get-AutoChestStatus.ps1 `
  -ConfigPath .\config\autochest.json `
  -Json

# 停止
pwsh -NoProfile -File .\scripts\Stop-AutoChest.ps1 `
  -ConfigPath .\config\autochest.json
```

真实测试记录见 [docs/test-results.md](docs/test-results.md)。

## 故障排查

### 状态显示 `app_found=false`

- 手动启动并登录 Wuwa Tappo；
- 检查进程名是否为 `WuwaTappo`；
- 检查窗口标题是否为 `Wuwa Tappo`；
- 不要依赖托盘隐藏状态作为主窗口句柄。

### 一直显示 `waiting_for_chest`

- 当前没有宝箱；
- 两个槽位可能都被占用或仍在计时；
- 程序正在正常累积字符数时可能持续保持该状态；
- 如果状态中的 `activity_keys_sent` 不再增长或 `last_error=activity_disabled`，检查 Windows 输入注入是否被安全软件拦截。

### 出现 `ambiguous`

WebView UI Automation 结构可能与当前版本不一致。程序会拒绝猜测点击。请停止工作进程并重新进行只读诊断。

### 出现 `pending_operation_recovered`

上次进程在点击前后中断，程序无法确定服务端结果，因此主动进入未知结果冷却。等待冷却结束，并在应用背包中人工核对。

### 原生 Host 无法启动或补数

- 先停止主助手后台并关闭控制面板；
- 确认 UAC 已允许本次管理员运行；
- 确认 Wuwa Tappo 已登录且版本能正常显示宝箱；
- 查看 `runtime/logs/cdp-host.jsonl` 和 `runtime/logs/cdp-host-cli-error.txt`；
- 出现 `login-or-account-changed`、`store-state-changed` 或 `post-save-state-not-confirmed` 时不要连续重试，先人工核对页面数量；
- 强制结束 Host 后，手动关闭并重新打开 Wuwa Tappo，以移除临时调试端口参数。

### Wuwa Tappo 更新后不再开箱

先通过控制面板或命令停止并重新启动后台 Worker。应用更新会重启 WebView 和账号会话，旧 Worker 可能仍保留更新前的点击确认状态。

已在 1.0.2 验证：宝箱按钮、InvokePattern 和开箱接口仍可用；重启 Worker 后能够正常确认开箱。若重启后仍持续 `confirmation_timeout`，请保留日志并重新检查 UIA 结构。

### PowerShell 无法启动工作进程

- 确认使用 PowerShell 7：`pwsh --version`；
- 确认 `Start-AutoChest.ps1` 路径存在；
- 查看 `state/status.json` 和 `logs/autochest.jsonl`；
- 应用若以更高完整性级别运行，停止应用应由用户通过应用菜单完成，本工具不会强制结束它。

## 已知限制

- 活动累积依赖应用仍接受全局键盘计数；当前 UAT 构建已验证 F24 能增加字符数，升级后需复测。
- 注入会向当前前台应用发送无文本含义的 F24，不切换焦点、不移动鼠标；极少数应用若占用 F24，可修改 `activity.virtualKey` 或禁用活动模式。
- 默认在字符数低于 10,000 时持续注入；达到目标后完全停止输入，只低频轮询宝箱。宝箱入口仍由应用自身的 1,000 字符显示门槛控制。
- 已验证一次真实自动开箱：`reward-btn` 被自动调用后消失，状态记录为 `confirmed`，证据为 `single_button_removed`。
- UI Automation 依赖应用当前 `.reward-btn` 和 `reward-anchor--left/right` 结构；应用升级后需重新验证。
- 真实物品名优先来自奖励图片 SHA-256。若 WebView2 命中旧缓存且没有写入新图片，同时资产 ID 也不在图鉴内，程序会明确显示“名称未提供”，不会猜测名称。
- 当前 UAT 主程序签名状态为 `HashMismatch`。用户已确认信任该文件夹，但生产分发前仍建议重新校验可信安装包。
- 未登录、Geetest、强制更新和维护公告需要人工处理。
- 原生 Host 已在 1.0.4 通过 500,000 和 1,000,000 次真实补数，但单次通过不等于统计意义上的 99%；首次在新环境使用时仍建议先跑 1,000 和 5,000。
- 页面导出名称是当前构建值，发布版通过资源扫描动态发现；应用更新后若能力不唯一，Host 会拒绝执行。
- Host 依赖短暂的本地 WebView2 调试端口；它不接受非回环连接，也不会读取 Cookie 或 Token。

## 项目结构

```text
config/                         配置示例与当前本机配置
docs/investigation.md           调查结论
docs/design.md                  设计方案
docs/native-count-refill-design.md 页面原生计数补次数设计与实测
docs/test-results.md            实际测试记录
docs/retry-recovery-design.md    50 万级稳定性与恢复状态机方案
docs/atlas-gallery.md           离线全图鉴使用说明
docs/portable.md                便携版与 EXE 使用说明
gallery/index.html              已生成的全图鉴页面
gallery/images/                 899 张本地图鉴图片
dist/                           便携目录与 ZIP
portable/                       EXE 启动器源码
portable/CdpHost.cs             管理员 WebView2 CDP 一次性补次数源码
portable/cdp-host.manifest      CDP Host 的 requireAdministrator 清单
portable/NativePageRefill.cs    页面能力发现、快照与保存确认
portable/NativeRecoveryPolicy.cs 批次恢复预算与安全重基准判定
portable/native/                动态发现、只读快照和批次补数脚本
scripts/                        启动、停止、状态和工作进程入口
src/WuwaTappoAutoChest.psm1     核心自动化模块
tests/Run-Tests.ps1             自动化测试
tests/Run-AtlasGalleryTests.ps1 图鉴生成结果测试
tests/Test-NativeRewardLookup.ps1 在线奖励缓存与真实名称映射测试
tests/Test-NativeRefillPackage.ps1 原生补数包结构与隐私边界测试
tools/Build-AtlasGallery.ps1    官方公开资源到离线图鉴的生成器
tools/Build-Portable.ps1         便携包构建脚本
state/                          运行状态（已忽略）
logs/                           运行日志（已忽略）
```

## 安全说明

- 不在源码、配置或日志中保存凭据。
- `.env.example` 不包含真实密钥。
- `.gitignore` 忽略本机配置、状态、日志和临时文件。
- 使用前请确认账号、应用条款以及所在环境允许自动化操作。

<!-- Project marker: by Phrolova2405 -->
