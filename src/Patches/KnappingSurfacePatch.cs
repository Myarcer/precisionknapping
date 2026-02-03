using Vintagestory.API.Common;
using Vintagestory.API.Server;
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
            Type targetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                targetType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                if (targetType != null) break;
            }

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
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, bool mouseMode)
        {
            try
            {
                // Get the BlockEntity instance
                var entity = __instance as BlockEntity;
                if (entity == null) return true;

                var config = PrecisionKnappingModSystem.Config;

                // ========== CHARGED STRIKES MODE ==========
                // When enabled, ALWAYS block vanilla OnUseOver
                // All strikes are handled via ChargeReleasePacket
                if (config?.ChargedStrikes ?? false)
                {
                    return false; // Block vanilla completely
                }

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

                if (recipeVoxels == null || voxelPos.X >= 16 || voxelPos.Z >= 16)
                {
                    return true; // Allow vanilla behavior
                }

                // Check if clicked voxel exists and is protected
                bool voxelExists = currentVoxels[voxelPos.X, voxelPos.Z];
                bool isPartOfFinalShape = recipeVoxels[voxelPos.X, 0, voxelPos.Z];
                bool isProtected = isPartOfFinalShape && voxelExists;

                // ==================== ADVANCED MODE ====================
                if (advancedMode)
                {
                    return HandleAdvancedMode(entity, byPlayer, voxelPos, currentVoxels, recipeVoxels, voxelExists, isProtected, config);
                }

                // ==================== DEFAULT MODE ====================
                // Mistake tolerance - count mistakes, break stone when exceeding allowance
                // No durability penalty - you get 100% item or nothing
                if (isProtected)
                {
                    return HandleDefaultModeMistake(entity, byPlayer, voxelPos, config);
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
        /// Handle Default Mode mistake - counts toward breaking, NOT durability
        /// </summary>
        static bool HandleDefaultModeMistake(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, PrecisionKnappingConfig config)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle
            }

            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
            int newTotal = currentMistakes + 1;
            int allowance = config?.MistakeAllowance ?? 1;

            // Check if this exceeds allowance
            if (newTotal > allowance)
            {
                return HandleProtectedVoxelHit(entity, byPlayer, voxelPos,
                    $"Too many mistakes ({newTotal})! Stone destroyed.");
            }

            // Register mistake
            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);

            if (byPlayer is IServerPlayer serverPlayer)
            {
                int remaining = allowance - newTotal;
                if (remaining > 0)
                {
                    KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
                }
                else
                {
                    KnappingMessageHelper.NotifyFinalWarning(byPlayer);
                }
            }

            // Play warning sound
            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);

            // Manually remove the voxel for visual feedback
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);

            // Return FALSE to prevent vanilla from running (which could break recipe validation)
            return false;
        }

        /// <summary>
        /// Handle Advanced Mode mechanics: edge detection, line-breaking with fracture spread, mistake tracking
        /// </summary>
        static bool HandleAdvancedMode(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos,
            bool[,] currentVoxels, bool[,,] recipeVoxels, bool voxelExists, bool isProtected, PrecisionKnappingConfig config)
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
                    return HandleMistake(entity, byPlayer, voxelPos, config, 1, "Hit protected voxel!");
                }
                return true; // Safe edge click - allow vanilla
            }
            else
            {
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
                        entity.MarkDirty(true);
                        KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);
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
                        return HandleMistake(entity, byPlayer, voxelPos, config, 1, "Hit protected voxel!");
                    }
                    return true;
                }

                // Calculate fracture zone with spread around path
                var fractureZone = FractureCalculator.CalculateFractureZone(path, currentVoxels, config);

                // Count protected voxels in the entire fracture zone
                int protectedCount = FractureCalculator.CountProtectedInZone(fractureZone, recipeVoxels, currentVoxels);

                // If any protected voxels in fracture zone, register mistakes
                if (protectedCount > 0)
                {
                    // Check if this exceeds allowance
                    int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
                    int newTotal = currentMistakes + protectedCount;

                    if (newTotal > config.MistakeAllowance)
                    {
                        // Too many mistakes - break stone
                        return HandleProtectedVoxelHit(entity, byPlayer, voxelPos,
                            $"Too many mistakes ({newTotal})! Stone destroyed.");
                    }

                    // Register mistakes but continue
                    AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
                    int remaining = config.MistakeAllowance - newTotal;

                    // Only show durability % if scaling is enabled
                    if (config?.EnableDurabilityScaling ?? true)
                    {
                        float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                        KnappingMessageHelper.NotifyMistakes(byPlayer, protectedCount, remaining, durability);
                    }
                    else
                    {
                        KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
                    }
                    KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);
                }

                // Remove all voxels in the fracture zone
                foreach (var pos in fractureZone)
                {
                    currentVoxels[pos.X, pos.Y] = false;
                }

                // Remove any voxels that became disconnected from the recipe pattern
                AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);

                // Mark entity dirty so changes persist
                entity.MarkDirty(true);

                // Play chip sound for fracture effect
                KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);

                return false; // Prevent vanilla - we handled removal ourselves
            }
        }

        /// <summary>
        /// Handle a single mistake in Advanced Mode
        /// </summary>
        static bool HandleMistake(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos,
            PrecisionKnappingConfig config, int mistakeCount, string message)
        {
            if (entity.Api.Side != EnumAppSide.Server) return true;

            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
            int newTotal = currentMistakes + mistakeCount;

            if (newTotal > config.MistakeAllowance)
            {
                // Too many mistakes - break stone
                return HandleProtectedVoxelHit(entity, byPlayer, voxelPos,
                    $"Too many mistakes ({newTotal})! Stone destroyed.");
            }

            // Register mistake
            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
            int remaining = config.MistakeAllowance - newTotal;

            // Only show durability % if scaling is enabled
            if (config?.EnableDurabilityScaling ?? true)
            {
                float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                KnappingMessageHelper.NotifyMistake(byPlayer, remaining, durability);
            }
            else
            {
                KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
            }
            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);

            // Manually remove the voxel for visual feedback
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);

            // Return FALSE to prevent vanilla from running (which could break recipe validation)
            return false;
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

            // Clear mistake cache for this position
            AdvancedKnappingHelper.ClearMistakeCount(entity.Pos);

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
