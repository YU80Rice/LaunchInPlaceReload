using HarmonyLib;
using SDG.Unturned;

namespace LaunchInPlaceReload.Patches
{
    /// <summary>
    /// 在 UseableGun.ReceiveAttachMagazine 入口处记录新弹匣完整槽位 (page, x, y, rot, size_x, size_y)。
    /// 供 ForceAddItemPatch 原位放回旧弹匣。
    ///
    /// 调用链：客户端按 R -> SendAttachMagazine -> 服务端 ReceiveAttachMagazine：
    ///   line 2827-2835: 创建旧弹匣 item = new Item(...)
    ///   line 2840-2844: 取新弹匣 jar
    ///   line 2914: removeItem(page, index) 释放新弹匣位置
    ///   line 2918: forceAddItem(item, true) 放回旧弹匣  ← 我们拦截这里
    /// </summary>
    [HarmonyPatch(typeof(UseableGun), "ReceiveAttachMagazine")]
    internal static class UseableGunReceiveAttachMagazinePatch
    {
        static void Prefix(UseableGun __instance, byte page, byte x, byte y)
        {
            P2PAmmoManager.Reset();

            // page = 255 是 detach-only 分支（仅卸下当前弹匣，无替换目标），不记录
            if (page == 255) return;

            Player player = __instance.player;
            if (player == null) return;

            byte index = player.inventory.getIndex(page, x, y);
            if (index == 255) return;

            ItemJar jar = player.inventory.getItem(page, index);
            if (jar == null) return;

            P2PAmmoManager.BeginReload(page, x, y, jar.rot, jar.size_x, jar.size_y);
        }

        static void Postfix()
        {
            P2PAmmoManager.Reset();
        }
    }
}
