using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace precisionknapping
{
    /// <summary>
    /// Client side of Charged Strikes:
    /// - takes over the server's settings when they arrive
    /// - charge cues while holding (strike ready / power ready)
    /// - on release: sends the strike and plays the swing, sound, chips and camera shake locally
    ///   (the server leaves the striker out of its own strike sound, see KnappingSoundHelper)
    /// </summary>
    public class ChargedStrikeClient
    {
        public static ChargedStrikeClient Instance { get; private set; }

        private const int TapHintCooldownMs = 4000;

        private static readonly AssetLocation CueSound = new AssetLocation("game:sounds/tick");
        private static readonly AssetLocation ImpactSound = new AssetLocation("game:sounds/block/rock-hit-flint");
        private static readonly AssetLocation WhooshSound = new AssetLocation("game:sounds/player/strike");
        private static readonly AssetLocation[] KnapSounds =
        {
            new AssetLocation("game:sounds/player/knap1"),
            new AssetLocation("game:sounds/player/knap2")
        };

        private readonly ICoreClientAPI capi;
        private readonly IClientNetworkChannel channel;
        private long lastTapHintMs = -TapHintCooldownMs;

        public ChargedStrikeClient(ICoreClientAPI api)
        {
            capi = api;

            channel = api.Network.RegisterChannel(ChargedStrikeServer.ChannelName)
                .RegisterMessageType<ChargedStrikePacket>()
                .RegisterMessageType<KnappingSettingsPacket>()
                .SetMessageHandler<KnappingSettingsPacket>(OnSettingsReceived);

            Instance = this;
        }

        public static void Dispose()
        {
            Instance = null;
        }

        /// <summary>
        /// The server decides the gameplay settings, so a client config can't disagree with it.
        /// </summary>
        private void OnSettingsReceived(KnappingSettingsPacket packet)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || packet == null) return;

            config.ChargedStrikes = packet.ChargedStrikes;
            config.MinChargeTimeMs = packet.MinChargeTimeMs;
            config.FullChargeTimeMs = packet.FullChargeTimeMs;
            config.PowerStrikeRadius = packet.PowerStrikeRadius;
            config.AdvancedMode = packet.AdvancedMode;
        }

        private bool IsLocalPlayer(EntityAgent entity) => entity != null && entity == capi.World?.Player?.Entity;

        /// <summary>
        /// Called every tick while the mouse is held.
        /// </summary>
        public void OnChargeStep(EntityAgent entity, ChargeState state)
        {
            if (!IsLocalPlayer(entity)) return;

            var config = PrecisionKnappingModSystem.Config;
            float charge = ChargeTracker.GetChargeLevel(capi.World.ElapsedMilliseconds - state.StartMs, config);

            if (state.CuesPlayed < 1 && charge >= ChargeTracker.GetMinChargeLevel(config))
            {
                state.CuesPlayed = 1;
                PlayCue(state.Pos, 0.9f, 0.25f);
            }

            if (state.CuesPlayed < 2 && ChargeTracker.IsPowerCharge(charge))
            {
                state.CuesPlayed = 2;
                PlayCue(state.Pos, 1.4f, 0.45f);
                if (config.EnableStrikeCameraShake) capi.World.AddCameraShake(0.02f);
            }
        }

        /// <summary>
        /// Called when the mouse is released (or the charge was cancelled).
        /// </summary>
        public void OnRelease(EntityAgent entity, ChargeState state, BlockSelection blockSel, float charge)
        {
            if (!IsLocalPlayer(entity) || state.Cancelled)
            {
                ChargedStrikeAnimations.StopCharge(entity);
                return;
            }

            var config = PrecisionKnappingModSystem.Config;
            if (charge < ChargeTracker.GetMinChargeLevel(config))
            {
                ChargedStrikeAnimations.StopCharge(entity);
                ShowTapHint();
                return;
            }

            // The strike lands where the player looks now, on the surface they started on
            var surface = KnappingStoneTargets.GetActiveSurface(capi.World, blockSel);
            if (surface == null || !blockSel.Position.Equals(state.Pos) || !TryGetVoxel(surface, blockSel, out int voxelX, out int voxelZ))
            {
                ChargedStrikeAnimations.StopCharge(entity);
                return;
            }

            channel.SendPacket(new ChargedStrikePacket
            {
                X = blockSel.Position.X,
                InternalY = blockSel.Position.InternalY,
                Z = blockSel.Position.Z,
                VoxelX = voxelX,
                VoxelZ = voxelZ,
                FacingIndex = blockSel.Face?.Index ?? BlockFacing.UP.Index,
                Charge = charge
            });

            ChargedStrikeAnimations.PlayStrike(entity, charge);
            PlayStrikeFeedback(surface, voxelX, voxelZ, charge, config);
        }

        /// <summary>
        /// Voxel under the cursor, read the same way vanilla does (from the selection box).
        /// </summary>
        private bool TryGetVoxel(BlockEntity surface, BlockSelection blockSel, out int voxelX, out int voxelZ)
        {
            voxelX = voxelZ = -1;

            var boxes = surface.Block?.GetSelectionBoxes(capi.World.BlockAccessor, blockSel.Position);
            int index = blockSel.SelectionBoxIndex;
            if (boxes == null || index < 0 || index >= boxes.Length) return false;

            var box = boxes[index];
            voxelX = (int)(16 * box.X1);
            voxelZ = (int)(16 * box.Z1);
            return voxelX >= 0 && voxelX < 16 && voxelZ >= 0 && voxelZ < 16;
        }

        private void ShowTapHint()
        {
            long now = capi.World.ElapsedMilliseconds;
            if (now - lastTapHintMs < TapHintCooldownMs) return;

            lastTapHintMs = now;
            capi.TriggerIngameError(this, "precisionknapping-tap", "Hold left click to charge the strike, then release");
        }

        private void PlayCue(BlockPos pos, float pitch, float volume)
        {
            if (PrecisionKnappingModSystem.Config?.EnableChargeSounds != true || pos == null) return;
            capi.World.PlaySoundAt(CueSound, pos.X + 0.5, pos.Y + 0.1, pos.Z + 0.5, null, pitch, 8f, volume);
        }

        /// <summary>
        /// Local sound, chips and shake. Harder strikes are louder, deeper and throw more chips.
        /// </summary>
        private void PlayStrikeFeedback(BlockEntity surface, int voxelX, int voxelZ, float charge, PrecisionKnappingConfig config)
        {
            bool isPower = ChargeTracker.IsPowerCharge(charge);
            var voxels = KnappingReflectionHelper.GetCurrentVoxels(surface);
            bool hitStone = voxels != null && voxels[voxelX, voxelZ];

            double x = surface.Pos.X + (voxelX + 0.5) / 16.0;
            double y = surface.Pos.Y + 1 / 16.0;
            double z = surface.Pos.Z + (voxelZ + 0.5) / 16.0;

            if (!hitStone)
            {
                capi.World.PlaySoundAt(WhooshSound, x, y, z, null, 1.1f, 8f, 0.3f);
                return;
            }

            var knap = KnapSounds[capi.World.Rand.Next(KnapSounds.Length)];
            capi.World.PlaySoundAt(knap, x, y, z, null, 1.15f - 0.3f * charge, 12f, 0.6f + 0.4f * charge);

            if (isPower && config.EnableChargeSounds)
            {
                capi.World.PlaySoundAt(ImpactSound, x, y, z, null, 0.75f, 16f, 1f);
            }

            SpawnChips(surface, x, y, z, charge, isPower);

            if (config.EnableStrikeCameraShake)
            {
                capi.World.AddCameraShake(isPower ? 0.15f : 0.05f * charge);
            }
        }

        private void SpawnChips(BlockEntity surface, double x, double y, double z, float charge, bool isPower)
        {
            var material = KnappingReflectionHelper.GetBaseMaterial(surface);
            if (material?.Collectible == null) return;

            var rand = capi.World.Rand;
            float spread = isPower ? 3f / 16f : 1f / 16f;
            int bursts = isPower ? 6 : 1 + (int)(3 * charge);

            for (int i = 0; i < bursts; i++)
            {
                capi.World.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1,
                    AddQuantity = 2,
                    Color = material.Collectible.GetRandomColor(capi, material),
                    MinPos = new Vec3d(x - spread / 2, y + 0.01, z - spread / 2),
                    AddPos = new Vec3d(spread, 0.01, spread),
                    MinVelocity = new Vec3f(0, 1 + charge, 0),
                    AddVelocity = new Vec3f(
                        (2 + 3 * charge) * ((float)rand.NextDouble() - 0.5f),
                        1f * ((float)rand.NextDouble() - 0.5f),
                        (2 + 3 * charge) * ((float)rand.NextDouble() - 0.5f)),
                    LifeLength = 0.25f,
                    GravityEffect = 1f,
                    MinSize = 0.1f,
                    MaxSize = isPower ? 0.5f : 0.35f,
                    ParticleModel = EnumParticleModel.Cube,
                    SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.15f)
                });
            }
        }
    }
}
