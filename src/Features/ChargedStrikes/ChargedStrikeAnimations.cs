using System;
using Vintagestory.API.Common;

namespace precisionknapping
{
    /// <summary>
    /// Player animations for Charged Strikes. Runs on both sides: the client animates its own
    /// player, the server animates the player for everyone else watching.
    ///
    /// WHY THE OLD VERSION KEPT SWINGING:
    /// The game picks the "hit" animation from ItemStone/ItemFlint.GetHeldTpHitAnimation ("knap")
    /// and restarts it every time it finishes while the left mouse is down. The old code tried to
    /// stop it every tick, which lost the fight. Now KnappingStonePatches returns null there while
    /// charging (the game then has no hit animation to restart) and this class plays the
    /// held-back pose and the single release swing itself.
    /// </summary>
    public static class ChargedStrikeAnimations
    {
        private const int MaxStrikeMs = 1500;
        private static bool warnedMissingChargeAnim;

        /// <summary>
        /// Arm pulled back. The animation holds/loops until StopCharge.
        /// </summary>
        public static void StartCharge(EntityAgent entity)
        {
            var animManager = entity?.AnimManager;
            var config = PrecisionKnappingModSystem.Config;
            if (animManager == null || config == null) return;

            // A previous swing may still be finishing
            animManager.StopAnimation(config.StrikeAnimation);

            if (!string.IsNullOrEmpty(config.ChargeAnimation) && !animManager.StartAnimation(config.ChargeAnimation)
                && !warnedMissingChargeAnim)
            {
                warnedMissingChargeAnim = true;
                entity.World.Logger.Warning($"[PrecisionKnapping] Charge animation '{config.ChargeAnimation}' not found on the player. Check ChargeAnimation in the config.");
            }
        }

        public static void StopCharge(EntityAgent entity)
        {
            var config = PrecisionKnappingModSystem.Config;
            if (string.IsNullOrEmpty(config?.ChargeAnimation)) return;
            entity?.AnimManager?.StopAnimation(config.ChargeAnimation);
        }

        /// <summary>
        /// One release swing. Faster with more charge. Stopped after one pass, because the
        /// vanilla knap animation repeats until something stops it.
        /// </summary>
        public static void PlayStrike(EntityAgent entity, float charge)
        {
            StopCharge(entity);

            var animManager = entity?.AnimManager;
            string code = PrecisionKnappingModSystem.Config?.StrikeAnimation;
            if (animManager == null || string.IsNullOrEmpty(code)) return;

            // Restart from the first frame, even if the last swing is still finishing
            animManager.StopAnimation(code);
            (animManager as AnimationManager)?.ResetAnimation(code);

            float speedFactor = 1f + 0.6f * Math.Clamp(charge, 0f, 1f);
            bool started;
            var registered = entity.Properties?.Client?.AnimationsByMetaCode;
            if (registered != null && registered.TryGetValue(code, out var meta))
            {
                // Clone: the registered metadata is shared by every player
                var swing = meta.Clone();
                swing.AnimationSpeed = meta.AnimationSpeed * speedFactor;
                started = animManager.StartAnimation(swing);
            }
            else
            {
                started = animManager.StartAnimation(code);
            }

            if (!started) return;

            var world = entity.World;
            long startMs = world.ElapsedMilliseconds;
            int fallbackMs = (int)(500 / speedFactor);
            long listenerId = 0;
            listenerId = world.RegisterGameTickListener(dt =>
            {
                long elapsed = world.ElapsedMilliseconds - startMs;
                if (elapsed < 100) return; // Give the animator a frame or two to pick it up

                // Stop near the end of the first pass. If this side has no animator state
                // to read (can happen server-side), fall back to a fixed swing time.
                var state = animManager.GetAnimationState(code);
                bool finished = state == null
                    ? elapsed >= fallbackMs
                    : !state.Running || state.Iterations > 0 || state.AnimProgress >= 0.85f;

                if (finished || elapsed > MaxStrikeMs)
                {
                    animManager.StopAnimation(code);
                    world.UnregisterGameTickListener(listenerId);
                }
            }, 20);
        }
    }
}
