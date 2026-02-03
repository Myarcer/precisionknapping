using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;
using ProtoBuf;

namespace precisionknapping
{
    #region Network Packet

    /// <summary>
    /// Packet sent from client to server when player releases a charged strike.
    /// Contains target position, voxel, and charge level.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ChargeReleasePacket
    {
        public int BlockX;
        public int BlockY;
        public int BlockZ;
        public int VoxelX;
        public int VoxelZ;
        public float ChargeLevel; // 0.0 - 1.0
    }

    #endregion

    #region Client Tracker

    /// <summary>
    /// Client-side tracker for ChargedStrikes mode.
    /// Polls mouse button state and sends ChargeReleasePacket on mouse release.
    /// Integrates with ChargeSoundManager and KnappingAnimationManager for feedback.
    /// </summary>
    public class ChargeStateTracker
    {
        private readonly ICoreClientAPI capi;
        private IClientNetworkChannel channel;
        private ChargeSoundManager soundManager;
        
        private bool wasMouseDown = false;
        private long chargeStartTime = 0;
        private BlockPos targetBlockPos = null;
        private int targetVoxelX = 0;
        private int targetVoxelZ = 0;
        private bool isTracking = false;
        
        public bool IsCharging => isTracking && targetBlockPos != null;
        public float CurrentChargeLevel => CalculateChargeLevel();

        public ChargeStateTracker(ICoreClientAPI api)
        {
            capi = api;
            
            // Initialize sound manager
            soundManager = new ChargeSoundManager(api);
            
            // Register network channel on client
            channel = api.Network.RegisterChannel("precisionknapping")
                .RegisterMessageType<ChargeReleasePacket>();
            
            // Register tick listener
            api.Event.RegisterGameTickListener(OnClientTick, 20); // 20ms = 50Hz
            
            api.Logger.Notification("[PrecisionKnapping] ChargedStrikes charge tracker initialized");
        }

        private void OnClientTick(float dt)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || !config.ChargedStrikes) return;

            var player = capi.World?.Player;
            if (player == null) return;

            var blockSel = player.CurrentBlockSelection;
            bool isLookingAtKnapping = IsKnappingSurface(blockSel);
            bool isMouseDown = capi.Input.InWorldMouseButton.Left;
            
            // Mouse down on knapping surface → start tracking
            if (isMouseDown && !wasMouseDown && isLookingAtKnapping)
            {
                StartTracking(blockSel, player.Entity as EntityPlayer);
            }
            
            // Update charge feedback while charging
            if (isMouseDown && isTracking)
            {
                UpdateCharging();
            }
            
            // Mouse up while we have a target → send strike packet
            if (!isMouseDown && wasMouseDown && isTracking)
            {
                ReleaseStrike(player.Entity as EntityPlayer);
            }
            
            // Look away while charging → cancel (just clear local state)
            if (isMouseDown && isTracking && !isLookingAtKnapping)
            {
                CancelTracking(player.Entity as EntityPlayer);
            }
            
            wasMouseDown = isMouseDown;
        }
        
        private void StartTracking(BlockSelection sel, EntityPlayer player)
        {
            targetBlockPos = sel.Position.Copy();
            
            // Calculate voxel position from hit location
            // HitPosition is 0-1 within the block, multiply by 16 for voxel grid
            targetVoxelX = (int)(sel.HitPosition.X * 16);
            targetVoxelZ = (int)(sel.HitPosition.Z * 16);
            
            // Clamp to valid range
            targetVoxelX = Math.Clamp(targetVoxelX, 0, 15);
            targetVoxelZ = Math.Clamp(targetVoxelZ, 0, 15);
            
            chargeStartTime = capi.World.ElapsedMilliseconds;
            isTracking = true;
            
            // Start charge animation and sound
            KnappingAnimationManager.StartChargeAnimation(player);
            soundManager.StartChargeSound(targetBlockPos);
        }
        
        private void UpdateCharging()
        {
            float chargeLevel = CalculateChargeLevel();
            soundManager.UpdateChargeTick(chargeLevel, targetBlockPos);
            
            // Strategy A: Continuously suppress vanilla hit animations every tick
            // The game engine triggers repeated "hit" animations when left-click is held,
            // so we must stop them continuously to prevent rapid up/down swinging
            var player = capi.World?.Player?.Entity;
            if (player?.AnimManager != null)
            {
                player.AnimManager.StopAnimation("hit");
                player.AnimManager.StopAnimation("breakhand");
                player.AnimManager.StopAnimation("holdhit");
            }
        }
        
        private void ReleaseStrike(EntityPlayer player)
        {
            if (!isTracking) return;
            
            // Get CURRENT block selection and voxel position (not where we started)
            var localPlayer = capi.World?.Player;
            var blockSel = localPlayer?.CurrentBlockSelection;
            
            // Must still be looking at a knapping surface
            if (!IsKnappingSurface(blockSel))
            {
                CancelTracking(player);
                return;
            }
            
            // Calculate voxel from CURRENT hit position
            int voxelX = (int)(blockSel.HitPosition.X * 16);
            int voxelZ = (int)(blockSel.HitPosition.Z * 16);
            voxelX = Math.Clamp(voxelX, 0, 15);
            voxelZ = Math.Clamp(voxelZ, 0, 15);
            
            float chargeLevel = CalculateChargeLevel();
            
            // Stop charge sound, play strike effects
            soundManager.StopChargeSound();
            soundManager.PlayStrikeSound(blockSel.Position, chargeLevel);
            KnappingAnimationManager.PlayStrikeAnimation(player, chargeLevel);
            
            // Send packet to server with CURRENT position
            channel.SendPacket(new ChargeReleasePacket
            {
                BlockX = blockSel.Position.X,
                BlockY = blockSel.Position.Y,
                BlockZ = blockSel.Position.Z,
                VoxelX = voxelX,
                VoxelZ = voxelZ,
                ChargeLevel = chargeLevel
            });
            
            ClearTracking();
        }
        
        private void CancelTracking(EntityPlayer player)
        {
            // Stop sounds and animation
            soundManager.StopChargeSound();
            KnappingAnimationManager.StopChargeAnimation(player);
            
            ClearTracking();
        }
        
        private void ClearTracking()
        {
            targetBlockPos = null;
            isTracking = false;
            chargeStartTime = 0;
        }
        
        private float CalculateChargeLevel()
        {
            if (!isTracking) return 0f;
            
            var config = PrecisionKnappingModSystem.Config;
            long elapsed = capi.World.ElapsedMilliseconds - chargeStartTime;
            return Math.Clamp(elapsed / (float)config.FullChargeTimeMs, 0f, 1f);
        }
        
        private bool IsKnappingSurface(BlockSelection sel)
        {
            if (sel == null) return false;
            var entity = capi.World.BlockAccessor.GetBlockEntity(sel.Position);
            return entity != null && entity.GetType().Name == "BlockEntityKnappingSurface";
        }
        
        /// <summary>
        /// Clean up resources on unload.
        /// </summary>
        public void Dispose()
        {
            soundManager?.Dispose();
            KnappingAnimationManager.Reset();
        }
    }

    #endregion
}

