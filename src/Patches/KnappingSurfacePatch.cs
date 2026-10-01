using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for BlockEntityKnappingSurface.OnUseOver
    /// Intercepts voxel removal to validate against recipe patterns
    /// </summary>
    [HarmonyPatch]
    public static class KnappingSurfacePatch
    {
        /// <summary>
        /// Target the OnUseOver method in BlockEntityKnappingSurface
        /// </summary>
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            // Find the BlockEntityKnappingSurface type
            Type targetType = KnappingReflectionHelper.FindGameType("Vintagestory.GameContent.BlockEntityKnappingSurface");

            if (targetType == null)
            {
                throw new Exception("[HARMONY-PATCH] BlockEntityKnappingSurface type not found!");
            }

            // Get the OnUseOver method
            var method = targetType.GetMethod("OnUseOver",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(IPlayer), typeof(Vec3i), typeof(BlockFacing), typeof(bool) },
                null);

            if (method == null)
            {
                throw new Exception("[HARMONY-PATCH] OnUseOver method not found!");
            }

            return method;
        }

        /// <summary>
        /// Prefix method - runs BEFORE vanilla OnUseOver
        /// Return false to prevent vanilla method from running
        ///
        /// MODES:
        /// - Default: Mistake tolerance - exceeding MistakeAllowance breaks stone (no durability penalty)
        /// - Advanced: Edge enforcement + mistake tracking with durability scaling
        /// - Charged Strikes (either mode): only strikes sent by ChargedStrikeServer may knap
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, bool mouseMode)
        {
            try
            {
                // Get the BlockEntity instance
                var entity = __instance as BlockEntity;
                if (entity == null || voxelPos == null) return true;

                var config = PrecisionKnappingModSystem.Config;
                bool advancedMode = config?.AdvancedMode ?? false;

                // Get voxel data using reflection helper
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(__instance);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(__instance);

                if (selectedRecipe == null || currentVoxels == null)
                {
                    return true; // Allow vanilla behavior - no recipe selected
                }

                // Get recipe voxel pattern
                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);

                if (recipeVoxels == null || voxelPos.X < 0 || voxelPos.X >= 16 || voxelPos.Z < 0 || voxelPos.Z >= 16)
                {
                    return true; // Allow vanilla behavior
                }

                // ==================== CHARGED STRIKES ====================
                // Only charged strikes may knap. A plain click (vanilla packet) is ignored.
                ChargedStrike strike = ChargedStrikeContext.Current;
                if (config?.ChargedStrikes == true && strike == null)
                {
                    // The client may have predicted a removal - resend the real state
                    if (entity.Api.Side == EnumAppSide.Server) entity.MarkDirty(true);
                    return false;
                }

                // Check if clicked voxel exists and is protected
                bool voxelExists = currentVoxels[voxelPos.X, voxelPos.Z];
                bool isPartOfFinalShape = recipeVoxels[voxelPos.X, 0, voxelPos.Z];
                bool isProtected = isPartOfFinalShape && voxelExists;

                // A fully charged strike knocks off a whole flake (except into the interior in
                // Advanced Mode, where it widens the fracture cone instead - see HandleAdvancedMode)
                if (strike != null && strike.IsPower && voxelExists && entity.Api.Side == EnumAppSide.Server)
                {
                    bool interiorFracture = advancedMode &&
                        !AdvancedKnappingHelper.IsEdgeVoxel(voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);

                    if (!interiorFracture && (config?.PowerStrikeRadius ?? 0) > 0)
                    {
                        return HandlePowerStrike(entity, byPlayer, voxelPos, currentVoxels, recipeVoxels, config);
                    }
                }

                // ==================== ADVANCED MODE ====================
                if (advancedMode)
                {
                    float fractureMultiplier = strike?.FractureMultiplier ?? 1f;
                    return HandleAdvancedMode(entity, byPlayer, voxelPos, currentVoxels, recipeVoxels, voxelExists, isProtected, config, fractureMultiplier);
                }

                // ==================== DEFAULT MODE ====================
                // Mistake tolerance - count mistakes, break stone when exceeding allowance
                // No durability penalty - you get 100% item or nothing
                if (isProtected)
                {
                    return HandleSingleMistake(entity, byPlayer, voxelPos, config);
                }

                return true; // Allow vanilla behavior - safe voxel

            }
            catch (Exception ex)
            {
                Console.WriteLine("[HARMONY-PATCH] Error in prefix: " + ex.Message);
                Console.WriteLine("[HARMONY-PATCH] Stack: " + ex.StackTrace);
                return true; // Allow vanilla behavior on error
            }
        }

        /// <summary>
        /// Handle Advanced Mode mechanics: edge detection, line-breaking with fracture spread, mistake tracking
        /// </summary>
        /// <param name="fractureMultiplier">Scales the fracture cone spread (charged strikes: light hits = smaller flakes)</param>
        static bool HandleAdvancedMode(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos,
            bool[,] currentVoxels, bool[,,] recipeVoxels, bool voxelExists, bool isProtected,
            PrecisionKnappingConfig config, float fractureMultiplier)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle
            }

            // Voxel must exist to be clicked
            if (!voxelExists)
            {
                return true; // Allow vanilla - clicking empty space
            }

            // Check if this is an edge voxel
            bool isEdge = AdvancedKnappingHelper.IsEdgeVoxel(voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);

            if (isEdge)
            {
                // Clicking an edge voxel - check if protected
                if (isProtected)
                {
                    return HandleSingleMistake(entity, byPlayer, voxelPos, config);
                }
                return true; // Safe edge click - allow vanilla
            }

            // Clicking a non-edge voxel - could be risky OR could be enclosed waste pocket

            // FIRST: Check if this is an enclosed waste pocket (like macehead center)
            // This must be checked BEFORE path finding, because FindPathToNearestEdge
            // traverses through protected voxels and would find a path anyway
            if (!isProtected)
            {
                var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(
                    voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);
                if (pocket.Count > 0)
                {
                    // Safe removal of enclosed waste pocket
                    foreach (var pos in pocket)
                        currentVoxels[pos.X, pos.Y] = false;

                    FinishServerRemoval(entity, byPlayer);
                    return false;
                }
            }

            // Find path to nearest edge and calculate fracture zone
            var path = AdvancedKnappingHelper.FindPathToNearestEdge(voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);

            if (path.Count == 0)
            {
                // No path to edge found and not an enclosed pocket
                if (isProtected)
                {
                    return HandleSingleMistake(entity, byPlayer, voxelPos, config);
                }
                return true;
            }

            // Calculate fracture zone with spread around path
            var fractureZone = FractureCalculator.CalculateFractureZone(path, currentVoxels, config, fractureMultiplier);

            // Count protected voxels in the entire fracture zone
            int protectedCount = FractureCalculator.CountProtectedInZone(fractureZone, recipeVoxels, currentVoxels);

            // If any protected voxels in fracture zone, register mistakes (may destroy the stone)
            if (protectedCount > 0 && !TryRegisterMistakes(entity, byPlayer, voxelPos, config, protectedCount))
            {
                return false;
            }

            // Remove all voxels in the fracture zone
            foreach (var pos in fractureZone)
            {
                currentVoxels[pos.X, pos.Y] = false;
            }

            // Remove any voxels that became disconnected from the recipe pattern
            AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);

            FinishServerRemoval(entity, byPlayer);
            return false; // Prevent vanilla - we handled removal ourselves
        }

        /// <summary>
        /// Fully charged strike: knocks off a diamond-shaped flake around the target
        /// (PowerStrikeRadius 1 = target + 4 neighbours). Fast for clearing bulk waste,
        /// but every protected voxel inside the flake counts as a mistake.
        /// </summary>
        static bool HandlePowerStrike(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos,
            bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            int radius = Math.Clamp(config.PowerStrikeRadius, 0, 3);
            var flake = new System.Collections.Generic.List<Vec2i>();
            int protectedHits = 0;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Math.Abs(dx) + Math.Abs(dz) > radius) continue;

                    int x = voxelPos.X + dx;
                    int z = voxelPos.Z + dz;
                    if (x < 0 || x >= 16 || z < 0 || z >= 16 || !currentVoxels[x, z]) continue;

                    flake.Add(new Vec2i(x, z));
                    if (recipeVoxels[x, 0, z]) protectedHits++;
                }
            }

            if (protectedHits > 0 && !TryRegisterMistakes(entity, byPlayer, voxelPos, config, protectedHits))
            {
                return false; // Stone destroyed
            }

            foreach (var pos in flake)
            {
                currentVoxels[pos.X, pos.Y] = false;
            }

            AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);

            // The striker's client already played its own impact sound
            KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);
            FinishServerRemoval(entity, byPlayer, playChip: false);
            return false;
        }

        /// <summary>
        /// Shared tail for every removal we perform instead of vanilla:
        /// save + sync the voxels, play the chip sound, and run the completion check.
        /// </summary>
        static void FinishServerRemoval(BlockEntity entity, IPlayer byPlayer, bool playChip = true)
        {
            if (!AdvancedKnappingHelper.HasAnyVoxel(KnappingReflectionHelper.GetCurrentVoxels(entity)))
            {
                // Same as vanilla: an empty surface disappears
                AdvancedKnappingHelper.ClearMistakeCount(entity);
                entity.Api.World.BlockAccessor.SetBlock(0, entity.Pos);
                return;
            }

            entity.MarkDirty(true);

            if (playChip) KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos);

            // CRITICAL: Must trigger completion check since we bypassed vanilla OnUseOver
            // Without this, if this removal took the last waste voxel, the recipe never completes
            KnappingReflectionHelper.CallCheckIfFinished(entity, byPlayer);
        }

        /// <summary>
        /// One protected voxel was hit: count the mistake and chip that voxel away.
        /// </summary>
        static bool HandleSingleMistake(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, PrecisionKnappingConfig config)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle
            }

            if (!TryRegisterMistakes(entity, byPlayer, voxelPos, config, 1))
            {
                return false; // Stone destroyed
            }

            // Manually remove the voxel for visual feedback
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);

            // Return FALSE to prevent vanilla from running (which could break recipe validation)
            return false;
        }

        /// <summary>
        /// Add mistakes to the running count and tell the player.
        /// Returns false if the allowance was exceeded and the stone was destroyed.
        /// </summary>
        static bool TryRegisterMistakes(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos,
            PrecisionKnappingConfig config, int count)
        {
            int allowance = config?.MistakeAllowance ?? 1;
            int newTotal = AdvancedKnappingHelper.GetMistakeCount(entity) + count;

            if (newTotal > allowance)
            {
                HandleProtectedVoxelHit(entity, byPlayer, voxelPos,
                    $"Too many mistakes ({newTotal})! Stone destroyed.");
                return false;
            }

            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
            int remaining = allowance - newTotal;

            // Advanced Mode shows the durability the tool will end up with
            if ((config?.AdvancedMode ?? false) && (config?.EnableDurabilityScaling ?? true))
            {
                float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                if (count > 1) KnappingMessageHelper.NotifyMistakes(byPlayer, count, remaining, durability);
                else KnappingMessageHelper.NotifyMistake(byPlayer, remaining, durability);
            }
            else if (remaining > 0)
            {
                KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
            }
            else
            {
                KnappingMessageHelper.NotifyFinalWarning(byPlayer);
            }

            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos);
            return true;
        }

        /// <summary>
        /// Handle complete failure - break the stone
        /// </summary>
        static bool HandleProtectedVoxelHit(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, string message)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle it
            }

            // Clear mistake count for this surface
            AdvancedKnappingHelper.ClearMistakeCount(entity);

            // Play stone breaking sound
            KnappingSoundHelper.PlayStoneBreakSound(entity.Api, entity.Pos);
            KnappingMessageHelper.NotifyStoneDestroyed(byPlayer, message);

            try
            {
                BlockPos breakPos = entity.Pos.Copy();
                entity.Api.World.BlockAccessor.BreakBlock(breakPos, byPlayer);
            }
            catch (Exception ex)
            {
                entity.Api.Logger.Error($"[CLEANUP] BreakBlock failed: {ex.Message}");
            }

            return false; // Prevent vanilla OnUseOver
        }
    }
}
