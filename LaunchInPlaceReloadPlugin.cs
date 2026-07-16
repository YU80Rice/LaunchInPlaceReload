using BepInEx;
using HarmonyLib;
using LaunchInPlaceReload.Patches;
using LaunchMultiplayerNet;
using SDG.Unturned;
using UnityEngine;

namespace LaunchInPlaceReload
{
    /// <summary>
    /// v2.0.0 重构要点：
    /// 1. 显式声明 LaunchMultiplayerNet 硬依赖（之前缺失，靠隐式加载顺序）
    /// 2. Awake 错误隔离：PatchAll / TidyHook / ModTransport / AmmoRepackNetwork 各自独立 try-catch
    ///    任一子步骤失败不再阻止其他子步骤（之前 TidyHook AmbiguousMatchException 会吞掉整个 Awake）
    /// 3. 单机+联机双模式自适应：Provider.isServer 时本地执行，否则走 LaunchMultiplayerNet 网络
    /// </summary>
    [BepInPlugin(Guid, "LaunchInPlaceReload [v2.0.0 重构 / 双端适配]", Version)]
    [BepInDependency(LaunchMultiplayerNetPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.yu80rice.launchinventorytidy", BepInDependency.DependencyFlags.SoftDependency)]
    public class LaunchInPlaceReloadPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.yu80rice.launchinplacereload";
        public const string Version = "2.0.0";
        public const string HARMONY_ID = "com.yu80rice.launchinplacereload";

        public static LaunchInPlaceReloadPlugin Instance { get; private set; }

        private Harmony _harmony;

        // ───── 双击换弹键检测状态 ─────
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;
        private float _lastReloadKeyDownTime = -1f;

        internal void LogInfo(string msg) => Logger.LogInfo(msg);
        internal void LogWarning(string msg) => Logger.LogWarning(msg);
        internal void LogError(string msg) => Logger.LogError(msg);

        private void Awake()
        {
            Instance = this;

            // 步骤 1：Harmony patch（原位换弹 + forceAddItem 拦截）
            try
            {
                _harmony = new Harmony(HARMONY_ID);
                _harmony.PatchAll();
                Logger.LogInfo($"[Bootstrap] Harmony patchAll 完成 (id={HARMONY_ID})");
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Bootstrap] Harmony patchAll 失败（原位换弹功能不可用）: {e}");
            }

            // 步骤 2：软依赖 LaunchInventoryTidy 整理后缀（功能 A 附属，失败不影响功能 B）
            try
            {
                TidyServicePostfixPatch.TryRegister(_harmony);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Bootstrap] TidyServicePostfixPatch 注册异常（已吞掉）: {e}");
            }

            // 步骤 3：DontDestroyOnLoad
            try
            {
                DontDestroyOnLoad(gameObject);
                gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Bootstrap] DontDestroyOnLoad 异常: {e}");
            }

            // 步骤 4：网络层 + 通道处理器注册（功能 B 联机路径）
            try
            {
                ModTransport.Initialize();
                AmmoRepackNetwork.RegisterHandlers();
                Logger.LogInfo("[Bootstrap] 网络层初始化完成 (channel=" + ModChannels.RepackAmmo + ")");
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Bootstrap] 网络层初始化失败（联机压弹不可用，单机压弹仍可用）: {e}");
            }

            Logger.LogInfo("===============================================");
            Logger.LogInfo(" LaunchInPlaceReload v2.0.0 已加载 - 换弹原位替换 + 一键压弹（单机/联机双适配）");
            Logger.LogInfo(" 提示：双击换弹键（默认 R）触发一键压弹 ");
            Logger.LogInfo("===============================================");
        }

        private void Update()
        {
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
                Logger.LogError("[RepackB] InputEx.GetKeyDown 异常: " + e);
                return;
            }

            if (!keyPressed) return;

            // ───── 双击差值算法 ─────
            float now = Time.time;
            float deltaT = now - _lastReloadKeyDownTime;

            if (_lastReloadKeyDownTime > 0f && deltaT <= DOUBLE_TAP_THRESHOLD)
            {
                // 双击触发
                // 清空时间戳，防止玩家连击时触发多重调用
                _lastReloadKeyDownTime = -1f;

                try
                {
                    if (Provider.isServer)
                    {
                        // 单机/房主：直接执行，本地显示 toast
                        int totalTransferred = AmmoRepackService.RepackFromAmmoBoxes(player);
                        if (totalTransferred > 0)
                        {
                            RepackToast.Show(
                                $"<b><color=#5ce65c>一键压弹：成功压入 {totalTransferred} 发子弹</color></b>");
                        }
                    }
                    else
                    {
                        // 客机：发请求包给服务器，等待 RepackSuccess 回包后由客户端 handler 显示 toast
                        AmmoRepackNetwork.SendRepackRequest();
                    }
                }
                catch (System.Exception e)
                {
                    Logger.LogError("[RepackB] uncaught: " + e);
                }
            }
            else
            {
                // 单击：更新时间戳，放行原版换弹行为
                _lastReloadKeyDownTime = now;
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Instance = null;
        }
    }
}
