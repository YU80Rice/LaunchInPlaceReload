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
    ///     [EModMessage.RequestRepackAmmo: byte = 10]
    ///     （无业务字段，整个背包所有弹匣都压弹）
    ///
    ///   服务器 -> 客机（v1.1.2 新增）
    ///     [REPACK_SUCCESS: byte = 11][totalTransferred: int32]
    ///     由发起方客户端接收后调 RepackToast.Show 本地显示 toast。
    ///     U3DS 是 headless 服务器无 PlayerUI，toast 必须在客户端显示。
    ///
    /// 注意：REPACK_SUCCESS 标识字节值 11 是本插件内部约定，不依赖
    /// LaunchMultiplayerNet 的 EModMessage 枚举（避免对前置库做任何修改）。
    /// 数值 11 与 EModMessage 现有值（10=RequestRepackAmmo, 20/21=Horde）不冲突。
    ///
    /// 服务器端处理：
    ///   1) 通过 sender CSteamID 在 Provider.clients 中反查 Player
    ///   2) 调 AmmoRepackService.RepackFromAmmoBoxes(player) 拿到 totalTransferred
    ///   3) Items.removeItem/addItem 触发原生网络同步，客机端自动收到 inventory 更新
    ///   4) SendToClient(sender, REPACK_SUCCESS + totalTransferred) 让发起方显示 toast
    /// </summary>
    public static class AmmoRepackNetwork
    {
        /// <summary>本插件内部约定的"压弹成功回包"标识字节。不写入 EModMessage 枚举以避免修改前置库。</summary>
        private const byte REPACK_SUCCESS = 11;

        /// <summary>由 Plugin.Awake 调用，注册服务器端 + 客户端通道处理器。</summary>
        public static void RegisterHandlers()
        {
            ModTransport.RegisterServerHandler(ModChannels.RepackAmmo, HandleRequestRepackAmmo);
            ModTransport.RegisterClientHandler(ModChannels.RepackAmmo, HandleRepackSuccessFromServer);
            LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                "[RepackNet] 已注册 channel=" + ModChannels.RepackAmmo + " 服务器端+客户端处理器");
        }

        // ─────────────────────────────────────────────────────────────
        // 客机端：发送请求
        // ─────────────────────────────────────────────────────────────

        /// <summary>客机端：请求服务器帮我执行全背包压弹。</summary>
        public static void SendRepackRequest()
        {
            byte[] payload = ModTransport.BuildMessage(EModMessage.RequestRepackAmmo);
            ModTransport.SendToServer(ModChannels.RepackAmmo, payload, reliable: true);
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

                int totalTransferred = AmmoRepackService.RepackFromAmmoBoxes(player);
                // LaunchInPlaceReloadPlugin.Instance?.LogInfo(
                //     $"[RepackNet] 服务器: 已为 sender={(ulong)sender} 执行压弹，转移 {totalTransferred} 发");

                // 回发 REPACK_SUCCESS 给发起方客户端，让其显示 toast
                SendRepackSuccess(sender, totalTransferred);
            }
            catch (Exception e)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogError(
                    $"[RepackNet] HandleRequestRepackAmmo crash: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 服务器端：回发压弹成功消息（不依赖 EModMessage，手写 payload）
        // ─────────────────────────────────────────────────────────────

        private static void SendRepackSuccess(CSteamID client, int totalTransferred)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(REPACK_SUCCESS);
                writer.Write(totalTransferred);
                payload = ms.ToArray();
            }
            ModTransport.SendToClient(client, ModChannels.RepackAmmo, payload, reliable: true);
        }

        // ─────────────────────────────────────────────────────────────
        // 客户端：接收服务器回发的 REPACK_SUCCESS
        // ─────────────────────────────────────────────────────────────

        private static void HandleRepackSuccessFromServer(BinaryReader reader)
        {
            try
            {
                byte msgType = reader.ReadByte();
                if (msgType != REPACK_SUCCESS)
                {
                    LaunchInPlaceReloadPlugin.Instance?.LogWarning(
                        $"[RepackNet] 客户端收到未知消息类型 {msgType}，忽略");
                    return;
                }

                int totalTransferred = reader.ReadInt32();

                if (totalTransferred > 0)
                {
                    RepackToast.Show(
                        $"<b><color=#5ce65c>一键压弹：成功压入 {totalTransferred} 发子弹</color></b>");
                }
            }
            catch (Exception e)
            {
                LaunchInPlaceReloadPlugin.Instance?.LogError(
                    $"[RepackNet] HandleRepackSuccessFromServer crash: {e}");
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
                if (sp == null || ReferenceEquals(sp.playerID, null)) continue;
                if ((ulong)sp.playerID.steamID == targetId)
                    return sp.player;
            }
            return null;
        }
    }
}
