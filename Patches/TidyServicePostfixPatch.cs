using HarmonyLib;
using SDG.Unturned;

namespace LaunchInPlaceReload.Patches
{
    /// <summary>
    /// 软依赖 LaunchInventoryTidy：在一键整理完成后追加"同 ID 弹匣子弹合并"（功能 A）。
    ///
    /// 通过 Harmony Postfix 拦截 ManualTidyService.TidyAllPlayerPages(PlayerInventory, bool)，
    /// 整理完毕后调 AmmoRepackService.MergeSameIdMagazines(inv.player)。
    ///
    /// 若 LaunchInventoryTidy 未安装，本 patch 不会注册（TypeByName 返回 null 时跳过）。
    /// </summary>
    internal static class TidyServicePostfixPatch
    {
        /// <summary>由 Plugin.Awake 调用，软依赖注册 postfix。</summary>
        public static void TryRegister(Harmony harmony)
        {
            var tidyServiceType = AccessTools.TypeByName("LaunchInventoryTidy.ManualTidyService");
            if (tidyServiceType == null)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                    "[TidyHook] LaunchInventoryTidy 未安装，跳过功能 A 附属");
                return;
            }

            var targetMethod = AccessTools.Method(tidyServiceType, "TidyAllPlayerPages",
                new[] { typeof(PlayerInventory), typeof(bool) });
            if (targetMethod == null)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                    "[TidyHook] 找不到 ManualTidyService.TidyAllPlayerPages 方法");
                return;
            }

            var postfix = new HarmonyMethod(typeof(TidyServicePostfixPatch), nameof(Postfix_TidyAllPlayerPages));
            harmony.Patch(targetMethod, postfix: postfix);

            LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                "[TidyHook] 已附加 LaunchInventoryTidy 整理后缀 patch（功能 A 附属）");
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
            AmmoRepackService.MergeSameIdMagazines(player);
        }
    }
}
