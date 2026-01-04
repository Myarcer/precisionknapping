using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for BlockEntityKnappingSurface.CheckIfFinished
    /// Handles recipe completion with missing protected voxels (mistakes)
    /// Applies durability/quantity penalties based on mistake count
    /// </summary>
    [HarmonyPatch]
    public static class KnappingCompletionPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            Type targetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                targetType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                if (targetType != null) break;
            }

            if (targetType == null) return null;

            var method = targetType.GetMethod("CheckIfFinished",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            return method;
        }

        /// <summary>
        /// Prefix checks if recipe is "effectively complete" (all waste removed)
        /// If mistakes were made, we handle completion ourselves with penalties
        /// Only intercepts when waste is removed but protected voxels are missing
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, IPlayer byPlayer)
        {
            try
            {
                var entity = __instance as BlockEntity;
                if (entity == null || entity.Api.Side != EnumAppSide.Server) return true;

                int mistakes = AdvancedKnappingHelper.GetMistakeCount(entity);

                // Note: Don't return early for 0 mistakes - we need to check for bonus application

                // Get current voxels and recipe using reflection helper
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(entity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(entity);

                if (currentVoxels == null || selectedRecipe == null) return true;

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);

                if (recipeVoxels == null) return true;

                // Check if recipe is "effectively complete":
                // All non-protected voxels (waste) have been removed
                bool allWasteRemoved = true;
                bool hasMissingProtected = false;

                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        bool isProtected = recipeVoxels[x, 0, z];
                        bool voxelExists = currentVoxels[x, z];

                        // If this is waste (not protected) and still exists, not complete
                        if (!isProtected && voxelExists)
                        {
                            allWasteRemoved = false;
                        }

                        // If this is protected but missing, we have mistakes
                        if (isProtected && !voxelExists)
                        {
                            hasMissingProtected = true;
                        }
                    }
                }

                // Intercept if:
                // 1. Waste is removed AND protected voxels missing (mistakes with penalties)
                // 2. OR waste is removed AND no mistakes AND bonus is enabled (perfect completion with bonus)
                var config = PrecisionKnappingModSystem.Config;
                bool scalingEnabled = config?.EnableDurabilityScaling ?? true;
                bool bonusEnabled = config?.PerfectKnappingBonus > 0f && scalingEnabled;
                bool shouldIntercept = allWasteRemoved && (hasMissingProtected || (bonusEnabled && mistakes == 0));

                if (!shouldIntercept)
                {
                    return true; // Let vanilla handle
                }

                // Recipe is complete! Handle with bonuses or penalties

                // Get output item using reflection helper
                ItemStack resolvedStack = KnappingReflectionHelper.GetRecipeOutput(selectedRecipe, entity.Api.World);

                if (resolvedStack == null)
                    return true;

                // Clone the output stack
                ItemStack outStack = resolvedStack.Clone();
                string itemCode = outStack.Collectible?.Code?.ToString() ?? "";

                // Apply durability scaling based on item type (applies in both modes when EnableDurabilityScaling is on)
                if (AdvancedKnappingHelper.IsToolHead(itemCode) && scalingEnabled)
                {
                    // Tool heads in Advanced Mode: store durability RATIO as custom attribute
                    // This ratio will be transferred to the finished tool during crafting
                    float durabilityMult = AdvancedKnappingHelper.GetDurabilityMultiplier(mistakes);

                    // Store our custom attribute that survives crafting transfer
                    outStack.Attributes.SetFloat("precisionknapping:durabilityRatio", durabilityMult);

                    // Also set the tool head's own durability for display purposes
                    int maxDur = outStack.Collectible.GetMaxDurability(outStack);
                    if (maxDur > 0)
                    {
                        int newDur = Math.Max(1, (int)(maxDur * durabilityMult));
                        outStack.Attributes.SetInt("durability", newDur);
                    }

                    KnappingMessageHelper.NotifyCompletionDurability(byPlayer, mistakes, durabilityMult);
                }
                else if (!AdvancedKnappingHelper.IsToolHead(itemCode) && scalingEnabled)
                {
                    // Stackable items (arrowheads, fishing hooks) in Advanced Mode:
                    // Apply the same durability multiplier to quantity with rounding
                    // Example: 4 items * 1.25 = 5, 4 items * 0.75 = 3, 4 items * 1.13 = 4.52 -> 5
                    float multiplier = AdvancedKnappingHelper.GetDurabilityMultiplier(mistakes);
                    int originalQty = outStack.StackSize;
                    int finalQty = Math.Max(1, (int)Math.Round(originalQty * multiplier));
                    outStack.StackSize = finalQty;

                    if (finalQty != originalQty)
                    {
                        if (finalQty > originalQty)
                        {
                            int bonusPercent = (int)((multiplier - 1.0f) * 100);
                            if (byPlayer is IServerPlayer sp)
                                sp.SendMessage(0, $"[Precision Knapping] Perfect! +{bonusPercent}% -> {finalQty}/{originalQty} items", EnumChatType.Notification);
                        }
                        else
                        {
                            KnappingMessageHelper.NotifyCompletionQuantity(byPlayer, mistakes, finalQty, originalQty);
                        }
                    }
                    else if (multiplier > 1.0f)
                    {
                        // Bonus applied but quantity same due to rounding (e.g., 1 * 1.13 = 1)
                        int bonusPercent = (int)((multiplier - 1.0f) * 100);
                        if (byPlayer is IServerPlayer sp)
                            sp.SendMessage(0, $"[Precision Knapping] Perfect! +{bonusPercent}% (no extra item due to rounding)", EnumChatType.Notification);
                    }
                }

                // Give item to player
                if (!byPlayer.InventoryManager.TryGiveItemstack(outStack))
                {
                    entity.Api.World.SpawnItemEntity(outStack, entity.Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                // Clear mistake count
                AdvancedKnappingHelper.ClearMistakeCount(entity.Pos);

                // Remove the knapping surface block
                entity.Api.World.BlockAccessor.SetBlock(0, entity.Pos);

                // Play completion sound
                KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);

                return false; // Skip vanilla - we handled it
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KnappingCompletionPatch] Error: {ex.Message}");
                Console.WriteLine($"[KnappingCompletionPatch] Stack: {ex.StackTrace}");
                return true;
            }
        }
    }
}
