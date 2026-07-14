using System;
using System.IO;
using LaunchMultiplayerNet;
using SDG.Unturned;
using Steamworks;

namespace LaunchInPlaceReload
{
    /// <summary>
    /// 压弹请求的双端自适应网络层。
    ///
    /// 协议（Channel 101 = ModChannels.RepackAmmo）：
    ///   客机 -> 服务器
    ///     [EModMessage.RequestRepackAmmo: byte]
    ///     （无业务字段，整个背包所有弹匣都压弹）
    ///
    /// 服务器端处理：
    ///   1) 通过 sender CSteamID 在 Provider.clients 中反查 Player
    ///   2) 调 AmmoRepackService.Repack(player.inventory) 在服务器端执行合并
    ///   3) Items.removeItem/addItem 触发原生网络同步，客机端自动收到 inventory 更新
    /// </summary>
    public static class AmmoRepackNetwork
    {
        /// <summary>由 Plugin.Awake 调用，注册服务器端通道处理器。</summary>
        public static void RegisterHandlers()
        {
            ModP2PTransport.RegisterServerHandler(ModChannels.RepackAmmo, HandleRequestRepackAmmo);
            LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                "[RepackNet] 已注册 channel=" + ModChannels.RepackAmmo + " 服务器端处理器");
        }

        // ─────────────────────────────────────────────────────────────
        // 客机端：发送请求
        // ─────────────────────────────────────────────────────────────

        /// <summary>客机端：请求服务器帮我执行全背包压弹。</summary>
        public static void SendRepackRequest()
        {
            byte[] payload = ModP2PTransport.BuildMessage(EModMessage.RequestRepackAmmo);
            ModP2PTransport.SendToServer(ModChannels.RepackAmmo, payload, reliable: true);
            // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
            //     "[RepackNet] -> 服务器: RequestRepackAmmo");
        }

        // ─────────────────────────────────────────────────────────────
        // 服务器端：处理请求
        // ─────────────────────────────────────────────────────────────

        private static void HandleRequestRepackAmmo(CSteamID sender, BinaryReader reader)
        {
            try
            {
                // 读取并校验消息类型（单通道单消息，可省略，但保留以便扩展）
                byte msgType = reader.ReadByte();
                if (msgType != (byte)EModMessage.RequestRepackAmmo)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                        $"[RepackNet] 收到未知消息类型 {msgType}，忽略");
                    return;
                }

                Player player = ResolvePlayerBySteamId(sender);
                if (player?.inventory == null)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                        $"[RepackNet] 收到 RequestRepackAmmo 但 sender {(ulong)sender} 无对应 Player");
                    return;
                }

                AmmoRepackService.RepackFromAmmoBoxes(player);
                // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                //     $"[RepackNet] 服务器: 已为 sender={(ulong)sender} 执行压弹");
            }
            catch (Exception e)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogError(
                    $"[RepackNet] HandleRequestRepackAmmo crash: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CSteamID -> Player 反查
        // ─────────────────────────────────────────────────────────────

        private static Player ResolvePlayerBySteamId(CSteamID steamId)
        {
            var clients = Provider.clients;
            if (clients == null) return null;

            ulong targetId = (ulong)steamId;
            for (int i = 0; i < clients.Count; i++)
            {
                SteamPlayer sp = clients[i];
                if (sp == null || sp.playerID == null) continue;
                if ((ulong)sp.playerID.steamID == targetId)
                    return sp.player;
            }
            return null;
        }
    }
}
