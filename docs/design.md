# Wuwa Tappo 自动字符数与开宝箱设计方案

## 1. 目标与边界

### 1.1 目标

后台静默监测 Wuwa Tappo 主窗口；无宝箱时通过应用正常接收的限速 `F24` 输入累积字符数和活跃时间；宝箱出现后按左、右槽位逐个开启，并记录可审计的本地状态。

### 1.2 不包含的行为

本程序不会：

- 直接修改字符数、宝箱计时或服务端数据；
- 调用应用内部的“增加点击”调试事件；
- 移动鼠标、切换前台窗口或输入文字；
- 直接调用内部 HTTP API；
- 读取、导出或保存登录凭据、Cookie、令牌；
- 绕过登录、Geetest、短信或风控；
- 修改应用文件、安装目录或 WebView 数据；
- 在结果未知时立即重复点击同一宝箱；
- 强制结束高权限的 `WuwaTappo.exe`。

“自动获取”包含两个阶段：

1. 无宝箱按钮时，以限速方式发送无文本含义的 `F24` 键；
2. 应用按其正常全局输入规则计算字符数和活跃时间，满足条件后渲染宝箱按钮，程序自动点击并确认。

## 2. 已确认的自动化依据

应用前端在满足以下条件时会渲染真实 HTML 按钮：

```text
button.reward-btn
```

按钮外层槽位类名为：

```text
.reward-anchor--left
.reward-anchor--right
```

UI Automation 能直接读取 WebView2 中的 HTML Button、ClassName、Enabled 状态和 InvokePattern。使用该语义接口可以保持应用原有行为：

- 登录状态检查；
- 应用自身以 1,000 字符数作为宝箱入口显示门槛，自动补足目标设为 10,000；
- 左右槽位检查；
- 单次开启业务锁；
- 服务端请求；
- 奖励动画与状态刷新；
- 防重复提交。

## 3. 总体架构

```text
scripts/
  Start-AutoChest.ps1
  Watch-AutoChest.ps1
  Stop-AutoChest.ps1
  Get-AutoChestStatus.ps1
src/
  WuwaTappoAutoChest.psm1
config/
  autochest.example.json
logs/
state/
tests/
  Run-Tests.ps1
```

### 3.1 启动器

`Start-AutoChest.ps1`：

- 校验配置和运行环境；
- 获取单实例互斥锁；
- 使用隐藏窗口启动后台工作进程；
- 等待工作进程写出首份状态；
- 默认不启动未运行的 Wuwa Tappo，避免触发 UAC 或反复弹出授权窗口；
- 允许 `-Foreground` 和 `-Once` 用于诊断。

### 3.2 工作进程

`Watch-AutoChest.ps1`：

- 加载模块和配置；
- 循环定位 Wuwa Tappo 主窗口；
- 扫描 `.reward-btn`；
- 检查状态文件、冷却时间和停止请求；
- 在条件满足时通过 UI Automation `InvokePattern` 点击；
- 等待按钮消失或奖励弹窗出现；
- 写出结构化日志、状态和幂等记录；
- 超时、达到最大运行时间或收到停止请求时安全退出。

活动累积器：

- 从 `pet-dock-content` 中的数字节点读取当前字符数；
- 使用 `SendInput` 批量调用，每 500 个按键合并为一次系统调用；
- 每次发送后重新读取计数，并检查是否已经出现 `.reward-btn`；
- 字符数达到 `characterThreshold` 后停止注入；
- 出现宝箱按钮、停止请求或窗口消失时立即暂停；
- 计数刷新仅用于状态记录，不因 UI 异步刷新误停活动；
- 只有 Windows 输入注入明确失败时才停用活动。

### 3.3 停止与状态查询

`Stop-AutoChest.ps1`：

- 写入停止请求；
- 等待工作进程自行退出；
- 超时后只结束本程序自己的后台工作进程 PID。

`Get-AutoChestStatus.ps1`：

- 输出工作进程是否存活；
- 输出最近扫描时间、宝箱按钮数量、当前状态和最近一次操作结果；
- 只读取本地状态文件，不接触应用数据。

## 4. 状态转换

```text
Stopped
  -> Starting
  -> SearchingApp
  -> WaitingForChest
  -> AccumulatingActivity
  -> CandidateDetected
  -> Submitting
  -> Confirming
  -> Cooldown
  -> WaitingForChest

任意状态 -> Blocked
Blocked: 未登录、窗口缺失、UI 结构异常、权限不足、连续失败等

任意状态 -> Stopping
Stopping -> Stopped
```

### 4.1 WaitingForChest

- 每 `pollIntervalSeconds` 扫描一次。
- 没有 `.reward-btn` 且活动功能启用时进入 `AccumulatingActivity`。
- 不移动鼠标、不切换窗口焦点。

### 4.2 AccumulatingActivity

- 从 `pet-dock-content` 定位字符数文本；
- 按 `intervalMilliseconds` 发送 `F24`；
- 每批最多发送 `burstSize` 次；
- 字符数达到 `characterThreshold` 时停止继续注入；
- 每批发送后读取计数并扫描宝箱按钮；
- 计数刷新仅用于状态记录，不因暂时不可读而停止；
- `DryRun` 和 `-DisableActivity` 不发送任何键。

### 4.3 CandidateDetected

候选条件：

- 找到 ClassName 包含 `reward-btn` 的 Button；
- 按钮 `IsEnabled=true`；
- 支持 InvokePattern；
- 对应左右槽位不处于冷却期；
- 当前没有待确认操作。

### 4.4 Submitting

在调用之前，先原子写入：

```text
operation_id
side
button_rect
detected_at
attempted_at
status=pending
```

如果进程在写入后崩溃，下次启动会看到 `pending`，先进入“未知结果”冷却，不会立刻重试。

### 4.5 Confirming

调用 `InvokePattern.Invoke()` 后：

- 每 `confirmationPollMilliseconds` 重新扫描；
- 成功迹象：对应 `reward-btn` 消失，或 `chest-reward-popup` 出现；
- 达到 `actionConfirmationTimeoutSeconds` 仍未确认时标记 `unknown`；
- `unknown` 状态下禁止重试同一侧，直到经过较长冷却并重新看到稳定的界面状态。

### 4.6 Cooldown

- 成功后为对应侧设置 `actionCooldownSeconds` 冷却；
- 结果未知时设置 `unknownOutcomeCooldownSeconds`；
- 处理下一槽位前等待 `actionSettleMilliseconds`，并确认旧奖励弹窗已经结束；
- 冷却只阻止本程序重复点击，不修改应用状态。

## 5. 左右槽位识别

识别顺序：

1. 从按钮向上遍历 UI Automation 父节点，查找 ClassName 中包含 `reward-anchor--left` 或 `reward-anchor--right` 的节点。
2. 若父节点不可识别，但只存在一个按钮，将其标记为 `unknown-single`，仍然可以处理。
3. 若同时存在两个按钮且无法从父节点识别，按屏幕横坐标排序；左侧为 `left`，右侧为 `right`。
4. 位置和类名完全无法匹配时不点击，记录 `UI_AMBIGUOUS`。

不依赖固定屏幕坐标；坐标仅用于排序和日志。

## 6. 重复操作防护

采用四层防护：

1. **单实例互斥锁**：同名 Local Mutex 和锁文件确保只有一个工作进程。
2. **应用内业务锁**：通过真实按钮触发，保留应用的 `Co` 单次开启锁。
3. **持久化 pending 记录**：点击前写入意图，崩溃后不会立即重放。
4. **左右独立冷却**：成功和未知结果分别设置冷却时间。

不会自动重试“结果未知”的开启。只有明确未发生提交的情况，例如按钮不可用、元素消失但 operation 未进入 Submitting，才可在下一轮重新扫描。

## 7. 配置管理

配置使用 JSON，示例：

```json
{
  "app": {
    "processName": "WuwaTappo",
    "windowTitle": "Wuwa Tappo",
    "launchIfMissing": false,
    "executablePath": "E:\\Program Files (x86)\\WuwaTappo\\WuwaTappo.exe"
  },
  "automation": {
    "pollIntervalSeconds": 1,
    "idlePollIntervalSeconds": 5,
    "confirmationPollMilliseconds": 250,
    "actionConfirmationTimeoutSeconds": 20,
    "actionCooldownSeconds": 3,
    "unknownOutcomeCooldownSeconds": 900,
    "actionSettleMilliseconds": 500,
    "maxOpensPerCycle": 2,
    "maxRuntimeMinutes": 0,
    "dryRun": false
  },
  "activity": {
    "enabled": true,
    "virtualKey": 135,
    "intervalMilliseconds": 0,
    "burstSize": 1000,
    "characterThreshold": 10000
  },
  "storage": {
    "stateDirectory": "state",
    "logDirectory": "logs"
  }
}
```

配置路径必须位于当前工程目录或用户明确指定的位置。相对路径相对于工程根目录解析。

## 8. 凭据管理

本方案不需要用户名、密码、验证码、Cookie 或 Token。

登录和验证码由用户在原应用中完成。脚本只观察 UI 状态。`.env.example` 仅提供可选的非敏感路径和日志级别覆盖，不包含真实密钥。

## 9. 日志与状态

### 9.1 日志

采用 JSON Lines：

```json
{"time":"2026-10-27T19:00:00+08:00","level":"INFO","event":"scan","process_id":1234,"button_count":1,"side":"left"}
```

日志字段不包含：

- Cookie 或 Authorization；
- 登录账号、手机号或 CUID 原值；
- 请求头；
- 页面完整快照；
- WebView 存储数据。

单文件默认最大 5 MiB，保留 5 个历史文件。

### 9.2 状态文件

`state/status.json` 保存：

- worker_pid；
- started_at；
- updated_at；
- phase；
- last_scan_at；
- reward_button_count；
- last_error；
- last_operation；
- side_cooldowns。

状态文件使用临时文件加原子替换，避免中断产生半份 JSON。

## 10. 超时、错误和恢复

| 情况 | 处理 |
|---|---|
| 应用未运行 | `Blocked`；默认不启动，等待用户启动 |
| 主窗口句柄为 0 | 短暂重试；持续失败进入 `Blocked` |
| UIA 找不到 WebView | 重试并记录结构变化 |
| 无 `.reward-btn` | 正常等待 |
| 按钮 disabled | 不点击，下一轮重新检测 |
| 不支持 InvokePattern | 不降级为坐标点击，记录结构不兼容 |
| 登录过期 | 页面不出宝箱按钮；状态提示人工登录，不尝试自动登录 |
| 网络失败 | 按钮通常保留；由于结果可能未知，进入确认超时和冷却 |
| 工作进程中断 | 下次读取 pending 记录，进入未知结果冷却 |
| 强制更新提示 | 不点击更新，不绕过；进入 `Blocked` |
| 达到最大运行时间 | 正常退出 |

## 11. 后台静默策略

- 启动器使用 `Start-Process -WindowStyle Hidden`；
- 工作进程使用 `-NonInteractive`；
- 不调用 `SetForegroundWindow`；
- 不移动鼠标；
- 只发送无文本含义的 `F24` 键按下和抬起；
- 活动输入默认不增加额外逐键等待；
- 每批最多发送 1000 个 `F24`，内部按 500 键分组并检测计数变化；
- 字符数达到 10,000 后停止活动注入，避免无意义地持续占用输入队列；
- 仅调用按钮自身的 `InvokePattern`；
- 控制台输出只写日志文件；
- 测试优先使用 `dryRun=true`。

## 12. 技术选型

最终便携实现选择 **原生 C# WinForms + .NET Framework UI Automation**：

- Windows 11 自带所需 UIAutomation 程序集；
- 便携包不需要 PowerShell 7 或额外运行时；
- 使用 x86 构建降低 WinForms 常驻内存，同时可通过跨进程 UI Automation 操作 64 位 Wuwa Tappo；
- 能直接读取 Tauri WebView2 的 HTML 语义控件；
- Worker 常驻内存低于旧 PowerShell Worker；
- 可以实现隐藏进程、PID、状态文件、原子 JSON 和单实例锁；
- 避免引入 Selenium、Playwright、浏览器驱动和版本适配。

不选择内部 HTTP API，因为它需要处理凭据、Cookie、请求签名和服务端风控，并且更容易造成重复提交。

主 Worker 不使用屏幕坐标点击、截图 OCR 或经验猜测。奖励图片只用于“缓存文件 SHA-256 与图鉴图片精确匹配”，不会根据相似度猜名称。独立 CDP Host 只在显式一次性入口中使用页面 DOM 计算出的相对坐标，不使用桌面屏幕固定坐标。

## 13. 测试方案

### 13.1 单元测试

不依赖真实账号或应用：

- 配置默认值和非法配置校验；
- 活动开关、虚拟键和最低间隔校验；
- 相对路径解析；
- 左右按钮映射；
- 单/双按钮场景；
- 冷却时间和 pending 恢复；
- 状态文件原子写入；
- 日志脱敏；
- 达到最大运行时间和停止请求。

测试使用自包含 PowerShell 断言脚本，不要求安装 Pester。

### 13.2 集成测试

在真实应用运行时：

- `-Once -DryRun`：定位主窗口和 WebView，只扫描 `.reward-btn`，断言不点击；
- 验证 `DryRun` 不发送任何活动键；
- 验证真实字符数节点可以读取；
- 先单键、再短批次验证字符数增长；
- 启动后台进程，等待 `status.json`；
- 执行状态查询；
- 执行停止请求，确认后台进程退出；
- 检查日志不含令牌、Cookie 或账号信息。

### 13.3 有宝箱时的验证

只有应用自身显示可开启按钮时执行。测试步骤：

1. 先只记录按钮数量、Enabled 和左右槽位；
2. 开启单次模式；
3. 点击一个按钮；
4. 等待按钮消失或奖励弹窗；
5. 验证状态文件记录 `confirmed`；
6. 确认没有在冷却内再次点击。

若当前测试时没有符合条件的宝箱，不得伪造或直接调用内部 API；该步骤记为“等待条件下一次验证”。

## 14. 验收标准

- 应用没有宝箱时不产生点击请求；
- 活动模式下字符数通过应用正常输入计数增长；
- 宝箱按钮出现后活动注入停止；
- 字符数不足时不产生点击调用；
- 可开启按钮出现时每次只提交一次；
- 确认完成后才允许处理另一个槽位；
- 后台运行无新窗口、无鼠标移动、无文字输入；
- 停止脚本可在配置超时内退出工作进程；
- 状态查询可以判断工作进程和最近结果；
- 自动开箱历史在 Worker 和控制面板重启后仍可读取；
- 能识别时显示真实物品名称，不能识别时明确显示“名称未提供”；
- 测试日志和状态文件不包含敏感信息。

## 15. 奖励名称与历史记录

在线版点击奖励按钮后，WebView2 会加载对应的奖励图片。构建便携包时为图鉴中的图片生成 SHA-256 索引；Worker 在点击前记录缓存快照，确认开箱后只检查新增或变化的缓存文件，并与该索引做精确匹配。

名称解析优先级：

1. UI Automation 中直接暴露的资产文件名；
2. WebView2 奖励图片缓存 SHA-256 精确匹配；
3. 本地 `bag_new_asset_ids` 新资产 ID 与图鉴映射；
4. 明确显示“新角色/新配件/新表情/重复收集物（名称未提供）”。

历史写入 `runtime/state/history.jsonl`，包含操作 ID、时间、槽位、物品名称、名称来源、资产 ID、确认证据和结果。该文件位于便携目录，控制面板只读取最近 100 条；文件变化时才重新加载。

## 16. 性能与渲染优化

- 字符数只查询 `ControlType.Text`，并缓存 10 秒；
- 宝箱按钮查询使用 UIA `CacheRequest` 批量读取属性和边界；
- 无宝箱时扫描间隔 5 秒，有候选按钮时 1 秒；
- 成功冷却 3 秒，结果未知仍保持 900 秒冷却；
- 状态文件只在内容变化或 15 秒心跳时写入；
- 控制面板最小化时暂停刷新，恢复窗口后立即刷新；
- 历史记录使用虚拟列表，只创建当前可见行；
- 圆角面板同步自身背景色，所有子标签使用实际卡片底色；
- 窗口标题栏、ListView 和滚动区域启用 Windows 深色主题；
- 顶层表格、流式布局和历史列表启用双缓冲，减少闪烁。

## 17. 一次性补次数

控制面板提供数量输入和开始、取消操作。UI 将请求原子写入 `runtime/state/refill-request.json`，格式包含请求 ID、期望增加量、状态、基准值、目标值、当前值和已发送数量。

Worker 处理规则：

1. 新请求为 `pending` 时，读取当前字符数并计算目标值；
2. 使用每 500 个 F24 合并的批量输入；
3. 每批后等待 UIA 刷新并校验字符数；
4. 如果 1.2 秒没有增长，对 `.pet-dock-content` 调用一次 UIA `InvokePattern` 重新激活页面；
5. 恢复后继续 F24；同一批内最多恢复两次；
6. 两次恢复后仍不增长，写入 `needs_focus` 并停止，不继续空转；
7. 达到目标后写入 `completed`，本次流程永久停止；
8. 取消请求写入 `cancelled`；
9. 请求状态会跨 Worker 重启恢复。

一次性模式完成后不会因开箱消耗自动重新启动；需要再次补次数时，由用户在控制面板重新提交。

### 17.1 UIA 激活动作

WebView2 页面中的 `.pet-dock-content` 同时暴露：

```text
InvokePattern
ScrollItemPattern
```

单次 `InvokePattern` 实测会通过应用自身的页面事件增加 1 个字符，不产生系统鼠标移动。连续快速调用会被页面合并，因此它只用于 F24 停止计数后的激活，不用于逐次刷量。

新的恢复流程：

```text
F24 批次 -> 1.2 秒无增长
  -> InvokePattern 一次
  -> 若计数恢复，继续 F24
  -> 仍无增长则最多再恢复一次
  -> 两次失败后停止
```

## 18. 独立管理员 CDP 一次性补次数

### 18.1 目的

F24 由 `SendInput` 成功返回，不代表 Wuwa Tappo 会一直处理该输入。实测长时间任务中，应用可能停止响应合成 F24；窗口重新获得一次真实激活后才短暂恢复。为保持用户明确要求的“不占用、不移动鼠标”，主 Worker 不会模拟桌面鼠标点击。

CDP Host 是独立显式入口，用于一次性大批量补足：

```text
WuwaTappoAutoChest-CdpHost.exe
```

它不是常驻进程，也不会由主 Worker 自动启动。

### 18.2 生命周期

```text
Stopped
  -> 校验主助手已停止
  -> 随机选择回环端口
  -> 重启 Wuwa Tappo 并设置 WebView2 调试参数
  -> 等待 /json 页面目标
  -> 连接 WebSocket 并等待 .pet-dock-content
  -> 分批派发页面输入并校验字符数
  -> 成功 / 取消 / 异常
  -> 关闭 CDP 并重启无调试端口的 Wuwa Tappo
  -> Stopped
```

正常退出、取消和异常均经过 `finally` 恢复路径。用户强制结束 Host 进程属于不可捕获的中断，文档明确要求人工关闭并重新打开 Wuwa Tappo。

### 18.3 CDP 连接边界

- Host 以 `requireAdministrator` 清单启动，只处理用户桌面上明确触发的 UAC；
- 每次运行随机分配空闲 TCP 端口；
- `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` 明确限制为 `127.0.0.1`；
- 只接受 WebSocket URL 主机为 `localhost` 或回环 IP，且端口与本次随机端口一致；
- 只选择 `tauri.localhost` 页面目标；
- 使用原生 `ClientWebSocket`，不依赖 Node、Playwright、Selenium 或浏览器驱动；
- 不读取 Cookie、Token、Authorization、账号资料或请求头。

### 18.4 页面输入

1. 查询 `.pet-dock-content`；
2. 等待字符数叶子节点完成渲染；
3. 使用 `getBoundingClientRect()` 计算宠物区域中心偏上的相对坐标；
4. 通过 `Input.dispatchMouseEvent` 发送按下和抬起事件；
5. 每 50 次页面点击作为一个流式小批次，避免一次创建数百个异步任务；
6. 批次完成后等待两个 `requestAnimationFrame`，再读取字符数；
7. 根据实际增长调整下一批大小，最大 500 次；
8. 连续两轮没有增长时停止并报告兼容性失败。

CDP 事件直接进入该 WebView 页面，不产生 Windows 系统级鼠标移动或点击。

### 18.5 超时与恢复

| 情况 | 处理 |
|---|---|
| 主助手仍在运行 | 拒绝启动并提示先停止 |
| Wuwa Tappo 无法结束 | 报告具体 PID，不继续连接 |
| 30 秒内 CDP 未就绪 | 关闭调试模式并重启应用 |
| 页面 30 秒内没有字符数 | 关闭调试模式并重启应用 |
| 单批 CDP 响应超过 15 秒 | 终止本次流程并恢复 |
| 字符数不增长 | 降低批次重试一次，仍不增长则失败 |
| 用户点击取消 | 中止后续点击、关闭连接并恢复 |
| `finally` 恢复失败 | 保留明确错误，提示人工重启应用 |

### 18.6 测试策略

可自动验证：

- CDP Host C# 编译；
- PE 架构为 x86、GUI 子系统；
- 嵌入清单为 `requireAdministrator`，且包含 DPI 感知；
- 便携目录包含 Host 和说明；
- 日志与状态目录初始化；
- 源码和发布包隐私扫描。

需要目标桌面人工验证：

- UAC 接受后 WebView2 是否开放随机回环调试端口；
- 当前版本是否接受 CDP `Input.dispatchMouseEvent`；
- 字符数是否按页面点击增长；
- 完成和取消后是否恢复普通模式；
- Wuwa Tappo 更新后 DOM 或输入策略是否变化。

未完成上述目标桌面验证前，不得宣称 CDP 运行时路径已经通过。
