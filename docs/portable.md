# 便携版说明

## 1. 产物

便携目录：

```text
dist/WuwaTappoAutoChest-Portable/
```

可直接发送给朋友的压缩包：

```text
dist/WuwaTappoAutoChest-Portable.zip
```

主启动器：

```text
WuwaTappoAutoChest.exe
```

注意：不能只发送这个 EXE。它依赖同目录下的 `runtime` 文件夹，必须发送完整 ZIP 或完整目录。

## 2. 朋友的系统要求

- Windows 10 或 Windows 11 x64；
- 已安装 Wuwa Tappo；
- 已手动启动并登录 Wuwa Tappo；
- .NET Framework 4.8。

.NET Framework 4.8 是 Windows 10/11 的常见系统组件。便携版不要求安装 PowerShell 7，后台也不使用 PowerShell。

## 3. 使用流程

1. 解压 `WuwaTappoAutoChest-Portable.zip`；
2. 启动并登录 Wuwa Tappo；
3. 双击 `WuwaTappoAutoChest.exe`；
4. 点击“启动后台”；
5. 可以关闭控制窗口，后台任务会继续运行；
6. 再次打开 EXE 可查看状态或点击“停止后台”。

## 4. 控制面板

控制面板显示：

- 后台是否运行；
- 当前状态阶段；
- 字符数；
- 活动注入是否启用；
- 已发送活动键数量；
- 宝箱按钮数量；
- 最近开出的真实物品名称；
- 全局自动开箱历史（时间、物品、位置、结果）；
- 一次性补次数输入框、开始/取消按钮和实时进度；
- 最近错误和状态消息。

控制面板按钮：

- 启动后台；
- 停止后台；
- 刷新状态；
- 打开全图鉴；
- 使用说明。

关闭窗口不会停止后台工作进程。

控制面板采用 Codex 风格深色主题、等宽状态标签、绿色运行状态和圆角卡片。状态消息与历史记录区域为纯黑背景。指标卡与按钮会随窗口宽度自动换行，最低推荐窗口尺寸为 740 × 780。

布局实测：

- 740 × 780：指标卡两行，操作按钮自动换行，历史列表和一次性补次数区可见；
- 980 × 840：默认尺寸，历史列表完整显示。

## 5. 命令行入口

```powershell
WuwaTappoAutoChest.exe --start-background
WuwaTappoAutoChest.exe --stop-background
WuwaTappoAutoChest.exe --open-gallery
```

这些入口可用于桌面快捷方式或其他启动器。

## 6. 一次性 CDP 补次数

当前 Wuwa Tappo 1.0.3 / WebView2 156 实测没有开放远程调试端口，因此不要优先使用 CDP。日常补次数请使用主控制面板的“一次性补次数”；CDP Host 仅保留用于其他版本兼容性诊断。

F24 在长时间运行后停止计数时，可使用独立程序：

```text
WuwaTappoAutoChest-CdpHost.exe
```

该程序需要管理员权限，适合一次补足至少一天所需的数量。推荐流程：

1. 在主控制面板点击“停止后台”；
2. 关闭主控制面板；
3. 双击 CDP Host，并在 UAC 中选择“是”；
4. 输入本次增加量，点击“启动 CDP 并补足”；
5. 观察状态，完成、取消或失败后等待 Wuwa Tappo 自动恢复。

运行期间 CDP Host 会：

- 重启 Wuwa Tappo，并设置随机本机回环调试端口；
- 等待 `.pet-dock-content` 和字符数完成加载；
- 通过 WebView2 CDP 向页面宠物区域派发输入事件；
- 每批发送后等待页面渲染并核对字符数；
- 字符数连续不增长时停止；
- 在成功、取消和异常路径中关闭调试端口并重启 Wuwa Tappo。

不会读取或保存 Cookie、Token、Authorization 或账号资料，也不会产生系统鼠标事件。调试端口只用于本机 `127.0.0.1`。

注意：

- 必须等待主自动助手完全退出，否则 CDP Host 会拒绝运行；
- 关闭窗口时如果任务仍在运行，程序会先请求取消并等待恢复；
- 如果 Host 被任务管理器强制结束，调试端口可能随 Wuwa Tappo 保留到下次重启；此时请关闭并重新打开 Wuwa Tappo；
- 如果当前 Wuwa Tappo 版本禁用远程调试或不再接受页面输入，Host 会停止并恢复普通模式，不会无限重试。

## 7. 数据与配置

便携数据均在自身目录：

```text
runtime/config/autochest.json
runtime/state/status.json
runtime/state/operation-state.json
runtime/state/history.jsonl
runtime/logs/
```

移动整个便携目录后，状态、历史和日志路径会随目录变化，不依赖原开发目录。`history.jsonl` 跨 Worker 与控制面板重启保留；发送给朋友的初始 ZIP 不包含本机历史。

奖励名称识别只读取在线版 Wuwa Tappo 的 WebView2 本地资产 ID 与图片缓存，并使用随包图鉴的 SHA-256 索引匹配真实物品名。不会读取或输出账号、密码、Cookie、Token、验证码、请求头或聊天内容。

CDP Host 的独立日志位于：

```text
runtime/logs/cdp-host.jsonl
```

## 8. 离线图鉴

便携包内包含完整的 899 项离线图鉴：

```text
runtime/gallery/index.html
```

也可以通过控制面板的“打开全图鉴”进入。

## 9. 安全边界

- 不包含账号、密码、Cookie、Token 或本机登录状态；
- 不修改 Wuwa Tappo 安装目录；
- 不修改账号背包数据；
- 兑换码由官方服务端校验；
- 需要登录或安全验证时必须由用户完成。

## 10. 重建便携包

```powershell
pwsh -NoProfile -File .\tools\Build-Portable.ps1 -Force
```

构建时会：

- 复制配置、说明和离线图鉴；
- 编译原生 C# Worker 和图形启动器；
- 编译带 `requireAdministrator` 清单的独立 CDP Host；
- 生成完整便携目录和 ZIP。

## 11. 实测资源占用

原生 Worker 稳定空闲采样：

```text
Worker 工作集：约 43.3 MB
Worker CPU：约 0.70% 单核
```

同时打开控制面板：

```text
Worker + 控制面板工作集：约 78–88 MB
```

字符数达到 10,000 后 Worker 不再发送活动输入，只按空闲间隔扫描宝箱。应用自身仍要求至少 1,000 字符才会显示宝箱入口。

快速补充模式将每 500 个 F24 合并成一次 `SendInput`，每轮最多 1000 个键。补足阶段允许较高 CPU，达标后回到约 0.62% 单核，Worker 内存约 41 MB。
