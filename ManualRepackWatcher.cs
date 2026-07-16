using SDG.Unturned;
using UnityEngine;

namespace LaunchInPlaceReload
{
    /// <summary>
    /// 双击换弹键拦截器（Double-Tap Reload Watcher）。
    ///
    /// 触发逻辑：
    ///   - 动态读取 ControlsSettings.reload（玩家当前绑定的换弹键，默认 R，可在 Settings 修改）
    ///   - 单击换弹键 -> 放行原版换弹行为，仅记录时间戳
    ///   - 双击换弹键（ΔT ≤ 0.3s）-> 触发一键压弹（功能 B），原版换弹仍照常执行
    ///   - 双击触发后清空时间戳，防止连击多重触发
    ///
    /// v2.0 架构（2026-07-14 重构）：
    /// - 弃用 listen server + SteamNetworking P2P，改用 U3DS dedicated server + vanilla SteamChannel
    /// - 房主 Unturned 客户端 = 普通客户端，请求统一走 AmmoRepackNetwork.SendRepackRequest -> U3DS
    /// - 删除 Poll（vanilla SteamChannel 自动路由）
    /// - 删除 Provider.isServer 分支（房主也是客户端，统一发请求给 U3DS）
    /// </summary>
    public class ManualRepackWatcher : MonoBehaviour
    {
        /// <summary>双击灵敏度阈值（秒）。</summary>
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;

        /// <summary>上一次换弹键按下时间戳（Time.time）。-1 表示尚未记录或刚触发完双击。</summary>
        private static float _lastReloadKeyDownTime = -1f;

        /// <summary>启动时是否已打印过换弹键绑定信息（节流）。</summary>
        private static bool _loggedKeyInfo;

        /// <summary>Update 是否已被调用过（诊断用）。</summary>
        private static bool _updateEntered;

        private void Awake()
        {
            LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                "[RepackB] ManualRepackWatcher.Awake() 被调用，MonoBehaviour 已附加到 GameObject");
        }

        private void Start()
        {
            LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                "[RepackB] ManualRepackWatcher.Start() 被调用，Update 即将开始");
        }

        private void Update()
        {
            // 诊断：第一次进入 Update 时打印（用于确认 Update 是否被调用）
            if (!_updateEntered)
            {
                _updateEntered = true;
                LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                    "[RepackB] ManualRepackWatcher.Update() 第一次执行");
            }

            // 启动后第一次 Update 时打印当前换弹键绑定
            if (!_loggedKeyInfo)
            {
                _loggedKeyInfo = true;
                KeyCode initReloadKey = ControlsSettings.reload;
                LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                    $"[RepackB] 监听换弹键 = {initReloadKey}（双击触发一键压弹，阈值 {DOUBLE_TAP_THRESHOLD}s）");
            }

            Player player = Player.LocalPlayer;
            if (player == null || player.inventory == null) return;

            KeyCode reloadKey = ControlsSettings.reload;
            if (reloadKey == KeyCode.None) return;

            // InputEx.GetKeyDown 内部已处理吞键：聊天框/搜索框聚焦、重绑定时返回 false
            bool keyPressed;
            try
            {
                keyPressed = InputEx.GetKeyDown(reloadKey);
            }
            catch (System.Exception e)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogError("[RepackB] InputEx.GetKeyDown 异常: " + e);
                return;
            }

            if (!keyPressed) return;

            // ───── 双击差值算法 ─────
            float now = Time.time;
            float deltaT = now - _lastReloadKeyDownTime;

            if (_lastReloadKeyDownTime > 0f && deltaT <= DOUBLE_TAP_THRESHOLD)
            {
                // 双击触发
                LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                    $"[RepackB] 双击换弹键触发 (ΔT={deltaT:F3}s)，开始扫描并执行一键压弹");

                // 清空时间戳，防止玩家连击时触发多重调用
                _lastReloadKeyDownTime = -1f;

                try
                {
                    // 统一走网络路径：客户端 -> U3DS 服务器 -> 自动事件同步回客户端
                    AmmoRepackNetwork.SendRepackRequest();
                }
                catch (System.Exception e)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogError("[RepackB] uncaught: " + e);
                }
            }
            else
            {
                // 单击：更新时间戳，放行原版换弹行为
                _lastReloadKeyDownTime = now;
            }
        }
    }
}
