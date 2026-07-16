using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SDG.Unturned;

namespace LaunchInPlaceReload.Patches
{
    /// <summary>
    /// 软依赖 LaunchInventoryTidy：在一键整理完成后追加"同 ID 弹匣子弹合并"（功能 A）。
    ///
    /// 通过 Harmony Postfix 拦截 ManualTidyService.TidyAllPlayerPages，
    /// 整理完毕后调 AmmoRepackService.MergeSameIdMagazines(inv.player)。
    ///
    /// v2.0.0 重构：自适应 LaunchInventoryTidy 多版本签名
    /// - v1.0~v1.3：2 参 (PlayerInventory, bool)
    /// - v1.4+：3 参 (PlayerInventory, bool, TidyMode) + 2 参 (bool, TidyMode) 重载共存
    ///   -> AccessTools.Method 不指定参数会抛 AmbiguousMatchException，Awake 整个崩
    /// - 解决：枚举所有 TidyAllPlayerPages 重载，优先选首个参数为 PlayerInventory 的版本
    ///
    /// 若 LaunchInventoryTidy 未安装，本 patch 不会注册（TypeByName 返回 null 时跳过）。
    /// 所有查找失败均只警告不抛异常，确保主功能 B 不受影响。
    /// </summary>
    internal static class TidyServicePostfixPatch
    {
        /// <summary>由 Plugin.Awake 调用，软依赖注册 postfix。任何异常被吞掉，仅警告。</summary>
        public static void TryRegister(Harmony harmony)
        {
            try
            {
                var tidyServiceType = AccessTools.TypeByName("LaunchInventoryTidy.ManualTidyService");
                if (tidyServiceType == null)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                        "[TidyHook] LaunchInventoryTidy 未安装，跳过功能 A 附属");
                    return;
                }

                var targetMethod = ResolveTidyAllPlayerPages(tidyServiceType);
                if (targetMethod == null)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                        "[TidyHook] 找不到 ManualTidyService.TidyAllPlayerPages(PlayerInventory, ...) 方法");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(TidyServicePostfixPatch), nameof(Postfix_TidyAllPlayerPages));
                harmony.Patch(targetMethod, postfix: postfix);

                LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                    $"[TidyHook] 已附加 LaunchInventoryTidy 整理后缀 patch（功能 A 附属），目标签名: {DescribeMethod(targetMethod)}");
            }
            catch (Exception e)
            {
                // 错误隔离：TidyHook 失败不能影响功能 B（双击压弹）
                LaunchInPlaceReloadPlugin.Instance?.LogError(
                    $"[TidyHook] TryRegister 异常（已吞掉，不影响功能 B）: {e}");
            }
        }

        /// <summary>
        /// 枚举 ManualTidyService.TidyAllPlayerPages 所有公开重载，
        /// 优先选首个参数为 PlayerInventory 的版本（v1.0~v1.4 全兼容）。
        /// </summary>
        private static MethodInfo ResolveTidyAllPlayerPages(Type tidyServiceType)
        {
            // AccessTools.all = BindingFlags.Public | NonPublic | Static | Instance | FlattenHierarchy
            var candidates = tidyServiceType.GetMethods(AccessTools.all)
                .Where(m => m.Name == "TidyAllPlayerPages")
                .ToList();

            if (candidates.Count == 0) return null;

            // 优先选首个参数为 PlayerInventory 的重载
            var preferred = candidates.FirstOrDefault(m =>
                m.GetParameters().Length >= 1 &&
                m.GetParameters()[0].ParameterType == typeof(PlayerInventory));
            return preferred ?? candidates[0];
        }

        private static string DescribeMethod(MethodInfo m)
        {
            var ps = m.GetParameters().Select(p => p.ParameterType.Name).ToArray();
            return $"{m.DeclaringType?.Name}.{m.Name}({string.Join(", ", ps)})";
        }

        /// <summary>
        /// Postfix：从 PlayerInventory.player 反查 Player，调 MergeSameIdMagazines。
        /// 参数名 inv 必须与原方法一致，Harmony 会按名注入。
        /// </summary>
        private static void Postfix_TidyAllPlayerPages(PlayerInventory inv)
        {
            if (inv == null) return;
            Player player = inv.player;
            if (player == null) return;
            try
            {
                AmmoRepackService.MergeSameIdMagazines(player);
            }
            catch (Exception e)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogError(
                    $"[TidyHook] Postfix MergeSameIdMagazines 异常: {e}");
            }
        }
    }
}
