using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Charged Strikes input handling: Harmony patches on the knapping stones
    /// (Vintagestory.GameContent.ItemStone and ItemFlint).
    ///
    /// Vanilla: left click = OnHeldAttackStart -> Step returns false -> Stop knaps one voxel.
    /// Holding the mouse just repeats that, so the arm keeps swinging.
    ///
    /// Charged: Start begins a charge, Step returns true (the attack stays "held", no repeats),
    /// releasing the mouse calls Cancel(ReleasedMouse) then Stop, and Stop performs the strike.
    ///
    /// All patches run on both sides. Only the client decides the strike (it knows what the
    /// player aims at); the server just mirrors the animation for other players.
    /// Parameters are read by position (__args) so the parameter names may differ per class.
    /// </summary>
    internal static class KnappingStoneTargets
    {
        private static readonly string[] StoneTypes =
        {
            "Vintagestory.GameContent.ItemStone",
            "Vintagestory.GameContent.ItemFlint"
        };

        public static IEnumerable<MethodBase> Find(string methodName)
        {
            foreach (string typeName in StoneTypes)
            {
                var type = KnappingReflectionHelper.FindGameType(typeName);
                var method = type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (method != null) yield return method;
            }
        }

        /// <summary>
        /// The knapping surface at the selection, if it has a recipe selected
        /// (without a recipe, vanilla must run so the recipe dialog opens).
        /// </summary>
        public static BlockEntity GetActiveSurface(IWorldAccessor world, BlockSelection blockSel)
        {
            if (world == null || blockSel?.Position == null) return null;

            var entity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (!KnappingReflectionHelper.IsKnappingSurface(entity)) return null;

            return KnappingReflectionHelper.GetSelectedRecipe(entity) != null ? entity : null;
        }

        public static bool Enabled => PrecisionKnappingModSystem.Config?.ChargedStrikes == true;
    }

    /// <summary>
    /// Begin a charge instead of knapping right away.
    /// </summary>
    [HarmonyPatch]
    public static class KnappingStoneAttackStartPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => KnappingStoneTargets.Find("OnHeldAttackStart");

        // (ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        [HarmonyPrefix]
        static bool Prefix(object[] __args, ref EnumHandHandling handling)
        {
            try
            {
                if (!KnappingStoneTargets.Enabled) return true;

                var byEntity = __args[1] as EntityPlayer;
                var blockSel = __args[2] as BlockSelection;
                if (byEntity == null) return true;

                var surface = KnappingStoneTargets.GetActiveSurface(byEntity.World, blockSel);
                if (surface == null) return true;

                if (!byEntity.World.Claims.TryAccess(byEntity.Player, blockSel.Position, EnumBlockAccessFlags.Use))
                {
                    return true; // Let vanilla deny it the usual way
                }

                ChargeTracker.Begin(byEntity, blockSel.Position);
                ChargedStrikeAnimations.StartCharge(byEntity);

                // No default swing animation, no block breaking; keep calling Step/Stop
                handling = EnumHandHandling.PreventDefault;
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PrecisionKnapping] Charge start error: {ex.Message}");
                return true;
            }
        }
    }

    /// <summary>
    /// Keep the attack held while charging (vanilla returns false = instant knap).
    /// </summary>
    [HarmonyPatch]
    public static class KnappingStoneAttackStepPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => KnappingStoneTargets.Find("OnHeldAttackStep");

        // (float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
        [HarmonyPrefix]
        static bool Prefix(object[] __args, ref bool __result)
        {
            var byEntity = __args[2] as EntityAgent;
            var state = ChargeTracker.Get(byEntity);
            if (state == null) return true;

            state.LastStepMs = byEntity.World.ElapsedMilliseconds;
            ChargedStrikeClient.Instance?.OnChargeStep(byEntity, state);

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Allow the release. Any other reason (slot change, death...) cancels the strike.
    /// </summary>
    [HarmonyPatch]
    public static class KnappingStoneAttackCancelPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => KnappingStoneTargets.Find("OnHeldAttackCancel");

        // (float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        [HarmonyPrefix]
        static bool Prefix(object[] __args, ref bool __result)
        {
            var state = ChargeTracker.Get(__args[2] as EntityAgent);
            if (state == null) return true;

            if (!(__args[5] is EnumItemUseCancelReason reason) || reason != EnumItemUseCancelReason.ReleasedMouse)
            {
                state.Cancelled = true;
            }

            __result = true; // Allow: the game then calls OnHeldAttackStop
            return false;
        }
    }

    /// <summary>
    /// Release: strike (client sends it to the server) and play the swing.
    /// </summary>
    [HarmonyPatch]
    public static class KnappingStoneAttackStopPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => KnappingStoneTargets.Find("OnHeldAttackStop");

        // (float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        [HarmonyPrefix]
        static bool Prefix(object[] __args)
        {
            var byEntity = __args[2] as EntityAgent;
            var state = ChargeTracker.Get(byEntity);
            if (state == null) return true;

            ChargeTracker.End(byEntity);

            try
            {
                var config = PrecisionKnappingModSystem.Config;
                long heldMs = byEntity.World.ElapsedMilliseconds - state.StartMs;
                float charge = ChargeTracker.GetChargeLevel(heldMs, config);
                bool isStrike = !state.Cancelled && charge >= ChargeTracker.GetMinChargeLevel(config);

                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    // Client: decides what was hit, sends it, plays local feedback + animation
                    ChargedStrikeClient.Instance?.OnRelease(byEntity, state, __args[3] as BlockSelection, charge);
                }
                else if (isStrike)
                {
                    // Server: animation for other players (the strike itself arrives as a packet)
                    ChargedStrikeAnimations.PlayStrike(byEntity, charge);
                }
                else
                {
                    ChargedStrikeAnimations.StopCharge(byEntity);
                }
            }
            catch (Exception ex)
            {
                ChargedStrikeAnimations.StopCharge(byEntity);
                Console.WriteLine($"[PrecisionKnapping] Charge release error: {ex.Message}");
            }

            return false; // Never run the vanilla instant knap for a charged attack
        }
    }

    /// <summary>
    /// While charging, report no "hit" animation, so the game stops restarting the knap swing.
    /// Also cleans up a charge the engine abandoned without calling Stop.
    /// </summary>
    [HarmonyPatch]
    public static class KnappingStoneHitAnimationPatch
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => KnappingStoneTargets.Find("GetHeldTpHitAnimation");

        // (ItemSlot slot, Entity byEntity)
        [HarmonyPostfix]
        static void Postfix(object[] __args, ref string __result)
        {
            var entity = __args[1] as EntityAgent;
            var state = ChargeTracker.Get(entity);
            if (state == null) return;

            if (ChargeTracker.IsStale(state, entity.World.ElapsedMilliseconds))
            {
                ChargeTracker.End(entity);
                ChargedStrikeAnimations.StopCharge(entity);
                return;
            }

            __result = null;
        }
    }
}
