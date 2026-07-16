# 📜 Changelog

本文件记录 `LaunchInPlaceReload` 的版本演进历程。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

---

## [2.0.0] - 2026-07-16 · 大版本重构（Awake 错误隔离 + TidyHook 多签名自适应 + 联机 toast 修复）

### 🎯 重构动机

v1.x 系列在 BepInEx 5.4.22 + LaunchInventoryTidy v1.4 环境下存在致命缺陷：插件完全不初始化，双击 R 与 TidyHook 同时失效。

**根因**：`TidyServicePostfixPatch.TryRegister` 调用 `AccessTools.Method(type, "TidyAllPlayerPages")` 不指定参数类型，但 LaunchInventoryTidy v1.4 同时存在两个重载（3 参 `(PlayerInventory, bool, TidyMode)` + 2 参 `(bool, TidyMode)`），AccessTools 找到多个匹配抛 `AmbiguousMatchException`。BepInEx 5.4.22 静默吞掉 plugin Awake 异常（LogOutput.log 不打印），Awake 中途终止，`ModTransport.Initialize` / `AmmoRepackNetwork.RegisterHandlers` 全部未执行。诊断关键：查 Player.log 而非 LogOutput.log 才能看到完整堆栈。

### ✨ 主要改动

#### 1. TidyHook 多签名自适应

- **v1.x**：`AccessTools.Method(tidyServiceType, "TidyAllPlayerPages")` 不指定参数类型，遇多重载抛 `AmbiguousMatchException`
- **v2.0.0**：改用 `tidyServiceType.GetMethods(AccessTools.all).Where(m => m.Name == "TidyAllPlayerPages").FirstOrDefault(...)` 反射枚举所有重载，优先选首个参数为 `PlayerInventory` 的版本
- **兼容性**：自动适配 LaunchInventoryTidy v1.0~v1.3（2 参 `(PlayerInventory, bool)`）和 v1.4+（3 参 `(PlayerInventory, bool, TidyMode)` + 2 参 `(bool, TidyMode)` 共存）
- **可观测性**：`DescribeMethod` 日志输出实际 patch 的签名，便于未来签名变更时定位

#### 2. Awake 错误隔离架构

v1.x 的 Awake 4 个子步骤无 try-catch，任一失败吞掉整个 Awake。v2.0.0 重构为各自独立 try-catch：

```csharp
private void Awake()
{
    Instance = this;
    try { /* 步骤 1：Harmony PatchAll */ } catch (Exception e) { Logger.LogError(...); }
    try { /* 步骤 2：TidyServicePostfixPatch.TryRegister */ } catch (Exception e) { Logger.LogError(...); }
    try { /* 步骤 3：DontDestroyOnLoad */ } catch (Exception e) { Logger.LogError(...); }
    try { /* 步骤 4：ModTransport.Initialize + AmmoRepackNetwork.RegisterHandlers */ } catch (Exception e) { Logger.LogError(...); }
    Logger.LogInfo("LaunchInPlaceReload v2.0.0 已加载");
}
```

任一子步骤失败只 LogError，不阻止其他子步骤。例如 TidyHook 失败时，功能 B（双击 R 压弹）仍能正常工作。

Postfix 内 `MergeSameIdMagazines` 也加 try-catch，防止整理过程异常污染 LaunchInventoryTidy 主流程。

#### 3. 显式 LaunchMultiplayerNet 硬依赖

- **v1.x**：未声明 `[BepInDependency(LaunchMultiplayerNetPlugin.Guid)]`，靠 BepInEx 字母序加载（`LaunchMultiplayerNet` < `LaunchInPlaceReload` 碰巧对）
- **v2.0.0**：显式 `[BepInDependency(LaunchMultiplayerNetPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]`，避免未来重命名导致的加载顺序问题

#### 4. 联机压弹 toast 修复

- **v1.1.x 问题**：`RepackFromAmmoBoxes` 末尾 `if (Provider.isServer) RepackToast.Show`，但 U3DS 是 headless 服务器无 `PlayerUI`，toast 永不显示；发起方客机也没收到任何回包
- **v2.0.0 修复**：
  - `RepackFromAmmoBoxes` 改返回 `int totalTransferred`，去掉本地 toast 调用
  - 新增服务器 -> 客机 `REPACK_SUCCESS = 11` 回包（byte 标识 + int32 totalTransferred，本插件内部约定不写入 LaunchMultiplayerNet 的 EModMessage 枚举）
  - U3DS 执行完压弹后通过 `ModTransport.SendToClient(sender, ...)` 回发
  - 发起方客户端 `HandleRepackSuccessFromServer` 收到后本地调 `RepackToast.Show`

#### 5. 单机 + 联机双路径

| 部署场景 | 行为 |
|---|---|
| 单机 Unturned（`Provider.isServer=true`） | Plugin.Update 双击检测 -> 本地 `RepackFromAmmoBoxes` -> 本地 `RepackToast.Show` |
| dedicated server 联机（`Provider.isServer=false`） | Plugin.Update 双击检测 -> `AmmoRepackNetwork.SendRepackRequest` -> U3DS 执行 -> `SendToClient` 回包 -> 客户端 `HandleRepackSuccessFromServer` -> `RepackToast.Show` |

### 🔧 BepInEx 5.4.22 静默吞 Awake 异常陷阱

本次重构发现并记录了一个跨插件通用的陷阱：

- **症状**：plugin 在 BepInEx Chainloader 加载时只输出 `[BepInEx] Loading [PluginName]` 横幅，没有任何 plugin 自身日志（既无 Info 也无 Error/Warning），plugin 完全不工作
- **根因**：BepInEx 5.4.22 调用 plugin.Awake 时如果抛异常，LogOutput.log 不打印异常信息，但 Unity 的 `Application.LogExceptionException` 会完整写入 Player.log
- **诊断方法**：查 Player.log（`%USERPROFILE%/AppData/LocalLow/Smartly Dressed Games/Unturned/Player.log`），grep `Exception|Error|MissingMethod|TypeLoad|Harmony` 即可定位
- **通用教训**：plugin Awake 必须每个子步骤独立 try-catch；`AccessTools.Method` 不指定参数类型遇多重载抛 `AmbiguousMatchException`（不是返回 null）

### 📦 部署变更

- **单文件部署**：v2.0.0 不修改 LaunchMultiplayerNet，仅升级 `LaunchInPlaceReload.dll` 单文件即可
- **前置库版本要求**：LaunchMultiplayerNet v3.2+（`ITransportConnection + MOD magic` 架构）
- **兼容性**：保留 `EModMessage.RequestRepackAmmo = 10` 协议字段，v1.x 客户端可与 v2.0.0 服务器互通（但 v1.x 客户端无 toast 显示）

### 🎯 三大铁规（保持不变）

1. **检测到蓝图自动填入，无蓝图直接跳过**（仅处理含 `FillTargetItem` 蓝图的弹匣）
2. **仅处理背包内容**（page 2..6 = SLOTS..PANTS），跳过装备槽/储物箱/区域
3. **功能 A 是一键整理附属；功能 B 是本模组独立热键**

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
