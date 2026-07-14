using SDG.Unturned;

namespace LaunchInPlaceReload
{
    /// <summary>
    /// 一键压弹成功时的屏幕提示器。
    ///
    /// 采用 Unturned 原生 EPlayerMessage 提示系统：
    ///   - 调用 PlayerUI.message(EPlayerMessage.NPC_CUSTOM, text, duration)
    ///   - NPC_CUSTOM 分支会直接显示传入的字符串，并允许富文本
    ///   - 原生系统自带位置管理、淡出和层级处理，不会被遮挡
    ///
    /// 提示位置与原版"换弹"/"交互"提示相同（屏幕底部上方），
    /// 符合"严禁拦截或覆盖底部原版提示"的要求。
    /// </summary>
    public static class RepackToast
    {
        private const float DURATION = 2.5f;

        /// <summary>
        /// 显示压弹成功提示。
        /// message 应为富文本，例如：
        /// "<b><color=#5ce65c>一键压弹：成功压入 60 发子弹</color></b>"
        /// </summary>
        public static void Show(string message)
        {
            // EPlayerMessage.NPC_CUSTOM 允许直接传入字符串显示，并允许富文本
            PlayerUI.message(EPlayerMessage.NPC_CUSTOM, message, DURATION);
        }
    }
}
