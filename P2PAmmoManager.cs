namespace LaunchInPlaceReload
{
    /// <summary>
    /// 弹匣原位替换协调器：换弹时记录新弹匣在背包中的完整槽位 (page, x, y, rot, size_x, size_y)，
    /// 让旧弹匣直接写入该槽位，避免原版 forceAddItem 因空间不足而 dropItem 掉到地上。
    ///
    /// 协议：
    ///   1. UseableGun.ReceiveAttachMagazine Prefix -> BeginReload(page, x, y, rot, sx, sy)
    ///   2. PlayerInventory.forceAddItem Prefix -> TryConsumeSlot 取出槽位，原位写入
    ///   3. ReceiveAttachMagazine Postfix -> Reset 兜底清理（防异常泄漏）
    /// </summary>
    public static class P2PAmmoManager
    {
        /// <summary>
        /// 单槽位记录。一次换弹只产生一个旧弹匣，无需栈。
        /// Page = 255 视为无效（255 是 vanilla 的"无指定页"哨兵）。
        /// </summary>
        private struct PendingSlot
        {
            public byte Page;
            public byte X;
            public byte Y;
            public byte Rot;
            public byte SizeX;
            public byte SizeY;

            public bool IsValid => Page != 255;
        }

        private static PendingSlot _pending;

        /// <summary>
        /// 在 ReceiveAttachMagazine Prefix 中调用。仅 attach 分支调用（page != 255）。
        /// detach-only 分支（page == 255）不调用此方法。
        /// </summary>
        public static void BeginReload(byte page, byte x, byte y, byte rot, byte sizeX, byte sizeY)
        {
            _pending = new PendingSlot
            {
                Page = page,
                X = x,
                Y = y,
                Rot = rot,
                SizeX = sizeX,
                SizeY = sizeY
            };
        }

        /// <summary>
        /// 消费待替换槽位。如果有效则消费并返回 true。
        /// 一次消费后立即失效，防止后续 forceAddItem 误用。
        /// </summary>
        public static bool TryConsumeSlot(
            out byte page, out byte x, out byte y,
            out byte rot, out byte sizeX, out byte sizeY)
        {
            page = _pending.Page;
            x = _pending.X;
            y = _pending.Y;
            rot = _pending.Rot;
            sizeX = _pending.SizeX;
            sizeY = _pending.SizeY;

            bool valid = _pending.IsValid;
            _pending = default;
            return valid;
        }

        /// <summary>
        /// 兜底清理。ReceiveAttachMagazine Postfix 调用，防异常导致状态泄漏到下次调用。
        /// </summary>
        public static void Reset() => _pending = default;
    }
}
