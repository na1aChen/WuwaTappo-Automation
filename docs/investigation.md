# Wuwa Tappo 阶段 1 调查

- 调查日期：2026-10-27
- 调查范围：当前工作区、已安装 Windows 程序、进程与运行环境、用户数据、数字签名、公开更新入口和可自动化接口
- 当前结论：已确认产品形态和技术栈，但尚未获得具体业务自动化目标，因此不能可靠地梳理完整人工流程或进入实现阶段

## 1. 结论摘要

1. 当前工作区不是源码仓库，没有 Git、README、配置文件、构建脚本或测试代码；只有 `WuwaTappo.exe` 和 `uninstall.exe` 两个 Windows 可执行文件。
2. 产品名为 **Wuwa Tappo**，版本 **1.0.1**，为 Windows x64 GUI 程序。安装信息显示发行方为 `kuro`；程序内存在来自库洛账号 SDK 的配置和库洛域名，更新包文件名包含 `uat-cn`，因此当前可判定为 **中文 UAT 测试构建**。
3. 程序使用 **Tauri 2.10.3 + Microsoft WebView2**。当前 WebView2 Runtime 版本为 `155.0.4283.62`，页面来源为 `http://tauri.localhost`。
4. 本地二进制中存在 `open_bag`、`open_atlas`、`open_exchange`、`open_room`、`toggle_pet`、`open_settings`、`login` 等入口名称，说明程序至少包含背包、图鉴、兑换、房间、宠物、设置和登录相关界面。仅凭名称不能确认这些入口的具体业务行为。
5. 没有发现公开官方 API、受支持的 CLI、本地 HTTP 自动化端口或现有测试接口。Tauri IPC 属于应用内部接口，不能当作稳定的第三方自动化 API。
6. 主程序 Authenticode 状态为 `HashMismatch`。这表示当前文件内容与签名中记录的摘要不一致，自动化投产前必须确认安装包可信来源，或改用官方重新下载且签名校验通过的版本。
7. 用户尚未说明具体要自动化哪个业务操作、输入是什么、成功标志是什么以及允许写入哪些真实数据。因此阶段 1 的核心需求尚不明确，不能负责任地编写“全自动”业务脚本。

## 2. 工作区与版本控制

### 2.1 已确认事实

当前目录为：

```text
%PROGRAMFILES(X86)%\WuwaTappo
```

完整文件清单：

| 文件 | 大小（字节） | 最后修改时间 |
|---|---:|---|
| `WuwaTappo.exe` | 77,958,880 | 2026-10-26 21:45:22 |
| `uninstall.exe` | 70,669,024 | 2026-10-26 21:44:38 |

调查结果：

- `git status`、`git remote` 和 `git log` 均返回“not a git repository”。
- 没有 `.git`、`README*`、`.env*`、YAML、TOML、JSON 配置、源代码、测试、安装脚本或文档。
- 当前工作区没有可复用实现。

### 2.2 对开发的影响

需要从零建立独立自动化工程，不能复用现有业务代码。由于目录同时是安装目录且位于 `Program Files (x86)` 下，后续源码不应直接和这两个二进制混合部署；建议将自动化工程放在独立子目录或另行建立 Git 仓库。

## 3. 程序身份与运行环境

### 3.1 PE 与版本信息

| 项目 | `WuwaTappo.exe` |
|---|---|
| ProductName | Wuwa Tappo |
| FileDescription | Wuwa Tappo |
| CompanyName | kuro |
| FileVersion | 1.0.1 |
| ProductVersion | 1.0.1 |
| PE 架构 | `0x8664`，x64 |
| PE 子系统 | Windows GUI |

操作系统：

```text
Microsoft Windows 11 专业版
10.0.26100, build 26100, 64 位
```

### 3.2 安装登记

注册表卸载项：

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\WuwaTappo
```

关键字段：

| 字段 | 值 |
|---|---|
| DisplayName | Wuwa Tappo |
| DisplayVersion | 1.0.1 |
| Publisher | kuro |
| InstallLocation | `%PROGRAMFILES(X86)%\WuwaTappo` |
| DisplayIcon | `%PROGRAMFILES(X86)%\WuwaTappo\WuwaTappo.exe` |
| UninstallString | `%PROGRAMFILES(X86)%\WuwaTappo\uninstall.exe` |

这是当前用户级卸载登记，但程序目录位于 `Program Files (x86)`。卸载和升级时需要注意当前用户权限及安装目录权限。

### 3.3 当前进程

调查时程序正在运行：

| 项目 | 值 |
|---|---|
| PID | 26420 |
| 进程名 | `WuwaTappo.exe` |
| 主窗口标题 | Wuwa Tappo |
| 窗口类 | `Tauri Window` |
| 窗口大小 | 2560 × 1440 |
| 响应状态 | Responding |

进程下存在独立的 Microsoft Edge WebView2 进程树。WebView2 用户数据目录为：

```text
%LOCALAPPDATA%\com.kuro.wuwatappo.cn\EBWebView
```

对进程树执行的只读 TCP 检查没有发现监听端口；观察到 WebView2 浏览器进程有两条到 Microsoft Azure 地址 `40.104.23.66:443` 的活动 TLS 连接。该地址不属于库洛域名，不能据此直接认定具体业务用途，可能是 WebView、遥测或更新基础设施。

### 3.4 更新入口显示的构建性质

二进制中存在公开可访问的更新描述入口：

```text
https://kurowuwabuddy2602.test.modiarts.com/app/json-host/latest_host.json
```

该入口在调查时返回：

```json
{"url":"https://kurowuwabuddy2602.test.modiarts.com/app/version-json/latest.json?t=1793012943"}
```

随后读取的版本描述显示：

- 版本：`1.0.1`
- Windows 包文件名：`WuwaTappo-uat-cn_1.0.1_x64-setup.exe`
- 发布时间：`2026-10-26T11:04:38.000Z`
- 更新说明为中文，包含“修复了一些已知问题”“支持任务栏图标显示或隐藏设置”“调整游戏模式默认快捷键”

因此当前目标不是稳定公开版本，而是 UAT 中文测试环境。自动化脚本必须固定并检测版本，否则界面或接口变化会直接破坏流程。

## 4. 数字签名与可信度

### 4.1 文件摘要

| 文件 | SHA-256 |
|---|---|
| `WuwaTappo.exe` | `DD340C92A7320D1ECF3697BA65B4AD23D8F196ACB547D2BAF3F75BA09D696D97` |
| `uninstall.exe` | `6CE324C784797DFF64A6B5031120FD28F07397520DDCF89AA366951470370AD6` |

### 4.2 Authenticode 校验

| 文件 | 状态 | 签名主体 |
|---|---|---|
| `uninstall.exe` | Valid | 广州库洛数界科技有限公司 |
| `WuwaTappo.exe` | **HashMismatch** | 证书主体仍显示为广州库洛数界科技有限公司 |

`HashMismatch` 的具体含义是：当前文件内容与签名中的文件摘要不一致。它可能来自更新、打包、安全软件改写、下载损坏或非授权修改，但在没有来源证据时不能排除供应链风险。

**该项是投产阻塞项。** 在自动化程序接触真实账号、奖励领取、兑换或其他写入操作前，必须先确认：

- 当前安装包是否由用户从可信测试渠道获得；
- 是否存在安装后自更新或补丁步骤；
- 官方来源当前是否提供签名完整且版本相同的安装包；
- 是否可以把目标切换到签名校验通过的可复现版本。

## 5. 技术栈与可自动化接口

### 5.1 已确认技术栈

二进制字符串和用户数据共同确认：

- Tauri：`tauri-2.10.3`
- Tauri Runtime：`tauri-runtime-wry-2.10.1`
- WebView：`wry-0.54.4` + Microsoft Edge WebView2
- WebView2 Runtime：`155.0.4283.62`
- 页面源：`http://tauri.localhost/`
- 用户数据标识：`com.kuro.wuwatappo.cn`

这不是 Electron 应用。主程序负责本地窗口、托盘、自动启动、宠物和系统交互，页面由 WebView2 渲染。

### 5.2 二进制中的本地入口

只读字符串检查发现了以下与功能相关的名称：

```text
open_bag
open_atlas
open_exchange
open_room
toggle_pet
open_settings
window_passthrough
restart
quit
login
update
about
```

还发现了托盘相关命令，例如宠物可见性、窗口透明度、窗口穿透和游戏模式设置。

这些名称只能证明本地命令或事件符号存在，不能证明：

- 命令的输入参数和返回结构；
- 命令是否已向 Web 页面开放；
- 调用后是否会发起网络写入；
- 是否有服务端限流、风控或二次确认；
- 版本升级后命令是否保持兼容。

因此不应直接调用这些 Tauri IPC 符号来构建生产流程。

### 5.3 未发现的公开接口

调查没有发现：

- 官方开发者文档或公开业务 API；
- 受支持的命令行参数；
- 本地 HTTP 服务或监听端口；
- Webhook、消息队列或导出文件接口；
- 可复用 SDK；
- 代码仓库或测试桩。

Tauri 命令通过应用内部 IPC 和 invoke key 暴露，只应被应用自己的页面调用。把内部 IPC 当公开 API 会带来版本兼容、权限绕过和账号风险，也不符合“官方接口优先”的原则。

### 5.4 浏览器/桌面自动化的可行性

| 方案 | 当前判断 | 原因 |
|---|---|---|
| 官方业务 API | 不可用 | 未发现公开 API、鉴权和文档 |
| CLI | 不可用 | 未发现稳定命令入口 |
| Tauri/WebView2 调试协议 | 有条件可行 | 需要重启并增加 WebView2 调试参数；当前实例未监听调试端口 |
| Tauri WebDriver | 有条件可行 | 需要 `tauri-driver`、匹配的驱动和测试启动参数；尚未验证此 UAT 构建 |
| Windows UI Automation | 部分可行 | 顶层 Tauri 窗口可识别，但 Web 内容在辅助功能树中没有暴露文本和按钮 |
| 固定坐标鼠标键盘 | 最后备选 | 对分辨率、DPI、透明穿透、动画和弹窗非常敏感，稳定性不足 |

当前 Windows UI Automation 只识别到 `Tauri Window`、`DesktopCatInput`、`WRY_WEBVIEW` 和 WebView2 容器，未暴露可操作的页面控件。截图也因应用是透明覆盖层而没有提供可靠的业务界面文本。

在获得授权后，最值得优先验证的是 **受控重启后启用 WebView2 调试协议或 Tauri WebDriver**，以 DOM 语义定位控件并检查实际请求。只有该路线不适用时才考虑普通桌面 UI 自动化。

## 6. 用户数据、登录与设置

### 6.1 数据目录

已观察到的目录：

```text
%APPDATA%\com.kuro.wuwatappo.cn
%LOCALAPPDATA%\com.kuro.wuwatappo.cn
%LOCALAPPDATA%\com.kuro.wuwatappo.installer
```

Roaming 目录包含：

- `ui-locale.txt`
- `cumulative_click_cn-cn-prod.dat`
- `key_press_count_cn-cn-prod.dat`

这说明应用会记录点击和按键统计，但具体数据格式和用途尚未确认。

### 6.2 设置数据

WebView Local Storage 中存在键 `wuwatappo-settings`。调查时观察到的配置字段包括：

```text
position
windowOpacity
petScale
windowPassthrough
hoverAutoHide
alwaysShowChest
gameMode
showTaskbarIcon
launchAtLogin
```

调查时的一组值为：

```text
position=bottomRight
windowOpacity=100
petScale=100
windowPassthrough=false
hoverAutoHide=false
alwaysShowChest=true
gameMode=false
showTaskbarIcon=true
launchAtLogin=false
```

当前没有检测到 `WuwaTappo` 的 Run 项或同名计划任务。该结论只适用于当前用户和当前时刻。

`alwaysShowChest` 的字面含义可能涉及奖励箱显示，但尚无证据证明它会自动领取奖励，不能据此设计自动领取流程。

### 6.3 登录与鉴权线索

本地数据中出现：

- 登录状态相关键名 `wuwatappo-login-cuid`；
- `gameId=G152`、`pkgId=A1476`；
- Kuro 账号 SDK 登录页、账号中心、客服和隐私政策地址；
- 手机号登录开关；
- Geetest 人机验证页面配置；
- `mc-wuwatappo.kurogames-global.com` 业务域名。

由此可以推断应用接入库洛账号体系，手机号登录可能需要短信、账号中心页面或 Geetest 验证。**本调查没有读取或输出任何凭据、令牌或验证码。**

自动化边界应先设定为：

- 登录、短信、二维码、Geetest 或人工安全验证必须由用户完成；
- 不绕过验证码、风控、设备验证或授权限制；
- 自动化程序不得把密码、令牌、Cookie 或密钥写入源码或提交到 Git；
- 会话过期时停止并请求人工处理，不能循环撞库或反复提交。

### 6.4 日志与恢复证据

日志文件：

```text
%LOCALAPPDATA%\com.kuro.wuwatappo.cn\logs\Wuwa Tappo.log
```

调查时文件大小为 0 字节。应用没有提供足够的公开运行日志来支持自动化恢复，后续脚本必须建立自己的结构化日志、状态快照和幂等记录。

## 7. 人工流程梳理状态

当前只能确认应用入口和可能的模块，无法确认完整业务操作。以下内容均未验证：

| 环节 | 已知信息 | 尚缺信息 |
|---|---|---|
| 启动 | 可通过桌面快捷方式或 `WuwaTappo.exe` 启动 | 是否必须等待宠物、托盘或更新检查完成 |
| 登录 | 存在 Kuro 账号 SDK、手机号/Geetest 线索 | 当前是否已登录、账号类型、会话期限 |
| 背包 | 存在 `open_bag` 字符串 | 输入、页面、可执行操作、成功标志 |
| 图鉴 | 存在 `open_atlas` 字符串 | 是否需要联网、是否有写操作 |
| 兑换 | 存在 `open_exchange` 字符串 | 兑换对象、库存、消耗、确认步骤、幂等规则 |
| 房间 | 存在 `open_room` 字符串 | 动作类型、权限、多人状态同步 |
| 宠物 | 存在 `toggle_pet`、穿透和缩放设置 | 是否只是外观，还是包含任务/奖励 |
| 奖励箱 | 存在 `alwaysShowChest` 设置 | 是否有领取入口、每日限制、服务端成功标志 |
| 更新 | 有 UAT 更新描述 | 是否强制更新、更新期间状态保存 |
| 退出/重启 | 存在 `restart`、`quit` | 执行前需要保存哪些状态 |

因为用户尚未指定业务目标，缺少以下开发所必需的核心信息：

1. 要执行的具体操作；
2. 每一步的人工输入与前置条件；
3. 业务成功标志和服务端确认方式；
4. 哪些操作会消耗资源、领取奖励或修改数据；
5. 失败后哪些步骤可重试、哪些绝对不能重放。

在这些信息缺失时编写的“全自动脚本”只能建立在猜测上，不满足正确性和数据完整性要求。

## 8. 风险清单

| 风险 | 影响 | 当前处理 |
|---|---|---|
| 主程序签名 `HashMismatch` | 供应链与自动化可靠性不可接受 | 投产前必须确认来源或重新获取可信安装包 |
| UAT 版本快速变化 | DOM、命令、接口和业务规则不稳定 | 固定版本检测；适配失败时安全停止 |
| 内部 Tauri IPC 非公开 API | 升级后失效，可能绕过应用权限边界 | 不作为生产接口 |
| 登录依赖 Kuro SDK 和 Geetest | 无法安全全自动处理验证 | 人工完成登录/验证，脚本等待状态 |
| 兑换或领取可能产生不可逆写入 | 重复执行、资源损失、账号风险 | 必须先获得明确授权并建立幂等凭据 |
| 当前应用日志为空 | 无法依赖应用日志恢复 | 自建状态机、运行记录和人工接管点 |
| 透明全屏覆盖层 | 普通截图与坐标点击不稳定 | 优先 DOM/WebView 自动化，避免固定坐标 |
| WebView2 和 UAT 更新 | 自动化驱动版本不匹配 | 启动前检查版本并在不匹配时停止 |

## 9. 技术选型结论（暂定）

在核心业务流程明确且安装包可信的前提下，建议优先顺序为：

1. **官方受支持的 API 或 CLI**：目前不存在，继续寻找官方文档后才能采用。
2. **WebView2 DevTools Protocol / Tauri WebDriver**：使用 DOM 语义选择器、状态等待和真实网络响应判断成功，最适合当前 Tauri 应用。
3. **Windows UI Automation**：在 Web 内容无法通过调试协议访问时使用语义控件；当前实例可访问性不足，需要进一步验证。
4. **图像识别或固定坐标**：仅作为最后手段，并保留人工接管，不应默认用于兑换或领取。

任何方案都必须：

- 使用配置文件和本地安全存储，不硬编码凭据；
- 记录步骤开始、提交前、服务端确认后等关键状态；
- 在提交前写入持久化的操作意图，成功确认后标记完成；
- 对不可重试操作禁止盲目重试；
- 到达验证码、登录过期、未知弹窗或状态不一致时停止；
- 提供超时、取消、状态检查和恢复入口；
- 先在无副作用模式或测试账号上验证。

## 10. 下一步

阶段 2 开始前，需要用户确认具体业务场景。确认后仍应先进行一次受控、只读的流程录制或 DOM/网络调查，再编写 `docs/design.md`。

当前最关键的 3 个问题：

1. **具体要自动完成什么？** 请按实际顺序说明目标流程，例如“启动 → 登录 → 进入背包 → 领取奖励 → 验证已领取”，并给出每一步的成功标志；如果要操作图鉴、兑换、房间、宠物或任务，请明确是哪一项。
2. **当前 UAT 1.0.1 和账号是否可用于测试？** 请说明是否使用专用测试账号、允许执行哪些只读或写入操作，以及哪些操作（兑换、领取、购买、发送、删除等）必须先逐次确认。
3. **是否确认当前安装包来源可信？** 主程序当前签名校验失败。请说明安装包来源，是否允许先关闭正在运行的应用、用 WebView2 调试参数重启做只读调查，或是否愿意重新获取一个签名校验通过的版本。

在这 3 项确认前，不进入业务代码实现，也不对真实账号执行任何写入操作。

## 11. 用户确认与补充调查

### 11.1 用户确认

用户在阶段 1 后补充确认：

1. 自动化目标为“自动累积字符数、自动获取宝箱并开启宝箱”。
2. 当前 UAT 1.0.1 和账号允许测试，但测试必须后台静默运行。
3. 用户明确表示“完全信任文件夹”。

因此，前文关于安装包来源的阻塞项已被用户接受为当前测试环境前提。签名不一致仍是客观风险，生产部署前仍建议重新校验可信安装包。

### 11.2 宝箱规则

在用户确认文件夹可信后，对安装目录内 Tauri Brotli 资源进行了只读、内存解压，并在前端资源中找到以下明确规则：

- 每累计 30 分钟活跃时间，宝箱槽内生成一个宝箱。
- 宝箱槽最多保存 2 个未开启宝箱。
- 两个槽位都满时，活跃计时暂停。
- 每次开启消耗 1,000 字符数。
- 鼠标点击和键盘敲击会累积字符数。
- 首次开启必定获得角色，第二次必定获得配件。

这说明“获取宝箱”受官方应用的活跃时间和字符数规则控制。自动化应通过应用正常接收的键盘输入路径累积计数，不直接修改计数、宝箱计时或调用内部调试事件。

### 11.3 可验证的宝箱界面状态

前端模板中的关键状态和控件如下：

```text
As = 1000
character count >= 1000 才允许显示开启入口
宝箱存在时渲染 button.reward-btn
左侧进度/按钮容器使用 reward-anchor reward-anchor--left
右侧进度/按钮容器使用 reward-anchor reward-anchor--right
开启过程中设置 disabled
```

Windows UI Automation 对当前 WebView 页面可读取到真实的 HTML Button 和 CSS 类。例如已经验证：

- `settings-btn` 暴露为 `ControlType.Button`；
- 支持 `InvokePattern`；
- `emotion-dot` 同样暴露为可调用按钮；
- 当前扫描没有发现 `reward-btn`；
- 阶段 1 测试时页面文字节点显示字符数为 `934`。

由于当时的 `934 < 1000`，不符合开启条件，所以没有执行真实开启请求。

### 11.4 业务请求与响应字段

前端代码中确认存在以下宝箱相关请求：

```text
POST /api/frontend/chests/open
body: {
  "place": "left" | "right",
  "click_num": <当前累计点击计数>
}
```

响应处理代码使用以下字段：

```text
click_consume
asset_id
asset_name
asset_type
rarity_type
left_next_time
right_next_time
```

虽然内部 HTTP 接口和鉴权方式已经被识别，但它不是公开 API。直接提取 Cookie、Bearer Token 或直接发请求会扩大安全边界，并使重复开启检测更困难。当前设计应优先点击应用自身渲染出的 `.reward-btn`，保留应用原有的登录、业务锁、动画、服务端校验和幂等逻辑。

### 11.5 后台静默可行性

当前 `WuwaTappo.exe` 以较当前终端更高的完整性级别运行，普通权限进程不能结束它，但可以读取它的 UI Automation 树。因此：

- 自动化程序可以在不切换前台焦点、不移动鼠标的情况下，通过 `InvokePattern` 触发现有 HTML 按钮；
- 已验证注入无文本含义的 `F24` 键可以触发应用正常的全局字符计数；
- 活动注入应严格限速，并在宝箱出现、计数不增长或收到停止请求时立即暂停；
- 不应尝试强制结束或重启高权限应用；
- 若应用未运行，默认由用户先启动，自动化程序不默认触发 UAC；
- 测试日志、状态文件和工作进程窗口均应在后台隐藏运行。

### 11.6 更新后的阶段结论

核心业务目标、规则、主要界面标志和请求语义已经明确，可以进入设计阶段。

仍需在实现后验证的关键点：

- 宝箱实际出现时 `reward-btn` 的 UIA 类名和左右槽位识别；
- `InvokePattern` 后按钮消失与奖励弹窗出现的时序；
- 网络失败时按钮是否保留；
- 字符数不足、未登录、强制更新等异常状态。

这些都可以通过“后台只读扫描 + 有宝箱时单次开启 + 成功后冷却”的低风险设计验证，不需要直接调用内部 API。

### 11.7 字符数自动累积验证

用户进一步明确字符数也必须自动累积后，进行了以下实际验证：

1. 使用无害虚拟键 `F24` 发送一次按键，UI Automation 读取的字符数从 `70` 变为 `71`。
2. 通过实现模块再发送一次，字符数从 `73` 变为 `74`。
3. 使用短批次发送 2 次，计数由 `75` 增长至 `78`。
4. 后台隐藏工作进程发送 5 个活动键，计数由 `78` 增长至 `92`；工作进程主窗口句柄为 `0`，随后优雅停止。

这证明当前 UAT 构建会把合成 `F24` 输入计入正常字符数增长。实现不直接修改计数，也不调用前端内部的 `boost-local-clicks`。

为避免高频输入和失控：

- 最初测试了 0 毫秒额外间隔，实际约 3.27 毫秒/键，但持续运行性能开销过高；
- 最终采用每键 50 毫秒、每批 20 次，并在字符数达到 1,000 后停止输入；
- 每批发送后重新扫描宝箱和计数；
- 每次发送后读取计数；
- 宝箱按钮出现时立即停止活动注入；
- 计数刷新仅用于状态记录，不因 UI 异步刷新暂停；只有 Windows 输入注入明确失败时才停用活动。

### 11.8 官方免费、联机功能和兑换码核实

公开资料核实结果：

1. Wuwa Tappo 是由《Bongo Cat》开发商 Irox Games 正式授权、库洛推出的《鸣潮》主题桌宠软件。
2. 官方公告明确写明“软件永久免费”，这一点属实。
3. 同一官方公告同时写明“还可与好友在线联机”，客户端也包含登录、多人房间和宝箱服务端请求，因此不能认定为“完全离线单机”。
4. 官方公开兑换码为：

```text
WuwaTappo
```

5. 该兑换码的官方范围是每个账号兑换一次，永久有效，奖励为“秧秧·经典”1 个，不是全部图鉴。

公开来源：

- 官方下载页：`https://download-wutheringwaves-wuwatappo.kurogames-global.com/download/en/`
- TapTap 官方公告：`https://www.taptap.cn/moment/850387288096508905`
- 库洛社区公告：`https://www.kurobbs.com/mc/post/1550859675671269376`

用户可在 Wuwa Tappo 的“设置 → 兑换码”中输入上述官方兑换码。

调查未发现任何公开的“全图鉴兑换码”。客户端中的收集物仍由宝箱和正常收集流程获得。

### 11.9 在线版真实物品名与历史记录

用户明确要求只处理在线版后，重新核对了在线版 `com.kuro.wuwatappo.cn` 的 WebView2 数据：

1. 奖励弹窗的无障碍名称通常只有 `New character!`、`New accessory!`、`New emote!` 或“重复收集物”，不能直接作为真实物品名。
2. 本地存储包含 `bag_new_asset_ids`，可读出最新奖励资产 ID；但部分新 ID 尚未进入公开图鉴接口，不能保证全部映射。
3. 在线版 WebView2 缓存中的奖励图片是原始 PNG，内容与随包图鉴图片字节一致。
4. 实测最近 200 个缓存 PNG 中，180 个与图鉴图片 SHA-256 完全一致，包括角色、配件和表情。
5. 因此实现采用“修改前缓存快照 + 开箱后新增/变化 PNG 的 SHA-256 精确匹配”，并将资产 ID 作为辅助路径。

该方案不读取 Cookie、Token、账号资料或请求头，也不依赖离线版目录。若图片缓存未产生新文件且资产 ID 不在图鉴中，界面会明确显示“名称未提供”，不做猜测。

### 11.10 WebView2 CDP 一次性补次数调查

F24 长时间运行后停止计数的表现已经复现：

- `SendInput` 返回成功；
- 字符数在任务前期正常增长；
- 后续相同 F24 输入不再增加字符数；
- 当前程序检测到一批输入无增长后会停止，不继续空转；
- 主 Worker 不会模拟桌面鼠标点击来恢复窗口激活。

因此新增了“独立管理员 CDP Host”调查分支。其依据是 Wuwa Tappo 使用 Tauri 2.10.3 和 WebView2，页面来源为 `http://tauri.localhost`；WebView2 支持通过附加浏览器参数启用本机远程调试，并提供 `Runtime.evaluate` 与 `Input.dispatchMouseEvent`。

已确认的页面结构：

```text
.pet-dock-content
  .reward-countdown
  字符数文本节点
  .settings-btn
```

当前 UI Automation 实测宠物区域边界为：

```text
X=2312, Y=583, Width=200, Height=211
```

字符数不在该区域的第一段文本中，区域里还包含宝箱倒计时。因此 CDP 读取逻辑不能简单截取 `innerText` 的第一个数字，而应排除 `.reward-countdown` 后选择字符数叶子节点。

计划使用的启动参数：

```text
WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=
  --remote-debugging-port=<随机端口>
  --remote-debugging-address=127.0.0.1
```

当前调查环境对该启动路径的限制：

- 已正常结束旧 Wuwa Tappo 并重新启动；
- 带远程调试参数的重启命令被当前执行策略拒绝；
- 没有绕过执行策略；
- Wuwa Tappo 随后已恢复普通启动；
- 当前环境未取得管理员 UAC 交互，因此无法完成真实 CDP 页面连接和点击验收。

已选择的安全边界：

- 独立 EXE 声明 `requireAdministrator`，由用户在桌面上明确接受 UAC；
- 不把管理员权限加入主 Worker；
- 每次使用随机回环端口，只选择 `tauri.localhost` 页面；
- 不读取 Cookie、Token、Authorization、账号资料或请求头；
- 完成、取消和异常均关闭调试端口并重启 Wuwa Tappo；
- 不修改存档、字符数或宝箱计时。

仍未确认：

- 目标桌面接受 UAC 后 WebView2 是否实际开放该随机回环端口；
- 当前 Wuwa Tappo 1.0.3 是否接受 CDP 鼠标事件并计入字符数；
- 页面输入吞吐和长任务稳定性；
- 应用后续更新是否会改变 `.pet-dock-content` 结构或输入策略。
