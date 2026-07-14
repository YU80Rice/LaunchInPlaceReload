using BepInEx;
using HarmonyLib;
using LaunchInPlaceReload.Patches;
using LaunchMultiplayerNet;
using SDG.Unturned;
using UnityEngine;

namespace LaunchInPlaceReload
{
    [BepInPlugin("com.yourname.launchinplacereload", "LaunchInPlaceReload [v1.0 正式版 / 双端适配]", "1.0.0")]
    [BepInDependency("com.yourname.launchinventorytidy", BepInDependency.DependencyFlags.SoftDependency)]
    public class LaunchInPlaceReloadPlugin : BaseUnityPlugin
    {
        public const string HARMONY_ID = "com.yourname.launchinplacereload";

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

            _harmony = new Harmony(HARMONY_ID);
            _harmony.PatchAll();

            // 软依赖 LaunchInventoryTidy：注册一键整理后缀 patch（功能 A 附属）
            TidyServicePostfixPatch.TryRegister(_harmony);

            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            // 初始化 P2P 网络层并注册通道处理器
            ModP2PTransport.Initialize();
            AmmoRepackNetwork.RegisterHandlers();

            Logger.LogInfo("===============================================");
            Logger.LogInfo(" LaunchInPlaceReload 已加载 - 换弹原位替换 + 一键压弹（联机）");
            Logger.LogInfo(" 提示：双击换弹键（默认 R）触发一键压弹 ");
            Logger.LogInfo("===============================================");
        }

        private void Update()
        {
            try
            {
                // 必须每帧驱动 P2P 轮询（房主收请求，客机无动作但调用安全）
                ModP2PTransport.Poll();
            }
            catch (System.Exception e)
            {
                Logger.LogError("[RepackB] ModP2PTransport.Poll() 异常: " + e);
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
                        // 房主：直接执行
                        AmmoRepackService.RepackFromAmmoBoxes(player);
                    }
                    else
                    {
                        // 客机：发请求包给服务器
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
