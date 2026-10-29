# Wuwa Tappo 离线全图鉴

## 1. 说明

官方客户端没有公开“全图鉴兑换码”。兑换请求由服务端校验，客户端只负责提交代码，因此不能通过本地修改伪造一个能够全解锁的兑换码。

本项目提供一个不修改账号数据的替代方案：从官方公开资源接口读取完整图鉴目录，将资源和名称缓存到本地，生成可搜索、筛选的离线浏览页面。

该页面展示图鉴内容，不代表 Wuwa Tappo 账号已经拥有对应收集物。

## 2. 生成结果

当前已生成：

```text
gallery/index.html
gallery/catalog.json
gallery/manifest.json
gallery/images/character/          260 项
gallery/images/accessory/          135 项
gallery/images/emote/              504 项
```

合计：

- 图鉴条目：899
- 本地图片：899
- 图片格式：PNG，560 × 560
- 图片总大小：约 36.8 MB
- 中文名称：899/899
- 图片映射缺失：0
- 下载失败：0

## 3. 数据来源

生成器只读取以下官方公开资源：

```text
https://api-mc-wuwatappo.kurogames-global.com/api/common/activity-config
https://api-mc-wuwatappo.kurogames-global.com/api/common/atlas
https://mc-wuwatappo.kurogames-global.com/app/json-host/appearance_asset_host.json
https://mc-wuwatappo.kurogames-global.com/app/json-host/dict_host.json
```

生成器不会：

- 登录或读取用户凭据；
- 调用兑换接口；
- 修改背包、账号或游戏数据；
- 修改 Wuwa Tappo 安装目录；
- 伪造兑换码。

图片和名称版权归 Kuro Games / Irox Games 所有，仅用于个人离线浏览。

## 4. 打开方式

直接双击：

```text
gallery/index.html
```

页面支持：

- 按角色、配件、表情筛选；
- 按 Common、Uncommon、Rare、Epic 筛选；
- 按名称、稀有度、编号排序；
- 搜索中文名、英文名、资源名和编号；
- 本地图片懒加载；
- 完全断网浏览。

## 5. 更新图鉴

官方资源更新后运行：

```powershell
pwsh -NoProfile -File .\tools\Build-AtlasGallery.ps1
```

仅更新元数据、不下载图片：

```powershell
pwsh -NoProfile -File .\tools\Build-AtlasGallery.ps1 -SkipImages
```

强制重新下载全部图片：

```powershell
pwsh -NoProfile -File .\tools\Build-AtlasGallery.ps1 -ForceDownload
```

## 6. 测试

```powershell
pwsh -NoProfile -File .\tests\Run-AtlasGalleryTests.ps1
```

重新生成后再测试：

```powershell
pwsh -NoProfile -File .\tests\Run-AtlasGalleryTests.ps1 -Rebuild
```

浏览器验证结果：

- 全部条目渲染：899；
- 角色筛选：260；
- 配件筛选：135；
- 表情筛选：504；
- 搜索“爱弥斯”：15 项；
- 搜索结果图片加载：15/15；
- 浏览器控制台错误：0。
