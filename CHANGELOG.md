# 📜 Changelog

本文件记录 `LaunchInPlaceReload` 的版本演进历程。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

---

## [1.0.0] - 2026-07-14 · 首次开源发布

### 🎉 里程碑

`LaunchInPlaceReload` v1.0.0 正式开源发布。这是 UMM 模组家族第三个对外开源的成员（继 LaunchMultiplayerNet、LaunchInventoryTidy 之后）。

### ✨ 新增功能

#### 功能 A：整理后合并弹匣（软依赖 LaunchInventoryTidy）

- **触发方式**：`ManualTidyService.TidyAllPlayerPages` Postfix 自动追加
- **实现机制**：通过 `[BepInDependency("...launchinventorytidy", SoftDependency)]` 声明软依赖
- **运行时检测**：`AccessTools.TypeByName("LaunchInventoryTidy.ManualTidyService")` 运行时探测
  - 已安装：Postfix 自动调用 `AmmoRepackService.MergeSameIdMagazines`
  - 未安装：跳过功能 A 附属，不影响功能 B 独立运行
- **业务逻辑**：合并同 ID 弹匣的子弹到最满的那个弹匣，仅处理含 `FillTargetItem` 蓝图的弹匣

#### 功能 B：一键压弹（独立热键）

- **触发方式**：双击 R 键（Unturned 原生换弹键，0.3 秒内双击）
- **压弹算法**：`AmmoRepackService.RepackFromAmmoBoxes`
  - 主路径：蓝图关联匹配（`FillTargetItem` -> `supplies` ID 列表）
  - Fallback：`calibers` 数组交集匹配（工坊模组鲁棒性保障）
- **弹药源管理**：弹药箱 amount 归零时自动删除物品
- **作用范围**：page 2..6（SLOTS..PANTS），跳过装备槽/储物箱/区域

#### 被动 Patch：换弹原位替换

- **Patch 文件**：`Patches/UseableGunReceiveAttachMagazinePatch.cs`
- **行为**：换弹时旧弹匣放回背包而非丢弃
- **价值**：避免误丢高价值弹匣（如工坊稀有弹匣）

### 🌐 双端自适应联机

- **通道**：`LaunchMultiplayerNet.ModChannels.RepackAmmo = 101`（独占）
- **子消息**：`EModMessage.RequestRepackAmmo = 10`（客机 -> 服务器）
- **协议**：
  ```
  客机双击 R 键 -> SendRepackRequest (Channel 101)
      -> 服务器反查 CSteamID -> Player
          -> AmmoRepackService.RepackFromAmmoBoxes(player)
              -> 原生网络同步自动推送回所有客机
                  -> 客机端 RepackToast 显示压弹结果
  ```
- **安全性**：服务器只对 sender 自己的 `Player.inventory` 操作，不会越权修改他人背包

### 🎯 三大铁规

1. **检测到蓝图自动填入，无蓝图直接跳过**（仅处理含 `FillTargetItem` 蓝图的弹匣）
2. **仅处理背包内容**（page 2..6 = SLOTS..PANTS），跳过装备槽/储物箱/区域
3. **功能 A 是一键整理附属；功能 B 是本模组独立热键**

### 🖥️ UI 集成

- **Toast 提示**：采用原生 `EPlayerMessage.NPC_CUSTOM`，不拦截原版 UI
- **显示时长**：2.5 秒
- **富文本格式**：绿色提示文本，支持多行压弹结果汇报

### 📦 工程结构

```
LaunchInPlaceReload/
├── LaunchInPlaceReload.csproj            # .NET Framework 4.7.2 库工程
├── LaunchInPlaceReloadPlugin.cs          # BepInEx 插件入口 + 双击 R 键检测
├── AmmoRepackService.cs                  # 压弹服务核心
├── AmmoRepackNetwork.cs                  # P2P 网络层：Channel 101
├── P2PAmmoManager.cs                     # P2P 弹药管理辅助
├── ManualRepackWatcher.cs                # 手动压弹监听器
├── RepackToast.cs                        # 屏幕提示器
├── Patches/
│   ├── UseableGunReceiveAttachMagazinePatch.cs  # 换弹原位替换
│   ├── ForceAddItemPatch.cs                     # 物品强制添加 Patch
│   └── TidyServicePostfixPatch.cs               # 软依赖 LaunchInventoryTidy
└── Properties/AssemblyInfo.cs            # v1.0.0.0
```

### 🤝 联动关系

- **前置依赖**：`LaunchMultiplayerNet` v1.1.1+（Channel 101 通信基建）
- **软依赖**：`LaunchInventoryTidy`（可选，启用功能 A 整理后合并弹匣）
- **兄弟模组**：`LaunchHordeTracker`（Channel 102，无通道冲突）

### 📜 协议

- **MIT License** 开源
- 部署形态：独立 `LaunchInPlaceReload.dll` + `LaunchMultiplayerNet.dll` 放入 `BepInEx/plugins/`

---

## 版本号规则

遵循 [SemVer](https://semver.org/lang/zh-CN/)：

- **v0.x**：内部测试版（未开源）
- **v1.0**：首个正式开源版
- **v1.x**：小更新（功能增强、Bug 修复）
- **v2.0**：大更新（架构变更、不向下兼容）
- **前置库**（LaunchMultiplayerNet）：不带版本后缀，裸名 `LaunchMultiplayerNet.dll`
