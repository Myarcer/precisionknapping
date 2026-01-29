using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using System;

namespace precisionknapping
{
    /// <summary>
    /// Manages knapping charge animations.
    /// Provides hooks for custom animation integration while using vanilla animations as fallback.
    /// 
    /// To add custom animations:
    /// 1. Create animation in VS Model Creator with code "knapping-charge"
    /// 2. Add to player entity via JSON patch
    /// 3. This manager will automatically use it
    /// </summary>
    public static class KnappingAnimationManager
    {
        private static bool _isCharging = false;
        private static string _chargeAnimCode = "knapping-charge";
        private static string _fallbackAnimCode = "interactstatic"; // Vanilla fallback
        
        /// <summary>
        /// Start the charge animation (arm pulling back).
        /// </summary>
        public static void StartChargeAnimation(EntityPlayer player)
        {
            var logger = PrecisionKnappingModSystem.Instance?.Api?.Logger;
            var config = PrecisionKnappingModSystem.Config;
            
            logger?.Debug($"[PrecisionKnapping] StartChargeAnimation called. Player={player != null}, EnableAnim={config?.EnableChargeAnimation}, IsCharging={_isCharging}");
            
            if (player == null) 
            {
                logger?.Debug("[PrecisionKnapping] Player is null, skipping animation");
                return;
            }
            if (config == null || !config.EnableChargeAnimation) 
            {
                logger?.Debug("[PrecisionKnapping] Animation disabled, skipping");
                return;
            }
            if (_isCharging) 
            {
                logger?.Debug("[PrecisionKnapping] Already charging, skipping");
                return;
            }
            
            try
            {
                var animManager = player.AnimManager;
                if (animManager == null) 
                {
                    logger?.Warning("[PrecisionKnapping] AnimManager is null");
                    return;
                }
                
                // Try to stop any existing hit animations to prevent repetitive swinging
                logger?.Debug("[PrecisionKnapping] Stopping vanilla hit animations");
                StopVanillaHitAnimations(animManager);
                
                // Try custom animation first, fall back to vanilla
                logger?.Debug($"[PrecisionKnapping] Trying animation: {_chargeAnimCode}");
                bool started = TryStartAnimation(animManager, _chargeAnimCode);
                if (!started)
                {
                    logger?.Debug($"[PrecisionKnapping] Custom anim failed, trying fallback: {_fallbackAnimCode}");
                    TryStartAnimation(animManager, _fallbackAnimCode);
                }
                
                _isCharging = true;
                logger?.Debug("[PrecisionKnapping] Charge animation started, _isCharging=true");
            }
            catch (Exception ex)
            {
                logger?.Warning($"[PrecisionKnapping] Failed to start charge animation: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop the charge animation.
        /// </summary>
        public static void StopChargeAnimation(EntityPlayer player)
        {
            if (player == null) return;
            if (!_isCharging) return;
            
            try
            {
                var animManager = player.AnimManager;
                if (animManager == null) return;
                
                // Stop our animations
                animManager.StopAnimation(_chargeAnimCode);
                animManager.StopAnimation(_fallbackAnimCode);
                
                _isCharging = false;
            }
            catch (Exception ex)
            {
                PrecisionKnappingModSystem.Instance?.Api?.Logger.Warning(
                    $"[PrecisionKnapping] Failed to stop charge animation: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Play the strike animation (arm swinging down).
        /// </summary>
        public static void PlayStrikeAnimation(EntityPlayer player, float chargeLevel)
        {
            if (player == null) return;
            if (!PrecisionKnappingModSystem.Config.EnableChargeAnimation) return;
            
            try
            {
                var animManager = player.AnimManager;
                if (animManager == null) return;
                
                // Stop charge animation first
                StopChargeAnimation(player);
                
                // Play hit animation - speed based on charge (faster swing for more charge)
                float animSpeed = 1.0f + (chargeLevel * 0.5f);
                
                // Try custom strike animation, fall back to vanilla hit
                bool started = TryStartAnimation(animManager, "knapping-strike", animSpeed);
                if (!started)
                {
                    TryStartAnimation(animManager, "hit", animSpeed);
                }
            }
            catch (Exception ex)
            {
                PrecisionKnappingModSystem.Instance?.Api?.Logger.Warning(
                    $"[PrecisionKnapping] Failed to play strike animation: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop vanilla hit animations to prevent repetitive swinging during charge.
        /// </summary>
        private static void StopVanillaHitAnimations(IAnimationManager animManager)
        {
            try
            {
                animManager.StopAnimation("hit");
                animManager.StopAnimation("breakhand");
                animManager.StopAnimation("holdhit");
            }
            catch { /* Ignore if animations don't exist */ }
        }
        
        /// <summary>
        /// Try to start an animation by code. Returns true if successful.
        /// </summary>
        private static bool TryStartAnimation(IAnimationManager animManager, string code, float speed = 1.0f)
        {
            try
            {
                // StartAnimation returns true if animation exists and was started
                animManager.StartAnimation(code);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Reset state (for cleanup on mod unload).
        /// </summary>
        public static void Reset()
        {
            _isCharging = false;
        }
    }
}
