# Wuwa Tappo 自动字符数与开宝箱工具

这是一个 Windows 后台辅助程序，用于自动发现并开启 Wuwa Tappo 自身界面中已经可用的宝箱。

程序通过限速 `F24` 键输入走 Wuwa Tappo 自身的正常计数机制，自动累积字符数和活跃时间；它不会直接修改存档、计数或宝箱计时，也不会直接访问内部 HTTP API 或读取登录凭据。为显示真实奖励名称，Worker 只读取在线版 WebView2 的本地奖励资产 ID 与图片缓存，不输出 Cookie、Token 或请求头。登录、验证码和 30 分钟宝箱生成规则仍由 Wuwa Tappo 自己处理。

## 功能

- 后台静默监测 `WuwaTappo.exe` 主窗口；
- 字符数低于 1,000 时按需自动补充，达到门槛后停止输入；
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

已生成可直接打包发送给朋友的便携版本：

```text
dist/WuwaTappoAutoChest-Portable/
dist/WuwaTappoAutoChest-Portable.zip
```

朋友解压后启动并登录 Wuwa Tappo，再双击：

```text
WuwaTappoAutoChest.exe
```

控制面板支持启动后台、停止后台、查看状态和打开离线图鉴。关闭控制面板不会停止后台任务。

便携版使用原生 C# 后台进程，只依赖 Windows 自带的 .NET Framework 4.8 和 UI Automation，不需要 PowerShell 7，也不会再启动 PowerShell Worker。采用 x86 便携构建并加入按可见行渲染、UIA 属性缓存和自适应扫描后，实测 Worker 约 43.3 MB，Worker 加控制面板约 78–88 MB，空闲 CPU 约 0.7% 单核。必须发送完整 ZIP，不能只发送 EXE。详细说明见 [docs/portable.md](docs/portable.md)。

控制面板采用 Codex 风格深色界面，状态区和历史记录区为纯黑背景，指标卡和按钮使用响应式流式布局，支持窗口缩放和 DPI 缩放。最小推荐尺寸为 740 × 700。

## 自动化边界

以下内容有意不做：

- 不自动输入账号、密码、短信或验证码；
- 不绕过 Geetest、风控或登录过期；
- 不直接修改字符数、宝箱计时或存档；
- 不调用应用内部的“增加点击”调试事件；
- 不直接调用 `/api/frontend/chests/open`；
- 不导入或复制浏览器 Cookie、Authorization 或 Token；
- 不开启应用尚未渲染出的宝箱。

## 环境要求

- Windows 10 或 Windows 11 x64；
- PowerShell 7（`pwsh`，仅源码测试和重新构建需要；便携 EXE 不需要）；
- 已安装并至少能手动正常运行的 Wuwa Tappo；
- 已由用户手动完成登录；
- Wuwa Tappo 主窗口存在。默认不自动启动应用，以避免触发 UAC 或重复授权。

当前调查环境：

```text
Windows 11 专业版 10.0.26100
Wuwa Tappo 1.0.2 UAT CN
Tauri 2.10.3 + WebView2 155.0.4283.62
```

## 快速开始

1. 启动并登录在线版 Wuwa Tappo；
2. 打开 `dist/WuwaTappoAutoChest-Portable/WuwaTappoAutoChest.exe`；
3. 点击“启动后台”；
4. 控制面板显示运行状态、字符数、阶段、真实物品名和自动开箱历史；
5. 关闭控制面板不会停止后台，再次打开 EXE 可查看历史或停止任务。

命令行入口：

```powershell
.\dist\WuwaTappoAutoChest-Portable\WuwaTappoAutoChest.exe --start-background
.\dist\WuwaTappoAutoChest-Portable\WuwaTappoAutoChest.exe --stop-background
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
| `activity.enabled` | `true` | 是否自动累积字符数 |
| `activity.virtualKey` | `135` | 注入的 Windows 虚拟键，135 为 F24 |
| `activity.intervalMilliseconds` | `50` | 每键间隔；越低越快，但会增加系统输入负载 |
| `activity.burstSize` | `20` | 每批发送并随后检查宝箱的数量 |
| `activity.characterThreshold` | `1000` | 达到该字符数后暂停活动注入 |
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
- 默认仅在字符数低于 1,000 时注入；达到门槛后完全停止输入，只低频轮询宝箱，避免持续占用性能。
- 已验证一次真实自动开箱：`reward-btn` 被自动调用后消失，状态记录为 `confirmed`，证据为 `single_button_removed`。
- UI Automation 依赖应用当前 `.reward-btn` 和 `reward-anchor--left/right` 结构；应用升级后需重新验证。
- 真实物品名优先来自奖励图片 SHA-256。若 WebView2 命中旧缓存且没有写入新图片，同时资产 ID 也不在图鉴内，程序会明确显示“名称未提供”，不会猜测名称。
- 当前 UAT 主程序签名状态为 `HashMismatch`。用户已确认信任该文件夹，但生产分发前仍建议重新校验可信安装包。
- 未登录、Geetest、强制更新和维护公告需要人工处理。

## 项目结构

```text
config/                         配置示例与当前本机配置
docs/investigation.md           调查结论
docs/design.md                  设计方案
docs/test-results.md            实际测试记录
docs/atlas-gallery.md           离线全图鉴使用说明
docs/portable.md                便携版与 EXE 使用说明
gallery/index.html              已生成的全图鉴页面
gallery/images/                 899 张本地图鉴图片
dist/                           便携目录与 ZIP
portable/                       EXE 启动器源码
scripts/                        启动、停止、状态和工作进程入口
src/WuwaTappoAutoChest.psm1     核心自动化模块
tests/Run-Tests.ps1             自动化测试
tests/Run-AtlasGalleryTests.ps1 图鉴生成结果测试
tests/Test-NativeRewardLookup.ps1 在线奖励缓存与真实名称映射测试
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

<!-- Project marker: by phrolova2405 -->
