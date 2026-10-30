# Wuwa Tappo 自动字符数与开宝箱测试记录

- 测试日期：2026-10-27
- 系统：Windows 11 专业版，build 26100，x64
- 应用：Wuwa Tappo 1.0.1 UAT CN
- PowerShell：7.x
- 测试方式：后台隐藏进程、UI Automation 扫描、F24 正常输入计数、限速短批次测试

## 1. 测试结果汇总

| 测试项 | 结果 | 证据 |
|---|---|---|
| 模块导入与函数导出 | 通过 | 模块可正常导入 |
| 配置加载与路径解析 | 通过 | 单元测试 |
| 单按钮槽位识别 | 通过 | 返回 `single` |
| 左右槽位父节点识别 | 通过 | 分别返回 `left`、`right` |
| 双按钮未知槽位坐标回退 | 通过 | 左侧按钮优先映射为 `left` |
| pending 阻止并发操作 | 通过 | 返回 `pending_operation` |
| 未知结果冷却 | 通过 | 冷却内拒绝，冷却后恢复 |
| pending 崩溃恢复 | 通过 | 恢复为 `unknown` 并清理 pending |
| 状态文件原子读写 | 通过 | 写入后可读取 |
| 结构化日志 | 通过 | JSONL 可读取且不含敏感字段 |
| 活动配置加载 | 通过 | 虚拟键、间隔和批次参数有效 |
| 真实应用窗口识别 | 通过 | PID 26420，窗口句柄 263988 |
| 真实 `.reward-btn` 只读扫描 | 通过 | 当时检测到 0 个 |
| 真实字符数节点读取 | 通过 | UI Automation 可读取 `pet-dock-content` 数字 |
| F24 单次输入计数 | 通过 | 计数 `70 → 71` |
| 模块短批次活动 | 通过 | 发送 2 次，返回 `pulse_complete` |
| 后台隐藏启动 | 通过 | 生成独立隐藏 `pwsh` 工作进程 |
| 后台状态查询 | 通过 | `running=true`，PID 与状态一致 |
| 后台自动字符累积 | 通过 | 发送 5 个活动键，计数 `78 → 92` |
| 计数异步刷新修正 | 通过 | 改为完整批次比较，20 秒计数 `219 → 276` 且活动未误停 |
| 最快输入吞吐 | 通过 | `0 ms` 额外等待，实测约 `3.27 ms/键` |
| 真实自动开宝箱 | 通过 | 自动点击按钮，按钮消失，记录 `confirmed` |
| 达标后停止输入 | 通过 | 字符数 69,761，活动键发送数保持 0 |
| 空闲 CPU 占用 | 通过 | 20 秒采样约一个核心的 0.23% |
| 优雅停止 | 通过 | `graceful=true`，最终状态 `stopped` |
| 无宝箱非 DryRun 单次运行 | 通过 | 0 个按钮，无操作状态文件，无点击 |

自动测试汇总：

```text
SUMMARY passed=27 failed=0
```

## 2. 实际执行命令与结果

### 2.1 单元测试和真实只读集成

```powershell
pwsh -NoProfile -File .\tests\Run-Tests.ps1 -Integration
```

结果：

```text
SUMMARY passed=27 failed=0
INFO integration_reward_button_count=0
INFO integration_character_count=69760
INFO integration_app_process_id=28952
INFO integration_window_handle=329682
```

该测试没有点击 `.reward-btn`。

### 2.2 单次 DryRun

```powershell
pwsh -NoProfile -File .\scripts\Watch-AutoChest.ps1 `
  -ConfigPath .\config\autochest.json `
  -Once `
  -DryRun
```

结果：

```text
app_found=true
app_process_id=26420
window_handle=263988
reward_button_count=0
dry_run=true
```

### 2.3 后台启动、状态与停止

执行 `Start-AutoChest.ps1 -DryRun` 后：

```text
started=true
running=true
dry_run=true
phase=waiting_for_chest
reward_button_count=0
app_found=true
```

执行 `Stop-AutoChest.ps1` 后：

```text
stopped=true
graceful=true
running_after_stop=false
final_phase=stopped
```

### 2.4 无宝箱时非 DryRun 安全测试

执行一次正常模式单次扫描：

```powershell
pwsh -NoProfile -File .\scripts\Watch-AutoChest.ps1 `
  -ConfigPath .\config\autochest.json `
  -Once
```

结果：

```text
phase=stopped
reward_button_count=0
app_found=true
last_action=null
operation_state_existed_before=false
operation_state_exists_after=false
worker_pid_file_exists=false
```

这说明在没有宝箱按钮时，正常模式也没有写入开启操作记录。

### 2.5 默认正常模式后台生命周期

使用 `automation.dryRun=false` 的当前配置执行后台启动：

```text
started=true
running=true
dry_run=false
phase=waiting_for_chest
reward_button_count=0
last_action=null
operation_state_before_stop=false
```

随后执行停止：

```text
stopped=true
graceful=true
running_after_stop=false
final_phase=stopped
reward_button_count=0
operation_state_after_stop=false
```

这验证了默认正常模式在无宝箱时保持零操作，并且后台进程可以正常收尾。

### 2.6 隐藏窗口检查

DryRun 后台工作进程运行期间：

```text
process_name=pwsh
main_window_handle=0
main_window_title=<empty>
responding=true
```

随后通过正常停止请求退出，`stopped=true`、`graceful=true`。这验证了工作进程没有可见主窗口。

### 2.7 自动累积字符数

单次 F24 测试：

```text
count_before=70
count_after=71
increment=1
```

模块短批次测试：

```text
presses_sent=2
reason=pulse_complete
count_before=75
count_after=78
```

后台工作进程测试：

```text
worker_main_window_handle=0
activity_enabled=true
activity_keys_sent=5
activity_last_result=pulse_complete
count_before=78
count_after=92
reward_button_count=0
stopped=true
graceful=true
```

这验证了字符数自动累积已接入真实工作的隐藏后台进程。

### 2.8 性能优化后的按需模式

持续高速输入会带来不必要的负载，因此最终改为：

```text
activity.intervalMilliseconds=50
activity.burstSize=20
activity.characterThreshold=1000
```

当字符数已经超过门槛时，不再发送活动键，只按空闲间隔检查宝箱：

```text
phase=waiting_for_chest
character_count=69758
activity_enabled=true
activity_keys_sent=0
activity_last_result=character_threshold_reached
```

20 秒稳定采样：

```text
character_count=69761
activity_keys_before=0
activity_keys_after=0
worker_cpu_seconds=0.047
worker_cpu_percent_one_core=0.23%
```

这消除了“字符数已经充足但仍持续注入输入”的性能浪费。

### 2.9 活动误停问题与修复

首次连续测试中，前 15 次输入已使计数从 `142` 增长到 `163`，但逐键检查受到 UI 异步/批量刷新影响，连续 3 次瞬时未变化后错误停用了活动。

修复方式：

- 不在单个按键后判定永久停用；
- 每个完整批次比较一次起始和结束计数；
- 计数刷新不再决定是否停用活动；
- 只有 Windows 输入注入明确失败时才停用；
- 每个批次结束后检查宝箱按钮并停止后续批次。

修复后复测：

```text
phase=waiting_for_chest
activity_enabled=true
activity_last_result=pulse_complete
count_before=219
count_after=276
reward_button_count=0
```

后台工作进程保持运行，字符数继续增长。

### 2.10 最快模式测试记录

用户要求按本机最快速度运行后，移除了 PowerShell 逐键等待，改为 C# `SendInput` 批次：

```text
configured_extra_interval=0 ms
presses_sent=10
elapsed=32.7 ms
effective_interval=3.27 ms/键
character_count_before=591
character_count_after=601
observed_increment=10
```

该 0 ms 模式只作为上限测试保留在记录中；由于持续运行性能过高，最终配置已改为按需补足模式。实际吞吐仍由 Windows 输入队列和 Wuwa Tappo 的全局键盘钩子决定。

## 3. 真实开箱验证

字符数快速累积后，应用实际渲染出一个可开启按钮：

```text
button_count=1
side=single
class_name=reward-btn
enabled=true
offscreen=false
rect=2147,1194,62,44
```

后台程序自动执行了以下流程：

```text
19:05:52.382 event=chest_open_submitting
19:05:54.223 event=chest_open_confirmed
status=confirmed
evidence=single_button_removed
```

开箱后字符数一度显示 `1112`，随后应用服务端状态刷新为 `607`，证明点击走过了应用真实业务请求与状态更新，而不是只修改本地显示。

## 4. 已知限制

- 应用升级可能改变 UIA 树；
- 结果确认依赖按钮消失或奖励弹窗；
- 不通过内部 API 补充确认，以避免读取凭据和绕过应用行为；
- 字符数累积依赖应用接受合成 F24 输入；若应用升级后停止计数，程序会自动停用活动；
- 自动累积不会直接增加宝箱计时或宝箱数量，宝箱仍由应用按正常规则生成。

## 5. 原生低占用便携版

便携版后台已从 PowerShell Worker 改为原生 C#：

| 项目 | 实测 |
|---|---:|
| PowerShell Worker 工作集 | 约 127.8 MB |
| 原生 C# Worker 工作集（x86 优化版） | 约 43.3 MB |
| Worker + 图形控制面板（x86 优化版） | 约 78–88 MB |
| 原生 Worker 空闲 CPU | 约 0.16%–0.21% 单核 / 0.01% 全机 |

原生 Worker 还通过了以下实际验证：

- 便携 EXE 启动和停止返回码均为 0；
- 后台只运行一个 Worker；
- 字符数低于测试门槛时发送了 60 个活动键；
- 字符数达到门槛后活动键发送数保持 0；
- 图形控制面板可正常打开并显示状态；
- 关闭控制面板后 Worker 继续运行。

## 6. Codex 风格响应式界面

新控制面板使用动态布局并通过 UI Automation 边界检查：

| 窗口尺寸 | 结果 |
|---|---|
| 720 × 620 | 指标卡 3+3 两行，按钮自动换行，越界控件 0 |
| 1200 × 720 | 指标卡 5+1 两行，按钮单行，越界控件 0 |

界面同时启用了 DPI 自动缩放、可调整窗口大小、圆角按钮、指标卡、状态指示点和深色 Codex 风格配色。

## 7. 2026-10-28 黑色状态栏、中文界面与持久化历史

本轮只对在线版 `WuwaTappo.exe` 进行验证，未把离线版目录作为运行目标。

### 7.1 自动测试

| 测试 | 实际结果 |
|---|---|
| PowerShell 核心回归测试 | `24/24` 通过 |
| 图鉴完整性测试 | `15/15` 通过 |
| 原生奖励资产 ID 解析 | 通过，合成值 `2928,1459` |
| 在线版 `bag_new_asset_ids` 读取 | 通过，读取到 `2160,1494` |
| 图鉴图片哈希索引 | `899` 项 |
| 在线图片缓存精确匹配 | 通过，`f_000380 → 露帕・开心` |
| Worker 内部资产 ID 索引 | `899` 项 |
| Worker 内部图片名称索引 | `899` 项 |

测试命令：

```powershell
pwsh -NoProfile -File .\tests\Run-Tests.ps1
pwsh -NoProfile -File .\tests\Run-AtlasGalleryTests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tests\Test-NativeRewardLookup.ps1
```

### 7.2 界面与历史

UI Automation 实测：

- 状态区和无障碍名称包含“状态消息”；
- 指标标题包含“工作进程、运行阶段、字符数、自动计数、待开宝箱、最近开箱”；
- 历史区域包含“自动开箱历史”；
- `740 × 700` 最小窗口下越界可见控件为 `0`；
- 关闭控制面板后 Worker 保持运行；
- 重新打开控制面板后历史行仍可读取。

历史持久化文件：

```text
runtime/state/history.jsonl
```

### 7.3 在线版真实开箱

2026-10-28 12:41:32，Worker 实际自动开启了一次宝箱：

```text
target_pid=20224
side=single
status=confirmed
evidence=single_button_removed
item_name=新配件（名称未提供）
item_source=popup_label
```

该宝箱的奖励图片已经命中旧 WebView2 缓存，没有产生新的缓存文件；对应资产 ID 也不在当前官方图鉴中。因此程序明确显示“名称未提供”，没有用错误名称冒充成功。

这一次记录已写入最终便携目录的本地历史，并已通过控制面板重启验证仍可读取。用于发给朋友的 ZIP 仍保持空历史。

### 7.4 最终便携包

```text
ZIP 文件数：913
状态文件：0
日志文件：0
ZIP 内 executablePath：空
SHA-256：见同目录 WuwaTappoAutoChest-Portable.zip.sha256
```

在线版运行采样（空闲字符数已达标，50 秒内每 10 秒采样一次）：

```text
Worker：约 51.9–54.6 MB
控制面板：约 40.4–40.7 MB
合计：约 92.2–95.3 MB
目标进程：在线版 PID 20224
```

启用 `app.processId` 快路径后避免重复通过 WMI 辨认同名进程；控制面板改为按状态文件签名刷新，避免每 1.5 秒重复解析未变化的 JSON。连续采样最高合计工作集为 `95.3 MB`，低于用户要求的 `100 MB`。

## 8. 2026-10-28 性能与渲染优化

### 8.1 性能改动

- Worker 从 x64 改为 x86 便携构建，仍能通过 UI Automation 控制 64 位在线版 Wuwa Tappo。
- 字符数扫描只查询 Text 控件，并缓存 10 秒。
- 宝箱按钮扫描使用 UIA CacheRequest 批量读取属性。
- 无宝箱扫描间隔为 5 秒；发现候选按钮后为 1 秒。
- 状态文件只在内容变化或 15 秒心跳时原子写入。
- 控制面板最小化时停止定时刷新。
- 历史列表改为 VirtualMode，只渲染可见行。

### 8.2 实测结果

| 项目 | 优化前 | 优化后 |
|---|---:|---:|
| Worker 空闲 CPU（单核） | 约 1.64% | 约 0.70% |
| 控制面板 CPU（单核） | 约 0.08% | 约 0.08% |
| Worker 工作集 | 约 53–57 MB | 约 43.3 MB |
| Worker + 控制面板 | 可能超过 100 MB | 约 78–88 MB |

45 秒连续采样：

```text
SAMPLE=1 WORKER_MB=43.3 UI_MB=44.1 TOTAL_MB=87.4
SAMPLE=2 WORKER_MB=43.3 UI_MB=44.1 TOTAL_MB=87.4
SAMPLE=3 WORKER_MB=43.3 UI_MB=44.1 TOTAL_MB=87.4
```

### 8.3 渲染修复

- 圆角卡片的 `FillColor` 同步到实际背景，消除透明标签后透出错误底色的问题。
- 标题、Mark、指标、状态消息和历史标题显式使用正确底色。
- 顶层表格、流式布局、按钮和历史列表启用双缓冲。
- 窗口标题栏、ListView 和滚动区域应用 Windows 深色主题。
- 历史表改为虚拟渲染，减少长历史时的重绘。
- 740 × 700 最小窗口布局保持可用。

使用 `PrintWindow` 抓取完整窗口并去除遮挡影响后，状态栏和滚动区域的系统默认灰色采样点为 `0`；窗口像素主要为预期深色背景、文字和绿色状态元素。

## 9. 2026-10-30 快速批量补次数

Wuwa Tappo 更新至 1.0.3 后，奖励弹窗可能返回空值。已在名称解析器中加入空值保护，并加入 `null_popup_fallback` 回归测试，避免开箱后 Worker 崩溃。

为降低频繁 `SendInput` 调用和文件检查开销，活动输入改为：

- 每 100 个 F24 合并为一次 `SendInput`；
- 每批最多 500 个 F24；
- 每批完成后立即刷新字符数缓存；
- 一轮输入后字符数没有增长时停止活动，避免无限空转。

实际测试：

```text
起始字符数：13782
目标字符数：15000
实际字符数：15282
活动键：1500
耗时：约 3.9 秒
补足阶段 CPU：约 10.5% 单核
达标后 CPU：约 0.62% 单核
Worker 内存：约 41 MB
```

该方案不移动或占用鼠标，只使用 F24 键盘输入路径。

## 10. 一次性补次数模块

控制面板新增“一次性补次数”区域，支持自定义本次增加量、开始、取消和进度显示。请求持久化在 `runtime/state/refill-request.json`。

端到端测试：

```text
请求增加：1000
起始字符数：17124
目标字符数：18124
最终字符数：18124
已发送：1000
结果：completed
```

取消测试：

```text
请求增加：100000
取消后状态：cancelled
```

更快的 500 键内部分组测试：

```text
请求增加：5000
耗时：约 17.3 秒
已发送：5038–5015
最终字符数：精确达到目标
结果：completed
```

继续增大内部批量后耗时不再明显改善，当前瓶颈是 Wuwa Tappo 自身处理 F24 的速度。一次性补足完成后不会自动续补，Worker 继续以低占用模式等待宝箱。

## 11. 2026-10-30 独立 CDP Host 构建与静态验收

本轮新增：

```text
portable/CdpHost.cs
portable/cdp-host.manifest
tests/Test-CdpHostPackage.ps1
```

### 11.1 便携构建

实际执行：

```powershell
pwsh -NoLogo -NoProfile -NonInteractive `
  -File .\tools\Build-Portable.ps1 `
  -OutputDirectory .\dist\WuwaTappoAutoChest-CdpTest `
  -Force -SkipZip
```

结果：

```text
主程序：WuwaTappoAutoChest.exe
CDP Host：WuwaTappoAutoChest-CdpHost.exe
主程序字节数：80,384
CDP Host 字节数：33,792
编译器退出码：0
编译警告：0
```

### 11.2 CDP Host 包结构验收

实际执行：

```powershell
pwsh -NoLogo -NoProfile -NonInteractive `
  -File .\tests\Test-CdpHostPackage.ps1 `
  -PackageDirectory .\dist\WuwaTappoAutoChest-CdpTest `
  -ExpectEmptyState
```

结果：

```text
SUMMARY passed=17 failed=0
```

已验证：

- 主程序和 CDP Host 存在；
- CDP Host 为 x86 PE32；
- CDP Host 为 Windows GUI 子系统；
- 嵌入清单包含 `requireAdministrator`；
- 嵌入清单不包含 `asInvoker`；
- 嵌入清单包含 `PerMonitorV2`；
- 便携配置没有写入本机 `WuwaTappo.exe` 路径；
- 分享包初始状态目录和日志目录为空。

### 11.3 隐藏窗口冒烟测试

为避免触发 UAC 和真实 CDP 操作，使用同一份 `CdpHost.cs` 临时编译了一个不带管理员清单的测试副本，通过：

```powershell
Start-Process -FilePath <SmokeExe> -WindowStyle Hidden -PassThru
```

在后台静默启动。2 秒后实际结果：

```text
Running=True
ProcessId=6812
WorkingSetMB=25.1
MainWindowHandle=0
```

进程保持运行，说明 WinForms 界面、依赖和初始化路径没有启动崩溃；测试结束后只终止了这个临时测试进程，并删除了临时目录。该测试没有启动 CDP、没有重启 Wuwa Tappo，也没有发送任何输入。

### 11.4 既有功能回归

实际执行：

```text
tests/Run-Tests.ps1
SUMMARY passed=24 failed=0

tests/Run-AtlasGalleryTests.ps1
SUMMARY passed=15 failed=0
```

这些测试没有触发真实开箱、真实补次数或 CDP 输入。

### 11.5 CDP 运行时验收状态

当前未宣称 CDP 真实点击通过。原因是 CDP Host 使用 `requireAdministrator`，需要用户在交互桌面接受 UAC；当前自动化会话不能代替用户确认，而且此前直接带远程调试参数启动 Wuwa Tappo 的尝试被当前执行策略拒绝。

仍未执行：

- 用户桌面 UAC 后的随机回环端口连接；
- Wuwa Tappo 1.0.3 对 `Input.dispatchMouseEvent` 的真实响应；
- 真实 1,000 次、100,000 次字符增长；
- 真实取消和异常后的普通模式恢复；
- CDP Host 长任务资源占用。

首次人工验证建议先输入 `1000`。确认界面显示的起始值到目标值准确增长，并确认完成后 Wuwa Tappo 被重启且调试端口关闭，再测试较大数量。

## 12. 2026-10-31 CDP Host 启动前内存耗尽修复

### 12.1 实际故障

用户运行 CDP Host 后收到：

```text
System.OutOfMemoryException
```

故障时实测旧 Host 进程：

```text
Working Set：1606.2 MB
Private Bytes：1597.0 MB
Virtual Bytes：1882.5 MB
Handles：352
Threads：11
TCP 连接：0
```

`runtime/logs/cdp-host.jsonl` 只有 `run_failed`，没有 `run_started`。这证明异常发生在重启 Wuwa Tappo 和连接 CDP 之前，问题位于 `FindWuwaExecutable()` 的祖先路径遍历，而不是 WebSocket 点击批次。

### 12.2 根因

原实现先去掉路径末尾分隔符，再调用 `Directory.GetParent()`。当输入为：

```text
E:\
```

去掉分隔符后变成盘符相对路径：

```text
E:
```

.NET Framework 在本次环境中把 `E:` 解析为 E 盘当前目录的父目录，而不是根目录。遍历因此从根目录重新进入已有的父目录链：

```text
E:\
  -> E:\Program Files (x86)\WuwaTappo
  -> E:\Program Files (x86)
  -> E:\Program Files
  -> E:\
  -> 循环
```

候选 EXE 路径不断加入 `List<string>`，x86 Host 到达约 2 GB 地址空间限制后抛出 `OutOfMemoryException`。

### 12.3 修复

- 先用 `Path.GetPathRoot()` 识别盘符根目录和 UNC 根目录；
- 根目录直接返回空父路径，不再把 `E:\` 转为 `E:`；
- 祖先遍历增加 32 层安全上限；
- 候选路径使用不区分大小写的 `HashSet<string>` 去重；
- 超过安全深度时报告明确错误，不再继续分配内存；
- 异常日志保留完整异常类型和堆栈。

### 12.4 回归测试

新增：

```text
tests/CdpHostSelfTest.cs
tests/Test-CdpHostPathWalk.ps1
```

实际执行：

```powershell
pwsh -NoProfile -File .\tests\Test-CdpHostPathWalk.ps1
```

结果：

```text
PASS 根目录终止 C:\
PASS 根目录终止 C:\
PASS 根目录终止 E:\
PASS 便携目录祖先遍历终止 depth=5
SUMMARY passed=4 failed=0
```

修复后便携构建：

```text
CDP Host 编译：成功，0 警告
CDP Host 字节数：34,816
便携包结构测试：17/17 通过
核心回归测试：24/24 通过
分享 ZIP 状态文件：0
分享 ZIP 日志文件：0
```

故障遗留的 1.6 GB 旧 Host 进程 PID 10212 已在确认无网络连接、无活动任务后结束，进程不再运行。主自动助手未受影响。

## 13. 2026-10-31 CDP 端口未开放与 UIA 回退

### 13.1 CDP 实测失败

修复路径遍历后再次运行 CDP Host：

```text
run_started: amount=100000; port=52424
run_failed: WebView2 CDP 未就绪。操作超时
```

时间线：

```text
00:03:55 启动调试模式 Wuwa Tappo
00:04:27 30 秒超时并恢复普通模式
```

恢复后的 Wuwa Tappo PID 为 25692，启动时间 `00:04:27`，没有监听端口 52424。该结果证明当前 Wuwa Tappo 1.0.3 / WebView2 156 没有将远程调试参数暴露为可用 CDP 端口。

CDP Host 已增加两项诊断防护：

- 重启前捕获 Wuwa WebView2 子进程树，等待旧浏览器进程真正退出；
- 启动后检查新 WebView2 子进程命令及回环端口，8 秒内没有调试能力时提前报明确错误。

但当前版本仍应把 CDP 视为实验入口，不作为日常补次数主路径。

### 13.2 UIA InvokePattern 发现

只读扫描当前 `.pet-dock-content`：

```text
ControlType: Group
KeyboardFocusable: False
Patterns:
  InvokePattern
  ScrollItemPattern
```

单次 `InvokePattern.Invoke()` 实测：

```text
字符数：177169 -> 177170
增量：1
系统鼠标移动：无
```

连续 10 次无等待调用：

```text
请求：10
实际增长：0
```

说明页面会合并快速连续的语义调用，不能把它当作高频点击接口。

### 13.3 F24 与 UIA 混合验证

在同一个应用进程中：

```text
F24 10 次 -> 字符数增加 10
UIA InvokePattern 1 次 -> 页面保持可操作
F24 再次 10 次 -> 字符数增加 10
```

1000 个 F24 批量实测：

```text
Before: 175207
After: 176207
Delta: 1000
发送调用耗时：约 2.7 秒
系统鼠标事件：无
```

因此主 Worker 的补次数继续使用 F24 作为高速路径，UIA `InvokePattern` 只用于计数停止后的页面激活。

### 13.4 新恢复策略

`RunActivityBurst` 已修改：

- 单批无增长等待时间从 400 ms 调整为 1200 ms；
- 无增长时查找 `.pet-dock-content` 并调用一次 `InvokePattern`；
- 恢复计数后继续 F24；
- 同一批最多执行两次 UIA 恢复；
- 两次仍无增长才写入 `needs_focus`；
- 记录 `activity_uia_recovery` 和 `activity_uia_recovery_failed` 日志。

当前已通过 C# 编译和既有回归测试。完整的 100000 次长任务已经通过；“页面停止接受 F24 后由 UIA 成功恢复”的真实场景仍需下一次条件出现时继续验收。

### 13.5 100000 次端到端长任务

使用 Hybrid 便携版 Worker 提交一次性增加 100000：

```text
起始字符数：177208
目标字符数：277208
最终字符数：277230
已发送 F24：100267
最终状态：completed
耗时：约 5 分 22 秒
Worker 内存：42.4–43.0 MB
```

过程中：

```text
activity_uia_recovery：0
activity_uia_recovery_failed：0
input_not_counted：0
needs_focus：未出现
```

本次 F24 从约 4 万到 10 万完整跑完，没有复现此前“无干预只完成一半”的问题。22 个字符的最终偏差来自运行期间 UI 数值刷新与批次边界，不是宝箱重复提交。

由于本次没有出现 F24 停摆，UIA 恢复分支没有在真实长任务中触发；该分支的单次 `InvokePattern` 已单独验证可增加 1 个字符。下一次出现 `input_not_counted` 时应检查 `activity_uia_recovery` 日志。
