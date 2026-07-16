# 🔫 LaunchInPlaceReload

> Unturned 一键压弹模组 · BepInEx 5 插件
> 双击 R 键从弹药箱给未满弹匣压弹，换弹时旧弹匣原位回背包，与 LaunchInventoryTidy 软联动实现"整理后自动合并同 ID 弹匣"。
>
> **当前版本：v2.0.0（2026-07-16 重构版）** · 单机 + 联机（dedicated server）双适配 · 仅依赖 LaunchMultiplayerNet 前置网络层

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.7.2-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Unturned](https://img.shields.io/badge/Unturned-3.x-59B200?logo=steam)](https://store.steampowered.com/app/304930/Unturned/)
[![BepInEx](https://img.shields.io/badge/BepInEx-5-FF7B00?logo=nuget)](https://github.com/BepInEx/BepInEx)
[![Harmony](https://img.shields.io/badge/Harmony-2-blue)](https://github.com/pardeike/Harmony)
[![Version](https://img.shields.io/badge/version-2.0.0-blue)](./CHANGELOG.md)

🌐 **开源仓库**：[github.com/YU80Rice/LaunchInPlaceReload](https://github.com/YU80Rice/LaunchInPlaceReload)

---

## 🆕 v2.0.0 重构声明

v2.0.0 是一次大版本更新，专注于**架构鲁棒性**与**双端适配正确性**。v1.x 系列在 BepInEx 5.4.22 + LaunchInventoryTidy v1.4 环境下存在致命缺陷：`TidyHook` 用 `AccessTools.Method` 不指定参数类型查找 `TidyAllPlayerPages`，但 LaunchInventoryTidy v1.4 同时存在 3 参 + 2 参两个重载，触发 `AmbiguousMatchException`，BepInEx 5.4.22 静默吞掉 Awake 异常，导致整个插件完全不初始化（双击 R 与 TidyHook 同时失效）。

### 重构核心

| 改动 | v1.x | v2.0.0 |
|---|---|---|
| TidyHook 方法查找 | `AccessTools.Method(type, name)` 遇多重载抛 `AmbiguousMatchException` | `GetMethods(AccessTools.all).Where(name==...).FirstOrDefault()` 反射枚举，优先选首个参数为 `PlayerInventory` 的版本 |
| Awake 错误隔离 | 4 子步骤无 try-catch，任一失败吞掉整个 Awake | PatchAll / TidyHook / DontDestroyOnLoad / 网络层 4 子步骤各自独立 try-catch |
| LaunchMultiplayerNet 依赖 | 隐式（靠 BepInEx 字母序加载） | 显式 `[BepInDependency(LaunchMultiplayerNetPlugin.Guid, HardDependency)]` |
| 联机压弹 toast | 服务器端 `if (Provider.isServer) RepackToast.Show` 在 U3DS headless 环境永不显示 | 新增服务器 -> 客机 `RepackSuccess` 回包（byte 11 + int32 totalTransferred），由发起方客户端本地显示 toast |
| 单机 + 联机双路径 | 双分支但联机路径不通 | 房主 `Provider.isServer=true` 本地执行 + 本地 toast；客机走网络 -> U3DS 执行 -> 回包 -> 客户端 toast |

### 单机 + 联机双适配声明

| 部署场景 | 行为 |
|---|---|
| **单机 Unturned**（无 U3DS） | `Provider.isServer=true`，双击 R 本地执行压弹 + 本地 toast |
| **dedicated server 联机**（U3DS） | 客户端 `Provider.isServer=false`，双击 R 发请求到 U3DS，U3DS 执行 + 回包，客户端显示 toast |
| **BepInEx 部署** | 只要把 `LaunchInPlaceReload.dll` + `LaunchMultiplayerNet.dll` 放入 `BepInEx/plugins/` 即可，无需启动器 |

### 与前置模组的关系

- **LaunchMultiplayerNet**（硬依赖）：提供 Channel 101 网络通信基建，**必装**。仓库：[github.com/YU80Rice/LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet)
- **LaunchInventoryTidy**（软依赖）：整理后自动合并同 ID 弹匣（功能 A 附属），**可选但强烈推荐**。仓库：[github.com/YU80Rice/LaunchInventoryTidy](https://github.com/YU80Rice/LaunchInventoryTidy)

完整更新日志见 [CHANGELOG.md](./CHANGELOG.md)。

---

## 📌 前置与联动声明

本插件是 [UMM 模组家族](https://github.com/YU80Rice/UnturnedModManager) 的成员之一。**特别注意：本插件与 [LaunchInventoryTidy](https://github.com/YU80Rice/LaunchInventoryTidy) 存在软依赖关系**——两者搭配使用可获得"整理后自动合并弹匣"的增强体验，但本插件也可独立运行。

### 🔧 前置依赖

| 依赖 | 版本 | 仓库 | 用途 |
|---|---|---|---|
| **LaunchMultiplayerNet** | **v3.2+**（硬依赖） | [github.com/YU80Rice/LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet) | 双端自适应网络传输层（本插件独占 Channel 101，使用 `ITransportConnection + MOD magic` Harmony patch 架构） |
| BepInEx | 5.x | [github.com/BepInEx/BepInEx](https://github.com/BepInEx/BepInEx) | 模组加载器 |
| Harmony | 2.x | [github.com/pardeike/Harmony](https://github.com/pardeike/Harmony) | 运行时方法注入 |
| Unturned | 3.x | [store.steampowered.com/app/304930](https://store.steampowered.com/app/304930/Unturned/) | 宿主游戏 |
| Steamworks.NET | - | [github.com/rlabrecque/Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) | P2P 传输底层 |
| SDG.Glazier.Runtime | - | 随 Unturned 分发 | UI 渲染（Toast 提示） |
| UnityEngine.TextRenderingModule | - | 随 Unturned 分发 | Toast 文字渲染 |

### 🌐 联动项目矩阵

| 项目 | 角色 | 仓库 | 与本插件的关系 |
|---|---|---|---|
| **Unturned Mod Manager** | 宿主启动器（可选，推荐） | [github.com/YU80Rice/UnturnedModManager](https://github.com/YU80Rice/UnturnedModManager) | 一键部署 BepInEx 核心 + 性能优化模组 + DXVK 优化；**本插件与前置库需手动放入 plugins/** |
| **LaunchMultiplayerNet** | 前置库（必装） | [github.com/YU80Rice/LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet) | 提供 Channel 101 通信基建 |
| **LaunchInventoryTidy** | **软依赖**（可选，强烈推荐） | [github.com/YU80Rice/LaunchInventoryTidy](https://github.com/YU80Rice/LaunchInventoryTidy) | 整理后自动合并同 ID 弹匣（功能 A） |
| LaunchHordeTracker | 兄弟模组 | [github.com/YU80Rice/LaunchHordeTracker](https://github.com/YU80Rice/LaunchHordeTracker) | 占用 Channel 102，与本插件无通道冲突 |

### 🤝 与 LaunchInventoryTidy 的软依赖关系

本插件通过 BepInEx 的 `[BepInDependency]` 机制声明双依赖：**LaunchMultiplayerNet 硬依赖**（必装）+ **LaunchInventoryTidy 软依赖**（可选）：

```csharp
[BepInPlugin("com.yu80rice.launchinplacereload", "LaunchInPlaceReload [v2.0.0 重构 / 双端适配]", "2.0.0")]
[BepInDependency(LaunchMultiplayerNetPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.yu80rice.launchinventorytidy", BepInDependency.DependencyFlags.SoftDependency)]
public class LaunchInPlaceReloadPlugin : BaseUnityPlugin { ... }
```

**运行时行为**：

| LaunchInventoryTidy 是否安装 | 本插件行为 |
|---|---|
| ✅ 已安装 | 整理完毕后自动调用 `AmmoRepackService.MergeSameIdMagazines`，合并同 ID 弹匣的子弹（功能 A 附属） |
| ❌ 未安装 | 跳过功能 A 附属，仅保留功能 B（双击 R 键独立压弹），不影响本插件独立运行 |

**软依赖实现机制**（`Patches/TidyServicePostfixPatch.cs`，v2.0.0 重构）：

```csharp
// v2.0.0：反射枚举所有 TidyAllPlayerPages 重载，优先选首个参数为 PlayerInventory 的版本
// 兼容 LaunchInventoryTidy v1.0~v1.3（2 参）+ v1.4+（3 参 + 2 参共存）
var candidates = tidyServiceType.GetMethods(AccessTools.all)
    .Where(m => m.Name == "TidyAllPlayerPages")
    .ToList();
var targetMethod = candidates.FirstOrDefault(m =>
    m.GetParameters().Length >= 1 &&
    m.GetParameters()[0].ParameterType == typeof(PlayerInventory));
// Postfix 通过参数名 inv 注入，与原方法参数数量无关
harmony.Patch(targetMethod, postfix: new HarmonyMethod(...Postfix_TidyAllPlayerPages));
```

> ⚠️ **v1.x 教训**：之前用 `AccessTools.Method(type, "TidyAllPlayerPages")` 不指定参数类型，在 LaunchInventoryTidy v1.4（同时有 3 参 + 2 参重载）下抛 `AmbiguousMatchException`，BepInEx 5.4.22 静默吞掉整个 Awake，导致插件完全不初始化。v2.0.0 改用反射枚举 + 错误隔离根除此问题。

### 📡 通道占用声明

本插件在 `LaunchMultiplayerNet.ModChannels` 中**独占 Channel 101**：

```csharp
public const int RepackAmmo = 101;  // ← 本插件独占
```

子消息类型（`EModMessage` + 本插件内部 byte 约定）：

| 子消息 | 值 | 方向 | 用途 |
|---|---|---|---|
| `RequestRepackAmmo`（`EModMessage`） | `10` | 客机 -> 服务器 | 请求执行全背包压弹（无业务字段） |
| `REPACK_SUCCESS`（本插件内部 byte 约定） | `11` | 服务器 -> 客机 | 压弹完成回包（含 int32 totalTransferred），由发起方客户端本地显示 toast |

> 💡 **设计说明**：`REPACK_SUCCESS = 11` 不写入 `LaunchMultiplayerNet.EModMessage` 枚举，避免对前置库做任何修改。数值 11 与 EModMessage 现有值（10/20/21）不冲突。

**通道分配规则**：其他模组请从 Channel 103 起分配，详见 [LaunchMultiplayerNet 仓库的 `ModChannels.cs`](https://github.com/YU80Rice/LaunchMultiplayerNet/blob/main/ModChannels.cs)。

### 💡 部署路径

```
<Unturned 游戏目录>/
└── BepInEx/
    └── plugins/
        ├── LaunchMultiplayerNet.dll       ← 前置库（必装）
        ├── LaunchInPlaceReload.dll         ← 本插件
        ├── LaunchInventoryTidy.dll         ← 软依赖（可选，推荐安装以启用功能 A）
        └── LaunchHordeTracker.dll          ← 兄弟模组（可选）
```

> 💡 **独立部署说明**：即使没有 [UMM 启动器](https://github.com/YU80Rice/UnturnedModManager)，只要玩家本地已有现成的 **BepInEx 5** 环境，也可以直接把 `LaunchInPlaceReload.dll` + `LaunchMultiplayerNet.dll` 放入 `BepInEx/plugins/` 即可使用。
>
> ⚠️ **关于 UMM 启动器的自动部署范围**：UMM 启动器**仅自动部署 BepInEx 核心与 2 个性能优化模组**（`WaterPerfOptimizer.dll` / `LaunchPerfOptimizer.dll`）。**本插件 `LaunchInPlaceReload.dll`、前置库 `LaunchMultiplayerNet.dll` 与软依赖 `LaunchInventoryTidy.dll` 均不在自动部署清单内**，需用户手动放入 `BepInEx/plugins/`。

---

## 📖 项目简介

`LaunchInPlaceReload` 是 [UMM 模组家族](https://github.com/YU80Rice/UnturnedModManager) 的成员之一，为 Unturned 玩家提供智能弹药管理能力。模组通过双击 R 键触发一键压弹，从背包内的弹药箱给所有未满弹匣自动压弹，并在换弹时把旧弹匣原位回背包而非丢弃。

### ✨ 核心功能

| 功能 | 触发 | 行为 |
|---|---|---|
| **功能 A：整理后合并弹匣** | LaunchInventoryTidy 整理完毕后（Postfix 自动触发） | 合并同 ID 弹匣的子弹到最满的那个弹匣，仅处理含 `FillTargetItem` 蓝图的弹匣 |
| **功能 B：一键压弹** | 双击 R 键（Unturned 原生换弹键，0.3 秒内双击） | 从弹药箱给所有未满弹匣压弹，弹药源 amount 归零时自动删除 |
| **被动：换弹原位替换** | 换弹时自动触发（Harmony Patch） | 旧弹匣放回背包而非丢弃 |

### 🧠 压弹匹配算法

`AmmoRepackService.RepackFromAmmoBoxes` 实现了**蓝图关联匹配**，对工坊模组鲁棒：

- **主路径**：对每个未满弹匣，遍历其所有 `FillTargetItem` 蓝图，收集所有 `supplies` 的物品 ID 作为"兼容弹药源 ID 列表"。在背包中查找 `asset.id` 命中该列表的物品即视为兼容。
- **Fallback**：若弹匣无任何 `FillTargetItem` 蓝图，且弹匣自身有 `calibers` 数组，则在背包内查找同为 `ItemCaliberAsset` 且 `calibers` 数组有交集的物品。

### 🌐 双端自适应联机

基于 [LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet) 前置库，使用 **Channel 101 (RepackAmmo)** 通信：

```
客机双击 R 键
    └─> SendRepackRequest() (Channel 101)
            └─> 服务器收到包
                    └─> 反查 sender CSteamID -> Player
                            └─> AmmoRepackService.RepackFromAmmoBoxes(player)
                                    └─> Items.removeItem/addItem + sendUpdateAmount
                                            └─> 原生网络同步自动推送回所有客机
                                                    └─> 客机端 RepackToast 显示压弹结果
```

**安全性**：服务器只对 sender 自己的 `Player.inventory` 操作，不会越权修改他人背包。

### 🎯 三大铁规

1. **检测到蓝图自动填入，无蓝图直接跳过**（仅处理含 `FillTargetItem` 蓝图的弹匣）
2. **仅处理背包内容**（page 2..6 = SLOTS..PANTS），跳过装备槽/储物箱/区域
3. **功能 A 是一键整理附属；功能 B 是本模组独立热键**

---

## 📦 项目结构

```
LaunchInPlaceReload/
├── LaunchInPlaceReload.csproj            # .NET Framework 4.7.2 库工程
├── LaunchInPlaceReloadPlugin.cs          # BepInEx 插件入口 + 双击 R 键检测
├── AmmoRepackService.cs                  # 压弹服务核心：MergeSameIdMagazines + RepackFromAmmoBoxes
├── AmmoRepackNetwork.cs                  # P2P 网络层：Channel 101 协议
├── P2PAmmoManager.cs                     # P2P 弹药管理辅助
├── ManualRepackWatcher.cs                # 手动压弹监听器
├── RepackToast.cs                        # 屏幕提示器（EPlayerMessage.NPC_CUSTOM）
├── Patches/
│   ├── UseableGunReceiveAttachMagazinePatch.cs  # 换弹原位替换
│   ├── ForceAddItemPatch.cs                      # 物品强制添加 Patch
│   └── TidyServicePostfixPatch.cs                # 软依赖 LaunchInventoryTidy 的 Postfix
├── Properties/AssemblyInfo.cs            # 程序集元数据 v2.0.0.0
├── LICENSE                               # MIT
├── README.md                             # 本文件
├── CHANGELOG.md                          # 版本演进
└── CONTRIBUTING.md                       # Vibecoding 声明与致谢
```

---

## 🔧 构建要求

- **目标框架**：.NET Framework 4.7.2（与 Unturned 游戏一致）
- **IDE**：Visual Studio 2019 / 2022 / JetBrains Rider
- **依赖 DLL**（不随源码分发，需自行从 Unturned 游戏目录或对应仓库提取）：

| DLL | 来源 |
|---|---|
| `Assembly-CSharp.dll` | Unturned 游戏目录 `/Unturned_Data/Managed/` |
| `UnityEngine.dll` | Unturned 游戏目录 `/Unturned_Data/Managed/` |
| `UnityEngine.CoreModule.dll` | Unturned 游戏目录 `/Unturned_Data/Managed/` |
| `UnityEngine.TextRenderingModule.dll` | Unturned 游戏目录 `/Unturned_Data/Managed/` |
| `SDG.Glazier.Runtime.dll` | Unturned 游戏目录 `/Unturned_Data/Managed/` |
| `com.rlabrecque.steamworks.net.dll` | [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) |
| `BepInEx.dll` | [BepInEx 5](https://github.com/BepInEx/BepInEx) |
| `0Harmony.dll` | [Harmony 2](https://github.com/pardeike/Harmony) |
| `LaunchMultiplayerNet.dll` | [LaunchMultiplayerNet](https://github.com/YU80Rice/LaunchMultiplayerNet) 仓库自行编译 |

### 📂 Libs 目录配置

本工程的 `.csproj` 中 `<HintPath>` 默认指向 `..\Libs\*.dll`，即与本仓库**同级目录**的 `Libs/` 文件夹：

```
some-folder/
├── LaunchInPlaceReload/         ← 本仓库
└── Libs/                       ← 把上面 9 个 DLL 放在这里
    ├── Assembly-CSharp.dll
    ├── UnityEngine.dll
    ├── UnityEngine.CoreModule.dll
    ├── UnityEngine.TextRenderingModule.dll
    ├── SDG.Glazier.Runtime.dll
    ├── com.rlabrecque.steamworks.net.dll
    ├── BepInEx.dll
    ├── 0Harmony.dll
    └── LaunchMultiplayerNet.dll
```

> 如果你的目录结构不同，请修改 `.csproj` 中 `<HintPath>` 节点的相对路径。

---

## 🚀 使用方式

### 玩家侧

1. **部署**：把编译好的 `LaunchInPlaceReload.dll` **和** `LaunchMultiplayerNet.dll`（前置库）一起放入 `<游戏目录>/BepInEx/plugins/`
   - **UMM 启动器用户**：启动器仅自动部署 BepInEx 核心与性能优化模组（`WaterPerfOptimizer.dll` / `LaunchPerfOptimizer.dll`），**本插件与 `LaunchMultiplayerNet.dll` 不在自动部署清单内，仍需手动放入 `BepInEx/plugins/`**
   - **独立 BepInEx 用户**：同样需要手动放入上述两个 DLL
   - **（可选）安装 LaunchInventoryTidy**：若同目录存在 `LaunchInventoryTidy.dll`，则自动启用功能 A（整理后合并弹匣）；不安装也不影响功能 B 独立运行
2. **触发压弹**：游戏中**双击 R 键**（Unturned 原生换弹键，0.3 秒内双击）-> 自动从背包内弹药箱给所有未满弹匣压弹
3. **观察提示**：压弹完成时屏幕底部上方显示绿色富文本提示（采用原生 `EPlayerMessage.NPC_CUSTOM`，不拦截原版 UI）

### 房主 / 客机行为

| 角色 | 行为 |
|---|---|
| **房主**（`Provider.isServer=true`） | 直接在本地 `PlayerInventory` 上执行 `RepackFromAmmoBoxes` |
| **客机**（`Provider.isServer=false`） | 通过 P2P 通道 101 发送请求，服务器代为执行，原生事件链自动同步回所有客机 |

### 与 LaunchInventoryTidy 协同使用

| 场景 | 行为 |
|---|---|
| 同时安装两个模组 | LaunchInventoryTidy 整理完毕后，LaunchInPlaceReload 自动追加 `MergeSameIdMagazines` 合并同 ID 弹匣 |
| 仅安装 LaunchInPlaceReload | 双击 R 键独立压弹（功能 B），换弹原位替换（被动 Patch）正常工作 |
| 仅安装 LaunchInventoryTidy | 一键整理正常工作，但整理后不会合并弹匣（功能 A 附属需要本插件存在） |

---

## 📜 版本协议

- **MIT 协议开源**：自由使用、修改、分发、商业利用
- **《未转变者》(Unturned) 版权归 Smartly Dressed Games 所有**
- 本模组仅为玩家社区的非官方辅助工具，不包含任何游戏资产，不修改游戏可执行文件
- 所有 BepInEx 插件均以 `.dll` 独立文件形式部署，可随时通过 `.disabled` 后缀停用或物理删除

---

## 🤝 致谢

本项目延续了 [UMM 主仓库](https://github.com/YU80Rice/UnturnedModManager) 的 Vibecoding 协作范式与致谢体系。

🙏 **完整 Vibecoding 声明与六位关键贡献者致谢**：详见 [CONTRIBUTING.md](./CONTRIBUTING.md)
