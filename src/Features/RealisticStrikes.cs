using Vintagestory.API.Common;
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
    /// Client-side tracker for RealisticStrikes mode.
    /// Polls mouse button state and sends ChargeReleasePacket on mouse release.
    /// </summary>
    public class ChargeStateTracker
    {
        private readonly ICoreClientAPI capi;
        private IClientNetworkChannel channel;
        
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
            
            // Register network channel on client
            channel = api.Network.RegisterChannel("precisionknapping")
                .RegisterMessageType<ChargeReleasePacket>();
            
            // Register tick listener
            api.Event.RegisterGameTickListener(OnClientTick, 20); // 20ms = 50Hz
            
            api.Logger.Notification("[PrecisionKnapping] RealisticStrikes charge tracker initialized");
        }

        private void OnClientTick(float dt)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || !config.RealisticStrikes) return;

            var player = capi.World?.Player;
            if (player == null) return;

            var blockSel = player.CurrentBlockSelection;
            bool isLookingAtKnapping = IsKnappingSurface(blockSel);
            bool isMouseDown = capi.Input.InWorldMouseButton.Left;
            
            // Mouse down on knapping surface → start tracking
            if (isMouseDown && !wasMouseDown && isLookingAtKnapping)
            {
                StartTracking(blockSel);
            }
            
            // Mouse up while we have a target → send strike packet
            if (!isMouseDown && wasMouseDown && isTracking)
            {
                ReleaseStrike();
            }
            
            // Look away while charging → cancel (just clear local state)
            if (isMouseDown && isTracking && !isLookingAtKnapping)
            {
                CancelTracking();
            }
            
            wasMouseDown = isMouseDown;
        }
        
        private void StartTracking(BlockSelection sel)
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
        }
        
        private void ReleaseStrike()
        {
            if (!isTracking) return;
            
            // Get CURRENT block selection and voxel position (not where we started)
            var player = capi.World?.Player;
            var blockSel = player?.CurrentBlockSelection;
            
            // Must still be looking at a knapping surface
            if (!IsKnappingSurface(blockSel))
            {
                ClearTracking();
                return;
            }
            
            // Calculate voxel from CURRENT hit position
            int voxelX = (int)(blockSel.HitPosition.X * 16);
            int voxelZ = (int)(blockSel.HitPosition.Z * 16);
            voxelX = Math.Clamp(voxelX, 0, 15);
            voxelZ = Math.Clamp(voxelZ, 0, 15);
            
            float chargeLevel = CalculateChargeLevel();
            
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
        
        private void CancelTracking()
        {
            // Just clear state - no packet needed since server blocks vanilla anyway
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
    }

    #endregion
}
