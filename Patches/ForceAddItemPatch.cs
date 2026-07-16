using HarmonyLib;
using SDG.Unturned;

namespace LaunchInPlaceReload.Patches
{
    /// <summary>
    /// 拦截 PlayerInventory.forceAddItem(Item, bool) 双参版本。
    /// UseableGun.ReceiveAttachMagazine line 2918/2956 调此方法放回旧弹匣。
    ///
    /// 若处于换弹上下文且槽位有效：
    ///   1. 优先用记录的 rot 验证空间（旧弹匣尺寸能否放入新弹匣原位）
    ///   2. 若 rot 不行且非装备槽/非方形，尝试 90 度旋转
    ///   3. 若可放，直接 items[page].addItem(x, y, rot, item) 原位写入
    ///      （addItem 内部触发 onItemAdded/onStateUpdated，由 PlayerInventory 订阅做网络同步）
    ///   4. 返回 false 阻止原版 tryFindSpace / dropItem
    /// 否则放行原版逻辑。
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), "forceAddItem",
                  new[] { typeof(Item), typeof(bool) })]
    internal static class ForceAddItemPatch
    {
        static bool Prefix(PlayerInventory __instance, Item item)
        {
            if (item == null) return true;

            if (!P2PAmmoManager.TryConsumeSlot(
                    out byte page, out byte x, out byte y,
                    out byte slotRot, out byte slotSizeX, out byte slotSizeY))
            {
                return true;
            }

            ItemAsset asset = item.GetAsset();
            if (asset == null) return true;

            // 尺寸安全守门员：仅当旧弹匣（item）与新弹匣（slot）物理尺寸完全一致时，
            // 才执行 1to1 坐标原位替换；尺寸不一致则退化为原版 tryFindSpace 空间检测。
            if (asset.size_x != slotSizeX || asset.size_y != slotSizeY)
            {
                return true;
            }

            // 优先使用记录的新弹匣 rot
            byte rot = slotRot;
            bool fits = __instance.checkSpaceEmpty(page, x, y, asset.size_x, asset.size_y, rot);

            // 旋转尝试：仅背包页（page >= SLOTS）+ 非方形物品才尝试切换 rot
            if (!fits && page >= PlayerInventory.SLOTS && asset.size_x != asset.size_y)
            {
                rot = (byte)(slotRot == 0 ? 1 : 0);
                fits = __instance.checkSpaceEmpty(page, x, y, asset.size_x, asset.size_y, rot);
            }

            if (!fits) return true; // 放不下，放行原版让 tryFindSpace 找空位

            __instance.items[page].addItem(x, y, rot, item);

            // 弹匣非 weapon/useable/clothing，无需 auto-equip
            return false;
        }
    }
}
