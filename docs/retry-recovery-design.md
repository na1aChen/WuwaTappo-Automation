# Wuwa Tappo 原生补数 50 万级稳定性与恢复方案

- 制定日期：2026-11-14
- 目标版本：Wuwa Tappo 1.0.4 / WebView2 157.0.4341.46
- 适用程序：`WuwaTappoAutoChest-CdpHost.exe`
- 目标：单次补足 500,000 次达到可重复的 99% 级完成率，并在 1,000,000 次以内保持稳定
- 原则：允许安全重试，不允许结果未知时盲目重复增加
- 实施状态：已实现；500,000 与 1,000,000 次最终版真实补数均通过

> 这里的“99%”是工程验收目标。实现完成后，至少要通过 1,000、5,000、50,000、100,000、500,000 的逐级真实测试；一次成功不能证明长期统计概率，测试记录必须按实际结果写明。

## 0. 结论与实施边界

推荐采用“预检重基准、保存后只读复核、结果不明即停止”的混合恢复机制：

```text
store-state-changed
  -> 重新读取页面状态
  -> 重算剩余量
  -> 最多连续重试 5 次

post-save-state-not-confirmed
  -> 只读轮询，不重复增加
  -> 达到期望值则确认成功
  -> 总窗口仍不确定则停止

increment-not-confirmed
  -> 重新读取实际状态
  -> 能精确计算剩余量才继续
  -> 否则停止并保留审计记录
```

本次实施只改原生计数 Host 及配套测试和文档。保持以下边界不变：

- 不覆盖现有 `dist/WuwaTappoAutoChest-Portable-Native`；
- 不修改自动开箱、UIA、BetterGI 或系统键鼠路径；
- 不新增服务器 HTTP 写请求；
- 不读取账号原值、Cookie、Token 或 Authorization；
- 不自动重放“可能已经写入但无法确认”的批次。

### 0.1 50 万和 100 万级的发布门槛

“50 万级 99% 达成率”属于工程可靠性目标，不能由一次成功直接证明。发布前至少满足：

1. 纯逻辑故障注入覆盖本方案列出的全部恢复分支；
2. 1,000、5,000、50,000、100,000 的逐级真实测试通过；
3. 连续 3 次 500,000 的真实任务均精确达到目标；
4. 每次 500,000 任务中没有“结果不明却继续增加”的记录；
5. 3 次任务结束后都没有遗留 Host、调试端口或异常普通模式；
6. 500,000 连续通过后，再执行一次 1,000,000 的真实稳定性测试；
7. 1,000,000 测试期间内存无持续线性增长，结束时仍在可接受范围。

连续 3 次成功仍只是最低发布门槛，不表述为统计证明。若任何一次出现未解释的重复增量，停止后续放大测试并重新审查恢复协议。

截至 2026-11-14，离线测试已通过；早于最终补丁的 `RetryTest` 构建完成过一次精确 500,000 次任务。该次正常路径成功不能替代最终 Recovery 包的冲突注入与多次真实长任务测试，当前不得标记为已达到统计意义上的 99%。

## 1. 现有证据

2026-11-14 的同类长任务记录：

| 请求量 | 实际确认量 | 结果 |
|---:|---:|---|
| 100,000 | 59,000 | `post-save-state-not-confirmed` |
| 100,000 | 100,000 | 成功 |
| 500,000 | 295,000 | `store-state-changed` |

日志还表明：

- 页面约每 30 秒重置一次 `localClickCount`；
- `available` 和 `cumulative` 会因页面自身事件出现少量额外增长；
- 当前 Host 要求四项计数与上一批完全相等，任何异步同步都可能被误判为冲突；
- 保存后的固定 500 ms 验证窗口可能早于页面最终回写。

2026-11-14 13:06 的 500,000 次记录进一步定位了失败点：

```text
13:06:03  local=56017
13:06:04  local=0       available/cumulative 继续正常增长
13:06:08  store-state-changed
```

这次失败紧跟在页面主动归零 `localClickCount` 后。它证明当前 `store-state-changed` 至少有一部分只是本地批次计数器发生了周期性归并；Host 仍持有上一批的 `local` 期望值，因此在任何写入前误判为冲突。

因此当前问题主要是页面异步状态变化与 Host 严格校验之间的竞争，不是次数硬上限。最优先修复点是：预检冲突后重新读取状态，而不是终止整个长任务。

## 2. 设计目标

### 2.1 功能目标

1. 1,000 次、5,000 次、50,000 次回归继续通过；
2. 500,000 次任务以精确目标完成为验收标准；
3. 500,000 次通过后再测试 1,000,000 次；
4. 发生可恢复的状态竞争时，不要求用户重新运行整个任务；
5. 所有重试必须可解释、可记录、可审计；
6. 不得因重试导致同一批次被重复计算。

### 2.2 稳定性判断口径

补数流程按每批最多 1,000 次执行。500,000 次任务约包含 500 个写入批次。只要批次级恢复可靠，单批偶发竞争就应被吸收在任务内部，而不是导致整个任务完成度停在中途。

验收优先看以下结果：

1. 单个 500,000 次任务最终 `completed`；
2. `available` 与 `cumulative` 都至少增加 500,000；
3. 所有 `store-state-changed` 都在可证明“尚未写入”的前提下恢复；
4. 所有保存后不确定都先做只读复核，不能直接重复整批；
5. 恢复后没有额外、无法解释的重复增加。

### 2.3 统计口径

“500,000 次达到 99%”首先作为工程可靠性目标，而不是单次实验即可证明的统计结论。验收至少要求：

- 连续正常批次尽量不附加等待；
- 可恢复竞争自动恢复；
- 单个 500,000 次任务实际完成；
- 日志中没有无法解释的重复增量；
- 后续重复长任务不出现相同故障。

若需要统计意义上的 99%，需要大量独立长任务样本；本方案先实现并验证恢复机制，不能用一次成功宣称统计概率已经达到 99%。代码、日志和测试结果必须区分“本次完成”和“长期统计概率”。

## 3. 错误分类

### 3.1 `store-state-changed`

发生位置：

```text
页面脚本读取 before
  -> 与 Host 提供的 expected 比较
  -> 尚未执行 incrementLocalClickCount
```

性质：

- 本批尚未修改计数；
- 页面可能刚完成周期保存、归并或其他计数事件；
- 可安全放弃旧基准并重新计算。

处理：

```text
重新读取最新 snapshot
  -> 重新计算 remaining
  -> 更新 target
  -> 重新执行本批
```

预检冲突默认最多连续恢复 5 次，每次重新读取页面快照，退避约 `100 ms × attempt`。只要重新读取后以下条件成立，就允许继续：

- `loggedIn == true`；
- `accountKey` 与任务开始时一致；
- `available` 和 `cumulative` 不低于最后已确认值；
- 当前 `available` 尚未达到目标。

`local` 可以因页面周期归并而归零，不能继续作为必须与上一批相等的阻断条件。`consumed` 也允许页面自身变化，但仍记录用于审计。

不允许直接重复旧 CDP 参数，因为旧 expected 已经过期。连续 5 次仍无法获得稳定的新基准时，任务停止。

### 3.2 `post-save-state-not-confirmed`

发生位置：

```text
页面已执行本批增加
  -> 已调用 save_key_press_count
  -> 等待回写后未观察到预期增量
```

性质：

- 本批可能已经生效，只是页面尚未回写；
- 也可能保存失败或页面状态被回滚；
- 立即重发整批存在重复增加风险。

处理：

```text
只读轮询 after snapshot
  -> 若 available 和 cumulative 已达到 expected，判成功
  -> 若持续未达到，进入重新基准判断
  -> 无法证明未生效时停止
```

本错误路径禁止直接调用下一次 `incrementLocalClickCount`。

Host 侧最多只读复核 5 次，每次间隔约 400 ms，连同页面脚本自身的 500 ms 等待约 2.1 秒。每次复核都检查账号摘要和登录状态。`local` 是否归零不影响保存成功判断；`available` 和 `cumulative` 必须达到该批写入前记录的期望值。

### 3.3 `increment-not-confirmed`

发生位置：

```text
incrementLocalClickCount 循环内部
```

性质：

- 已经可能增加了一部分计数；
- 不能按原计划重发完整批次。

处理：

```text
读取最新 snapshot
  -> 根据实际增量计算剩余
  -> 仅在状态稳定且差值可证明时继续
  -> 否则停止并记录 pending
```

页面脚本发生增量循环异常时，应返回 `before` 和能够读取到的 `after`，不能让上层只拿到错误字符串。Host 用最新快照重新计算剩余量；如果无法确认实际增加了多少，停止而不是补发整批。

### 3.4 `save-failed`

已有行为是只重试保存，不重复增加。新方案保留：

- 最多 3 次保存重试；
- 保存重试之间使用短退避；
- 全部失败时停止本次任务；
- 不把保存失败当成允许重新增加的理由。

### 3.5 传输或页面重载错误

包括 CDP 超时、页面重新导航、WebSocket 断开。

- 不按最后已确认快照直接重发；
- 允许重启 Wuwa Tappo 并建立新的随机回环 CDP 会话，最多 3 次；
- 新会话必须重新读取页面绝对计数，并按原 target 重新计算 remaining；
- 新会话必须验证账号摘要未变化；
- 重启后实际计数与最后确认值不一致时，以页面持久化后的实际值为准，不重复旧批次；
- 3 次会话恢复仍失败时结束本次 Host。

2026-11-14 的 1,000,000 次测试在约 104,000 次后遇到 WebSocket 异常。恢复后只读页面计数比最后确认值多 1,000，证明丢失的是回执而不是写入。该现象说明会话级恢复必须按绝对计数重新对账。

## 4. 批次恢复状态机

```text
READ_SNAPSHOT
  -> CALCULATE_REMAINING
  -> APPLY_BATCH
      -> PREFLIGHT_CONFLICT
          -> REBASE
          -> CALCULATE_REMAINING
          -> APPLY_BATCH
      -> INCREMENT_CONFLICT
          -> VERIFY_ACTUAL_STATE
          -> RECALCULATE_OR_STOP
      -> SAVE
          -> SAVE_FAILED
              -> RETRY_SAVE_ONLY
          -> SAVE_RETURNED
              -> CONFIRM_STATE
                  -> CONFIRMED
                  -> AMBIGUOUS
                      -> READ_ONLY_POLL
                      -> CONFIRMED_OR_STOP
```

## 5. 重试预算

默认参数：

| 项目 | 默认值 | 说明 |
|---|---:|---|
| 页面单批上限 | 1,000 | 保持当前已验证值 |
| 预检连续恢复次数 | 5 | 只允许重新取样后重试 |
| 增量中断连续恢复次数 | 5 | 仅在部分增量已安全保存且核心计数未倒退时补差额 |
| 保存调用次数 | 3 | 不重复增加 |
| 保存后只读复核次数 | 5 | 每次只读，不重复增加 |
| 保存后复核间隔 | 400 ms | 连同脚本等待约 2.1 秒 |
| 恢复退避 | 200 ms × attempt | 最大值约 800 ms |
| 连续不可恢复失败 | 1 | 出现结果不明的写入后立即停止 |
| 会话级恢复次数 | 3 | 仅用于 CDP/WebSocket 断开，重连后按绝对计数对账 |

“连续 5 次失败”只适用于 `store-state-changed` 和可证明已安全保存部分的 `increment-not-confirmed`。`post-save-state-not-confirmed` 不能直接套用同一逻辑。

## 6. 幂等与目标计算

每次任务记录：

```text
operation_id
start_available
start_cumulative
target_available
target_cumulative
account_fingerprint
requested_delta
confirmed_success
confirmed_by_reconcile
preflight_conflicts
post_save_ambiguous
last_snapshot
```

建议使用追加式操作台账 `runtime/logs/refill-operations.jsonl`，至少记录：

```text
batch_started
batch_confirmed
batch_rebased
batch_ambiguous
task_completed
task_failed
```

台账只用于审计和故障判断，不在新任务中盲目自动续传旧批次。结果未知时保留 `pending`，下次启动时提示存在未结批次，但不自动重放。

每批使用最新页面状态计算：

```text
remaining = target_available - current_available
batch = min(1000, remaining)
expected_available = current_available + batch
expected_cumulative = current_cumulative + batch
```

额外页面增量可以加速达到目标，但不能被算成本 Host 的确认量。完成条件：

```text
current_available >= target_available
current_cumulative >= target_cumulative
```

## 7. 日志要求

新增结构化事件：

```text
native_batch_started
native_batch_preflight_conflict
native_batch_rebased
native_batch_increment_conflict
native_post_save_poll
native_batch_reconciled
native_batch_ambiguous
native_retry_budget_exhausted
native_refill_completed
```

失败日志必须包含：

```text
attempt
error
expected_available
actual_available
expected_cumulative
actual_cumulative
expected_local
actual_local
remaining
elapsed_ms
```

不得记录账号原值、Cookie、Token、Authorization 或完整请求头。

## 8. 代码范围

预期修改：

```text
portable/CdpHost.cs
portable/NativePageRefill.cs
portable/native/boost.js
portable/native/state.js
portable/NativeRefillRecovery.cs
tests/CdpHostSelfTest.cs
tests/Test-NativeRefillPackage.ps1
docs/native-count-refill-design.md
docs/test-results.md
README.md
tools/Build-Portable.ps1
```

必要时增加纯 C# 恢复策略类，使重试状态机可以无页面依赖地做单元测试。

不修改：

- 自动开箱的 UIA 流程；
- BetterGI 或系统键鼠输入；
- 服务器 HTTP 写接口；
- 账号、Cookie、Token 读取逻辑；
- 现有历史文件格式。

## 9. 测试方案

### 9.1 纯逻辑测试

必须覆盖：

1. 正常批次无额外等待；
2. 一次 `store-state-changed` 后重新取样并成功；
3. 连续 5 次预检冲突后停止；
4. `post-save` 第一次未确认、后续只读轮询确认；
5. `post-save` 5 次未确认时停止，不重复增加；
6. 页面额外增加导致目标提前达到；
7. 账号摘要变化立即停止；
8. 保存失败只重试保存；
9. 传输错误不重发未知批次；
10. 日志字段完整且不含凭据。

### 9.2 包结构测试

```powershell
pwsh -NoProfile -File .\tests\Test-CdpHostPathWalk.ps1
pwsh -NoProfile -File .\tests\Test-CdpHostPackage.ps1 `
  -PackageDirectory .\dist\WuwaTappoAutoChest-Portable-Native `
  -ExpectEmptyState
pwsh -NoProfile -File .\tests\Test-NativeRefillPackage.ps1 `
  -PackageDirectory .\dist\WuwaTappoAutoChest-Portable-Native `
  -ExpectEmptyState
```

### 9.3 真实分级测试

按顺序执行，前一级未通过不得进入下一级：

```text
1,000
5,000
50,000
100,000
500,000
1,000,000
```

真实测试前必须：

- 停止自动助手 Worker；
- 关闭控制面板；
- 保持 Wuwa Tappo 登录；
- 接受一次 UAC；
- 确认任务目标是增加量，不是绝对值。

每次记录：

- 起始和目标可用次数；
- 起始和目标累计次数；
- 实际耗时；
- 预检冲突次数；
- 保存后复核次数；
- 最大内存；
- 是否正常退出；
- 调试端口是否关闭；
- Wuwa Tappo 是否恢复普通启动。

### 9.4 500,000 次验收门槛

必须满足：

- 可用次数至少增加 500,000；
- 累计次数至少增加 500,000；
- Host 正常退出；
- 按任务结果判定为 completed；
- 日志中没有结果未知却继续增加的路径；
- 结束后普通模式启动成功；
- 重复测试不得出现无解释的双倍批次。

### 9.5 1,000,000 次验收门槛

只有在 500,000 次通过后执行。除上述门槛外：

- 内存不得随批次持续增长；
- 任务时长和日志应保持稳定；
- 不得出现页面 store 版本丢失；
- 如出现不可恢复歧义，保留日志并停止，不得宣称通过。

## 10. 交付物

- 更新后的原生 Host；
- 新目录 `dist/WuwaTappoAutoChest-Portable-Native-Recovery`，不得覆盖现有 Native 包；
- 新目录对应 ZIP、校验文件，分享包内状态和日志为空；
- 单元测试与包结构测试；
- 1,000、5,000、50,000、100,000、500,000 的真实测试记录；
- 1,000,000 测试结果，或明确说明尚未执行；
- 更新后的中文文档。

## 11. 执行顺序

1. 实现 Host 侧恢复状态机和结构化错误返回；
2. 补充脚本静态测试、恢复策略单元测试和日志字段测试；
3. 构建新的 `Native-Recovery` 包，不覆盖现有产物；
4. 由用户确认当前账号和操作窗口后，依次执行真实 1,000、5,000、50,000 次测试；
5. 前三档全部通过后执行 100,000 和 500,000 次；
6. 500,000 次通过后再决定是否执行 1,000,000 次；
7. 更新 README、设计文档和真实测试记录；
8. 对新包做隐私扫描和 ZIP 校验，再提交推送。

## 12. 风险与边界

- 页面内部函数不是稳定公开 API，应用更新可能改变导出名称或行为；
- 页面周期保存可能继续制造竞争，只能通过重新基准和只读确认降低；
- 结果未知时停止会比“追求 100% 自动继续”少做一些次数，但优先避免重复增加；
- 500,000 和 1,000,000 测试会真实修改当前账号计数，必须在用户授权的目标环境执行；
- 不通过服务器 HTTP 写接口规避页面状态竞争。

## 13. 最终执行结果

离线验证：

```text
C# 恢复策略与路径自检：22 项通过
Boost 页面恢复夹具：5 项通过
旧 CDP 包结构：17 项通过
原生 Recovery 包结构：40 项通过
核心回归：24 项通过
真实只读集成：27 项通过
```

真实补数：

| 请求增量 | 起始 available | 最终 available | 耗时 | 保存回滚重基准 | 结果 |
|---:|---:|---:|---:|---:|---|
| 500,000 | 2,355,575 | 2,855,575 | 262.8 秒 | 1 | 通过 |
| 1,000,000 | 2,855,575 | 3,855,575 | 525.1 秒 | 2 | 通过 |

两轮 `cumulativeClickCount` 也分别精确增加相同数量。500,000 和 1,000,000 任务均正常结束，Wuwa Tappo 恢复普通启动，没有遗留调试监听端口。

恢复分支真实命中的是“保存后 5 次复核均未前进，且 `localClickCount` 归零”：

```text
expected available = current + 1000
actual available   = current
expected cumulative = current + 1000
actual cumulative   = current
core count stable for 5 polls
```

该场景下 Host 按实际绝对计数重基准，没有重发已经计入的旧批次。最终页面计数精确等于原目标，证明恢复没有造成重复增加。

CDP/WebSocket 会话恢复已实现并完成策略测试，但最终两次长任务没有再次触发远程断连；因此不把“真实 WebSocket 断连后续跑”标记为已命中，只标记为已实现、已编译、已有单元边界测试。
