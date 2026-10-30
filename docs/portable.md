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
- 最近错误和状态消息。

控制面板按钮：

- 启动后台；
- 停止后台；
- 刷新状态；
- 打开全图鉴；
- 使用说明。

关闭窗口不会停止后台工作进程。

控制面板采用 Codex 风格深色主题、等宽状态标签、绿色运行状态和圆角卡片。状态消息与历史记录区域为纯黑背景。指标卡与按钮会随窗口宽度自动换行，最低推荐窗口尺寸为 740 × 700。

布局实测：

- 740 × 700：指标卡两行，操作按钮自动换行，历史列表可见，无控件越界；
- 980 × 760：默认尺寸，历史列表完整显示。

## 5. 命令行入口

```powershell
WuwaTappoAutoChest.exe --start-background
WuwaTappoAutoChest.exe --stop-background
WuwaTappoAutoChest.exe --open-gallery
```

这些入口可用于桌面快捷方式或其他启动器。

## 6. 数据与配置

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

## 7. 离线图鉴

便携包内包含完整的 899 项离线图鉴：

```text
runtime/gallery/index.html
```

也可以通过控制面板的“打开全图鉴”进入。

## 8. 安全边界

- 不包含账号、密码、Cookie、Token 或本机登录状态；
- 不修改 Wuwa Tappo 安装目录；
- 不修改账号背包数据；
- 兑换码由官方服务端校验；
- 需要登录或安全验证时必须由用户完成。

## 9. 重建便携包

```powershell
pwsh -NoProfile -File .\tools\Build-Portable.ps1 -Force
```

构建时会：

- 复制配置、说明和离线图鉴；
- 编译原生 C# Worker 和图形启动器；
- 生成完整便携目录和 ZIP。

## 10. 实测资源占用

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

快速补充模式将每 100 个 F24 合并成一次 `SendInput`，每批 500 个键。实测补充 1500 次约需 3.9 秒；补足阶段约 10.5% 单核，达标后回到约 0.62% 单核，Worker 内存约 41 MB。
