using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace precisionknapping
{
    /// <summary>
    /// Server side of Charged Strikes:
    /// - sends the server's gameplay settings to each player on join
    /// - validates each strike packet and runs it through the vanilla OnUseOver, so all the
    ///   normal rules (KnappingSurfacePatch, completion, tutorial events) still apply
    /// </summary>
    public class ChargedStrikeServer
    {
        public const string ChannelName = "precisionknapping";

        /// <summary>Extra blocks of reach allowed beyond the player's picking range (lag, rounding).</summary>
        private const double ReachTolerance = 1.5;

        /// <summary>Extra charge allowed beyond the time since the last strike (network jitter).</summary>
        private const float ChargeTolerance = 0.25f;

        private readonly ICoreServerAPI sapi;
        private readonly IServerNetworkChannel channel;
        private readonly Dictionary<string, long> lastStrikeMs = new Dictionary<string, long>();

        public ChargedStrikeServer(ICoreServerAPI api)
        {
            sapi = api;

            channel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<ChargedStrikePacket>()
                .RegisterMessageType<KnappingSettingsPacket>()
                .SetMessageHandler<ChargedStrikePacket>(OnStrikePacket);

            api.Event.PlayerJoin += SendSettings;
            api.Event.PlayerDisconnect += player => lastStrikeMs.Remove(player.PlayerUID);
        }

        private void SendSettings(IServerPlayer player)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null) return;

            channel.SendPacket(new KnappingSettingsPacket
            {
                ChargedStrikes = config.ChargedStrikes,
                MinChargeTimeMs = config.MinChargeTimeMs,
                FullChargeTimeMs = config.FullChargeTimeMs,
                PowerStrikeRadius = config.PowerStrikeRadius,
                AdvancedMode = config.AdvancedMode
            }, player);
        }

        private void OnStrikePacket(IServerPlayer player, ChargedStrikePacket packet)
        {
            try
            {
                var config = PrecisionKnappingModSystem.Config;
                if (config?.ChargedStrikes != true || player?.Entity == null || packet == null) return;

                if (packet.VoxelX < 0 || packet.VoxelX >= 16 || packet.VoxelZ < 0 || packet.VoxelZ >= 16) return;

                var pos = new BlockPos(packet.X, packet.InternalY, packet.Z);
                var entity = sapi.World.BlockAccessor.GetBlockEntity(pos);
                if (!KnappingReflectionHelper.IsKnappingSurface(entity)) return;
                if (KnappingReflectionHelper.GetSelectedRecipe(entity) == null) return;

                // Same checks the vanilla knap packet gets: reach, land claims, something in hand
                double reach = player.WorldData.PickingRange + ReachTolerance;
                if (player.Entity.Pos.DistanceTo(new Vec3d(pos.X + 0.5, pos.InternalY + 0.5, pos.Z + 0.5)) > reach) return;
                if (!sapi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.Use)) return;
                if (player.InventoryManager.ActiveHotbarSlot?.Itemstack == null) return;

                // A strike can't carry more charge than the time since the previous strike allows
                long now = sapi.World.ElapsedMilliseconds;
                float charge = Math.Clamp(packet.Charge, 0f, 1f);
                if (lastStrikeMs.TryGetValue(player.PlayerUID, out long last))
                {
                    charge = Math.Min(charge, ChargeTracker.GetChargeLevel(now - last, config) + ChargeTolerance);
                }

                if (charge < ChargeTracker.GetMinChargeLevel(config)) return; // Too quick: just a tap
                lastStrikeMs[player.PlayerUID] = now;

                var strike = new ChargedStrike
                {
                    Charge = charge,
                    IsPower = ChargeTracker.IsPowerCharge(charge)
                };

                var facing = packet.FacingIndex >= 0 && packet.FacingIndex < BlockFacing.ALLFACES.Length
                    ? BlockFacing.ALLFACES[packet.FacingIndex]
                    : BlockFacing.UP;
                var voxelPos = new Vec3i(packet.VoxelX, 0, packet.VoxelZ);

                ChargedStrikeContext.Run(strike, () =>
                    KnappingReflectionHelper.CallOnUseOver(entity, player, voxelPos, facing));
            }
            catch (Exception ex)
            {
                sapi.Logger.Error($"[PrecisionKnapping] Charged strike failed: {ex}");
            }
        }
    }
}
