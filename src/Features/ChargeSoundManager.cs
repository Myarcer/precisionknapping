using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;

namespace precisionknapping
{
    /// <summary>
    /// Manages sounds for knapping charge mechanic.
    /// Uses repeated short sounds for charge feedback, rock impact on strike.
    /// </summary>
    public class ChargeSoundManager
    {
        private readonly ICoreClientAPI capi;
        private long _lastChargeSoundTime = 0;
        private const int CHARGE_SOUND_INTERVAL_MS = 150; // Play charge tick every 150ms
        
        // Sound asset locations
        private readonly AssetLocation _chargeTickSound;
        private readonly AssetLocation _strikeSound;
        
        public ChargeSoundManager(ICoreClientAPI api)
        {
            capi = api;
            
            // Charge tick: arcade-style tick sound that increases in frequency with charge
            // Using the game's UI tick sound for a subtle, non-intrusive indicator
            _chargeTickSound = new AssetLocation("game:sounds/tick");
            
            // Strike sound: actual knapping impact
            _strikeSound = new AssetLocation("game:sounds/player/knap2");
            
            capi.Logger.Notification("[PrecisionKnapping] ChargeSoundManager initialized");
        }
        
        /// <summary>
        /// Called on charge start - reset timing
        /// </summary>
        public void StartChargeSound(BlockPos pos)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || !config.EnableChargeSounds) return;
            
            _lastChargeSoundTime = capi.World.ElapsedMilliseconds;
            
            capi.Logger.Debug("[PrecisionKnapping] Charge sound started");
        }
        
        /// <summary>
        /// Update charge tick - play periodic sounds with increasing pitch
        /// </summary>
        /// <param name="chargePercent">0.0 to 1.0</param>
        /// <param name="pos">Block position</param>
        public void UpdateChargeTick(float chargePercent, BlockPos pos)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || !config.EnableChargeSounds) return;
            if (pos == null) return;
            
            long now = capi.World.ElapsedMilliseconds;
            
            // Calculate interval - gets faster as charge increases
            int interval = (int)(CHARGE_SOUND_INTERVAL_MS * (1.0f - chargePercent * 0.6f));
            interval = Math.Max(interval, 50); // Min 50ms between sounds
            
            if (now - _lastChargeSoundTime >= interval)
            {
                // Pitch increases with charge
                float pitch = config.ChargeSoundMinPitch + (config.ChargeSoundMaxPitch - config.ChargeSoundMinPitch) * chargePercent;
                float volume = 0.2f + (chargePercent * 0.3f);
                
                capi.World.PlaySoundAt(
                    _chargeTickSound,
                    pos.X + 0.5,
                    pos.Y + 0.5,
                    pos.Z + 0.5,
                    null,
                    randomizePitch: false,
                    range: 8f,
                    volume: volume
                );
                
                _lastChargeSoundTime = now;
            }
        }
        
        /// <summary>
        /// Stop charge sound (cleanup)
        /// </summary>
        public void StopChargeSound()
        {
            _lastChargeSoundTime = 0;
        }
        
        /// <summary>
        /// Play the strike/impact sound on release.
        /// </summary>
        public void PlayStrikeSound(BlockPos pos, float chargeLevel)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (config == null || !config.EnableChargeSounds) return;
            if (pos == null) return;
            
            try
            {
                // Louder/deeper with higher charge
                float pitch = 0.8f + (chargeLevel * 0.2f);
                float volume = 0.5f + (chargeLevel * 0.5f);
                
                capi.World.PlaySoundAt(
                    _strikeSound,
                    pos.X + 0.5,
                    pos.Y + 0.5,
                    pos.Z + 0.5,
                    null,
                    randomizePitch: true,
                    range: 16f,
                    volume: volume
                );
                
                capi.Logger.Debug($"[PrecisionKnapping] Strike sound played at charge {chargeLevel:F2}");
            }
            catch (Exception ex)
            {
                capi.Logger.Warning($"[PrecisionKnapping] Failed to play strike sound: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            StopChargeSound();
        }
    }
}
