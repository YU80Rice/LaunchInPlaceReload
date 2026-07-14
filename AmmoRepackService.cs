using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace LaunchInPlaceReload
{
    /// <summary>
    /// 弹药压弹服务：把玩家背包内同类弹匣的子弹合并 + 从弹药箱给未满弹匣压弹。
    ///
    /// 双端适配网络同步路径：
    ///   - 服务器端调用：items.removeItem + items.addItem 触发 onItemRemoved/onItemAdded，
    ///     PlayerInventory 订阅这些事件做原生网络同步 -> 客机端自动收到 inventory 更新包
    ///   - sendUpdateAmount 同样触发 onItemUpdated -> 网络同步
    ///
    /// 严格遵循用户三大铁规：
    ///   1) 检测到蓝图自动填入，无蓝图直接跳过（仅处理含 FillTargetItem 蓝图的弹匣）
    ///   2) 仅处理背包内容（page 2..6 = SLOTS..PANTS），跳过装备槽/储物箱/区域
    ///   3) 功能 A = 一键整理附属；功能 B = 本模组独立热键
    /// </summary>
    public static class AmmoRepackService
    {
        // ─────────────────────────────────────────────────────────────
        // 公共入口
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 功能 A：合并同 ID 弹匣的子弹。
        /// 把玩家背包内同 ID 的多个未满弹匣的子弹合并到最满的那个弹匣中。
        /// 仅处理 page 2..6，仅处理含 FillTargetItem 蓝图的弹匣。
        /// </summary>
        public static void MergeSameIdMagazines(Player player)
        {
            if (player == null) return;
            PlayerInventory inv = player.inventory;
            if (inv == null) return;

            int totalMerged = 0;
            for (byte page = PlayerInventory.SLOTS; page <= PlayerInventory.PANTS; page++)
            {
                try
                {
                    totalMerged += MergePageMagazines(inv.items[page]);
                }
                catch (Exception e)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogError(
                        $"[MergeA] page {page} crashed: {e}");
                }
            }
            // if (totalMerged > 0)
            //     LaunchInPlaceReloadPlugin.Instance?.LogInfo(
            //         $"[MergeA] 共调整 {totalMerged} 个弹匣的子弹");
        }

        /// <summary>
        /// 功能 B：从弹药源给未满弹匣压弹。
        ///
        /// 匹配算法（蓝图关联匹配，对工坊模组鲁棒）：
        ///   主路径：对每个未满弹匣，遍历其所有 FillTargetItem 蓝图（旧 API 称 AMMO 蓝图），
        ///          收集所有 supplies 的物品 ID 作为"兼容弹药源 ID 列表"。
        ///          在背包中查找 asset.id 命中该列表的物品即视为兼容。
        ///   Fallback：若弹匣无任何 FillTargetItem 蓝图，且弹匣自身有 calibers 数组，
        ///          则在背包内查找同为 ItemCaliberAsset 且 calibers 数组有交集的物品。
        ///
        /// 遍历玩家背包内所有未满弹匣，找到的兼容弹药源的 amount 转移到弹匣中。
        /// 弹药源 amount 归零且 ShouldDeleteAtZeroAmount=true 时自动删除。
        /// </summary>
        public static void RepackFromAmmoBoxes(Player player)
        {
            if (player == null)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                    "[RepackB] player == null，跳过");
                return;
            }
            PlayerInventory inv = player.inventory;
            if (inv == null)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                    "[RepackB] player.inventory == null，跳过");
                return;
            }

            // 1) 扫描 page 2..6，收集未满弹匣 + 所有可堆叠物品（潜在弹药源）
            var unfilledMags = new List<MagRef>();
            var ammoBoxMap = new Dictionary<ushort, List<AmmoBoxRef>>();

            for (byte page = PlayerInventory.SLOTS; page <= PlayerInventory.PANTS; page++)
            {
                Items items = inv.items[page];
                if (items == null) continue;
                if (items.width == 0 || items.height == 0) continue;

                byte count = items.getItemCount();
                for (byte i = 0; i < count; i++)
                {
                    ItemJar jar = items.getItem(i);
                    if (jar?.item == null) continue;

                    ItemAsset asset = jar.item.GetAsset<ItemAsset>();
                    if (asset == null) continue;

                    if (asset is ItemMagazineAsset magAsset)
                    {
                        if (magAsset.MaxAmountAsByte == 0) continue;
                        if (jar.item.amount >= magAsset.MaxAmountAsByte) continue;

                        // 主路径：蓝图关联匹配 - 收集所有 FillTargetItem 蓝图的 supplies ID
                        List<ushort> compatibleIds = CollectCompatibleAmmoIds(magAsset);

                        unfilledMags.Add(new MagRef
                        {
                            page = page,
                            x = jar.x,
                            y = jar.y,
                            currentAmount = jar.item.amount,
                            maxAmount = magAsset.MaxAmountAsByte,
                            compatibleAmmoIds = compatibleIds,
                            magAsset = magAsset,
                        });
                    }
                    else
                    {
                        if (jar.item.amount == 0) continue;
                        if (asset.MaxAmountAsByte == 0) continue;

                        if (!ammoBoxMap.TryGetValue(asset.id, out var list))
                        {
                            list = new List<AmmoBoxRef>();
                            ammoBoxMap[asset.id] = list;
                        }
                        list.Add(new AmmoBoxRef
                        {
                            page = page,
                            index = i,
                            x = jar.x,
                            y = jar.y,
                            currentAmount = jar.item.amount,
                            shouldDeleteAtZero = asset.ShouldDeleteAtZeroAmount,
                            asset = asset,
                        });
                    }
                }
            }

            if (unfilledMags.Count == 0)
            {
                // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                //     "[RepackB] 扫描完毕：背包内无未满弹匣，跳过");
                return;
            }

            // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
            //     $"[RepackB] 扫描完毕：找到 {unfilledMags.Count} 个未满弹匣，{ammoBoxMap.Count} 种可堆叠物品（潜在弹药源）");

            // 2) 为每个未满弹匣查找兼容弹药源并计算转移量
            var amountUpdates = new List<AmountUpdate>();
            var deletionsByPage = new Dictionary<byte, List<int>>();

            int totalTransferred = 0;
            int magWithBlueprintMatch = 0;
            int magWithCaliberFallback = 0;
            int magNoMatch = 0;

            foreach (var mag in unfilledMags)
            {
                int remaining = mag.maxAmount - mag.currentAmount;
                if (remaining <= 0) continue;

                // 收集所有候选弹药源列表（多Asset合并）
                var candidateBoxLists = new List<List<AmmoBoxRef>>();
                bool usedFallback = false;

                // 主路径：蓝图关联匹配
                if (mag.compatibleAmmoIds.Count > 0)
                {
                    foreach (ushort ammoId in mag.compatibleAmmoIds)
                    {
                        if (ammoBoxMap.TryGetValue(ammoId, out var list) && list.Count > 0)
                            candidateBoxLists.Add(list);
                    }
                }

                // Fallback：若主路径没找到任何匹配，且弹匣有 calibers，按 caliber 匹配
                if (candidateBoxLists.Count == 0)
                {
                    var fallbackLists = FindCaliberMatch(mag.magAsset, ammoBoxMap);
                    if (fallbackLists.Count > 0)
                    {
                        candidateBoxLists = fallbackLists;
                        usedFallback = true;
                    }
                }

                if (candidateBoxLists.Count == 0)
                {
                    magNoMatch++;
                    continue;
                }

                if (usedFallback) magWithCaliberFallback++;
                else magWithBlueprintMatch++;

                int newMagAmount = mag.currentAmount;

                foreach (var boxList in candidateBoxLists)
                {
                    if (remaining <= 0) break;

                    for (int bi = 0; bi < boxList.Count; bi++)
                    {
                        if (remaining <= 0) break;
                        AmmoBoxRef box = boxList[bi];
                        if (box.currentAmount == 0) continue;

                        int transfer = Math.Min(remaining, box.currentAmount);
                        if (transfer <= 0) continue;

                        newMagAmount += transfer;
                        remaining -= transfer;

                        int newBoxAmount = box.currentAmount - transfer;
                        if (newBoxAmount <= 0 && box.shouldDeleteAtZero)
                        {
                            if (!deletionsByPage.TryGetValue(box.page, out var delList))
                            {
                                delList = new List<int>();
                                deletionsByPage[box.page] = delList;
                            }
                            delList.Add(box.index);
                            box.currentAmount = 0;
                        }
                        else
                        {
                            byte newBoxAmountByte = (byte)Math.Max(1, newBoxAmount);
                            amountUpdates.Add(new AmountUpdate
                            {
                                page = box.page,
                                x = box.x,
                                y = box.y,
                                newAmount = newBoxAmountByte
                            });
                            box.currentAmount = newBoxAmountByte;
                        }

                        totalTransferred += transfer;
                    }
                }

                if (newMagAmount != mag.currentAmount)
                {
                    amountUpdates.Add(new AmountUpdate
                    {
                        page = mag.page,
                        x = mag.x,
                        y = mag.y,
                        newAmount = (byte)newMagAmount
                    });
                }
            }

            // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
            //     $"[RepackB] 匹配统计：蓝图主路径={magWithBlueprintMatch}，Caliber Fallback={magWithCaliberFallback}，无匹配={magNoMatch}");

            if (totalTransferred == 0)
            {
                // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                //     "[RepackB] 未发生转移：未满弹匣的兼容弹药源在背包中均不存在");
                return;
            }

            // 3) 应用 amount 更新（不引起索引位移）
            foreach (var upd in amountUpdates)
            {
                inv.sendUpdateAmount(upd.page, upd.x, upd.y, upd.newAmount);
            }

            // 4) 应用删除（按页分组，索引降序删除避免位移）
            foreach (var kvp in deletionsByPage)
            {
                kvp.Value.Sort((a, b) => b.CompareTo(a));
                foreach (var index in kvp.Value)
                {
                    inv.removeItem(kvp.Key, (byte)index);
                }
            }

            // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
            //     $"[RepackB] 共压弹 {totalTransferred} 发，删除空弹药源 {deletionsByPage.Count} 个页条目");

            // 屏幕中上方 Toast 提示（仅本地执行时显示，房主路径）
            if (Provider.isServer)
            {
                RepackToast.Show(
                    $"<b><color=#5ce65c>一键压弹：成功压入 {totalTransferred} 发子弹</color></b>");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 蓝图关联匹配辅助
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 收集弹匣所有 FillTargetItem 蓝图（对应旧 EBlueprintType.AMMO）的 supplies 物品 ID。
        /// 这些 ID 构成"兼容弹药源 ID 列表"，用于绕过工坊作者乱填/漏填 Caliber 的硬伤。
        /// </summary>
        private static List<ushort> CollectCompatibleAmmoIds(ItemMagazineAsset magAsset)
        {
            var result = new HashSet<ushort>();
            if (magAsset?.blueprints == null) return new List<ushort>(result);

            for (int i = 0; i < magAsset.blueprints.Count; i++)
            {
                Blueprint bp = magAsset.blueprints[i];
                if (bp == null) continue;
                // 新 API: Operation == FillTargetItem（对应旧 EBlueprintType.AMMO）
                if (bp.Operation != EBlueprintOperation.FillTargetItem) continue;
                if (bp.supplies == null) continue;

                for (int j = 0; j < bp.supplies.Length; j++)
                {
                    BlueprintSupply supply = bp.supplies[j];
                    if (supply == null) continue;
                    ItemAsset supplyAsset = supply.FindItemAsset();
                    if (supplyAsset == null) continue;
                    result.Add(supplyAsset.id);
                }
            }
            return new List<ushort>(result);
        }

        /// <summary>
        /// Fallback：当弹匣无 FillTargetItem 蓝图时，按 calibers 数组交集匹配背包内物品。
        /// 仅对同为 ItemCaliberAsset 的物品生效（普通弹药箱不会触发此路径）。
        /// </summary>
        private static List<List<AmmoBoxRef>> FindCaliberMatch(
            ItemMagazineAsset magAsset,
            Dictionary<ushort, List<AmmoBoxRef>> ammoBoxMap)
        {
            var result = new List<List<AmmoBoxRef>>();
            ushort[] magCalibers = magAsset?.calibers;
            if (magCalibers == null || magCalibers.Length == 0) return result;

            foreach (var kvp in ammoBoxMap)
            {
                List<AmmoBoxRef> boxList = kvp.Value;
                if (boxList == null || boxList.Count == 0) continue;
                ItemAsset firstAsset = boxList[0].asset;
                if (!(firstAsset is ItemCaliberAsset boxCaliberAsset)) continue;
                if (boxCaliberAsset.calibers == null || boxCaliberAsset.calibers.Length == 0) continue;

                bool hasCommon = false;
                foreach (ushort magCal in magCalibers)
                {
                    if (magCal == 0) continue;
                    foreach (ushort boxCal in boxCaliberAsset.calibers)
                    {
                        if (magCal == boxCal) { hasCommon = true; break; }
                    }
                    if (hasCommon) break;
                }
                if (hasCommon) result.Add(boxList);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // 功能 A 内部实现
        // ─────────────────────────────────────────────────────────────

        private static int MergePageMagazines(Items items)
        {
            if (items == null) return 0;
            if (items.width == 0 || items.height == 0) return 0;

            byte count = items.getItemCount();
            if (count == 0) return 0;

            var allJars = new List<ItemJar>(count);
            for (byte i = 0; i < count; i++)
            {
                ItemJar jar = items.getItem(i);
                if (jar == null) continue;
                allJars.Add(jar);
            }

            // 按弹匣 asset id 分组（仅含 FillTargetItem 蓝图的弹匣）
            var magGroups = new Dictionary<ushort, List<int>>();
            for (int i = 0; i < allJars.Count; i++)
            {
                ItemJar jar = allJars[i];
                if (jar?.item == null) continue;

                ItemMagazineAsset magAsset = jar.item.GetAsset<ItemMagazineAsset>();
                if (magAsset == null) continue;
                if (magAsset.MaxAmountAsByte == 0) continue;
                if (FindFillBlueprint(magAsset) == null) continue;

                if (!magGroups.TryGetValue(magAsset.id, out var list))
                {
                    list = new List<int>();
                    magGroups[magAsset.id] = list;
                }
                list.Add(i);
            }

            var newAmounts = new Dictionary<int, byte>();
            var skipReAdd = new HashSet<int>();
            int mergedCount = 0;

            foreach (var kvp in magGroups)
            {
                var indices = kvp.Value;
                if (indices.Count < 2) continue;

                byte maxAmount = 0;
                int totalBullets = 0;
                foreach (int idx in indices)
                {
                    ItemJar jar = allJars[idx];
                    totalBullets += jar.item.amount;
                    ItemMagazineAsset ma = jar.item.GetAsset<ItemMagazineAsset>();
                    if (ma != null && ma.MaxAmountAsByte > maxAmount)
                        maxAmount = ma.MaxAmountAsByte;
                }
                if (maxAmount == 0) continue;

                // 按现有 amount 降序排序：最满的优先填满
                indices.Sort((a, b) =>
                    allJars[b].item.amount.CompareTo(allJars[a].item.amount));

                int bulletsLeft = totalBullets;
                bool anyChange = false;
                foreach (int idx in indices)
                {
                    ItemJar jar = allJars[idx];
                    ItemMagazineAsset ma = jar.item.GetAsset<ItemMagazineAsset>();
                    bool shouldDeleteAtZero = ma != null && ma.ShouldDeleteAtZeroAmount;
                    byte originalAmount = jar.item.amount;

                    byte targetAmount;
                    if (bulletsLeft >= maxAmount)
                    {
                        targetAmount = maxAmount;
                        bulletsLeft -= maxAmount;
                    }
                    else if (bulletsLeft > 0)
                    {
                        targetAmount = (byte)bulletsLeft;
                        bulletsLeft = 0;
                    }
                    else
                    {
                        targetAmount = 0;
                        if (shouldDeleteAtZero)
                            skipReAdd.Add(idx);
                    }

                    if (targetAmount != originalAmount)
                    {
                        newAmounts[idx] = targetAmount;
                        anyChange = true;
                    }
                }
                if (anyChange) mergedCount += indices.Count;
            }

            if (newAmounts.Count == 0 && skipReAdd.Count == 0) return 0;

            // 清空 + 重建（与 ManualTidyService.TidyPage 同模式）
            while (items.getItemCount() > 0)
                items.removeItem(0);

            for (int i = 0; i < allJars.Count; i++)
            {
                ItemJar jar = allJars[i];
                if (jar?.item == null) continue;
                if (skipReAdd.Contains(i)) continue;

                byte amount = newAmounts.TryGetValue(i, out byte overridden)
                    ? overridden : jar.item.amount;

                var newItem = new Item(jar.item.id, amount, jar.item.quality, jar.item.state);
                items.addItem(jar.x, jar.y, jar.rot, newItem);
            }

            return mergedCount;
        }

        // ─────────────────────────────────────────────────────────────
        // 蓝图工具
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 查找弹匣资产的 FillTargetItem 蓝图。
        /// 返回 null 表示该弹匣不支持被弹药箱填入（功能 A/B 均跳过）。
        /// </summary>
        private static Blueprint FindFillBlueprint(ItemMagazineAsset magAsset)
        {
            if (magAsset?.blueprints == null) return null;
            for (int i = 0; i < magAsset.blueprints.Count; i++)
            {
                Blueprint bp = magAsset.blueprints[i];
                if (bp == null) continue;
                if (bp.Operation == EBlueprintOperation.FillTargetItem) return bp;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        // 私有结构体
        // ─────────────────────────────────────────────────────────────

        private struct MagRef
        {
            public byte page;
            public byte x, y;
            public byte currentAmount;
            public byte maxAmount;
            /// <summary>主路径：蓝图关联匹配的兼容弹药源 ID 列表（可能为空）。</summary>
            public List<ushort> compatibleAmmoIds;
            /// <summary>弹匣资产引用，用于 Fallback 路径的 caliber 数组比对。</summary>
            public ItemMagazineAsset magAsset;
        }

        private class AmmoBoxRef
        {
            public byte page;
            public byte index;
            public byte x, y;
            public byte currentAmount;
            public bool shouldDeleteAtZero;
            /// <summary>物品资产引用，用于 Fallback 路径判断是否为 ItemCaliberAsset。</summary>
            public ItemAsset asset;
        }

        private struct AmountUpdate
        {
            public byte page;
            public byte x, y;
            public byte newAmount;
        }
    }
}
