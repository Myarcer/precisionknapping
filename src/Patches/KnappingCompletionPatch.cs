using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for BlockEntityKnappingSurface.CheckIfFinished
    ///
    /// APPROACH: Instead of bypassing vanilla completion (which breaks the tutorial system),
    /// we manipulate the game state so vanilla completes normally:
    /// 1. Prefix: Detect "effectively complete" state (all waste removed, possibly with mistakes)
    /// 2. If mistakes exist: "heal" the voxel grid so vanilla sees a perfect match
    /// 3. Modify the recipe's ResolvedItemstack in-place with our bonuses/penalties
    /// 4. Return true - vanilla handles completion naturally (items, events, tutorial, block removal)
    /// 5. Postfix: Restore the recipe output to its original state (shared resource)
    /// </summary>
    [HarmonyPatch]
    public static class KnappingCompletionPatch
    {
        // Temporary storage for restoring recipe output after vanilla processes it
        private static ItemStack _originalResolvedStack;
        private static int _originalStackSize;
        private static bool _needsRestore;
        // Re-entry guard: vanilla CheckIfFinished can re-trigger after we heal voxels
        private static bool _isProcessing;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            Type targetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                targetType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                if (targetType != null) break;
            }

            if (targetType == null)
            {
                Console.WriteLine("[COMPLETION-PATCH] ERROR: Could not find BlockEntityKnappingSurface type!");
                return null;
            }

            var method = targetType.GetMethod("CheckIfFinished",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            if (method == null)
            {
                Console.WriteLine("[COMPLETION-PATCH] ERROR: Could not find CheckIfFinished method!");
            }
            else
            {
                Console.WriteLine($"[COMPLETION-PATCH] Found CheckIfFinished: {method}");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Detect effective completion, manipulate state for vanilla to process correctly.
        /// ALWAYS returns true so vanilla runs and triggers all events (including tutorial).
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, IPlayer byPlayer)
        {
            try
            {
                var entity = __instance as BlockEntity;
                if (entity == null || entity.Api.Side != EnumAppSide.Server) return true;

                // Re-entry guard: after we heal voxels, vanilla may call CheckIfFinished again
                if (_isProcessing) return true;

                _needsRestore = false;
                _isProcessing = true;

                int mistakes = AdvancedKnappingHelper.GetMistakeCount(entity);

                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(entity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(entity);

                if (currentVoxels == null || selectedRecipe == null) return true;

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
                if (recipeVoxels == null) return true;

                // Check completion state
                bool allWasteRemoved = true;
                bool hasMissingProtected = false;
                var missingProtectedPositions = new List<(int x, int z)>();

                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        bool isProtected = recipeVoxels[x, 0, z];
                        bool voxelExists = currentVoxels[x, z];

                        if (!isProtected && voxelExists)
                        {
                            allWasteRemoved = false;
                        }

                        if (isProtected && !voxelExists)
                        {
                            hasMissingProtected = true;
                            missingProtectedPositions.Add((x, z));
                        }
                    }
                }

                if (!allWasteRemoved) return true; // Not done yet

                var config = PrecisionKnappingModSystem.Config;
                bool scalingEnabled = config?.EnableDurabilityScaling ?? true;
                float bonusAmount = config?.PerfectKnappingBonus ?? 0.25f;
                bool bonusEnabled = bonusAmount > 0f && scalingEnabled;
                bool shouldModify = hasMissingProtected || (bonusEnabled && mistakes == 0);

                entity.Api.Logger.Debug($"[COMPLETION-PATCH] allWasteRemoved={allWasteRemoved}, hasMissingProtected={hasMissingProtected}, mistakes={mistakes}, shouldModify={shouldModify}");

                if (!shouldModify) return true; // Vanilla handles perfectly

                // === STEP 1: Heal voxel grid if mistakes were made ===
                // Fill in missing protected voxels so vanilla sees a perfect recipe match
                if (hasMissingProtected)
                {
                    entity.Api.Logger.Debug($"[COMPLETION-PATCH] Healing {missingProtectedPositions.Count} missing protected voxels");
                    foreach (var (x, z) in missingProtectedPositions)
                    {
                        currentVoxels[x, z] = true;
                    }
                    // Note: No MarkDirty needed - vanilla will remove the block anyway
                }

                // === STEP 2: Modify recipe output in-place ===
                ItemStack resolvedStack = KnappingReflectionHelper.GetRecipeOutput(selectedRecipe, entity.Api.World);
                if (resolvedStack == null) return true;

                string itemCode = resolvedStack.Collectible?.Code?.ToString() ?? "";
                int maxDur = resolvedStack.Collectible.GetMaxDurability(resolvedStack);
                bool hasDurability = maxDur > 0;
                bool isToolHead = AdvancedKnappingHelper.IsToolHead(itemCode);

                // Store original state for postfix restoration
                _originalResolvedStack = resolvedStack;
                _originalStackSize = resolvedStack.StackSize;
                _needsRestore = true;

                entity.Api.Logger.Debug($"[COMPLETION-PATCH] Modifying output: {itemCode}, isToolHead={isToolHead}, hasDurability={hasDurability}, mistakes={mistakes}");

                if (isToolHead && scalingEnabled)
                {
                    float durabilityMult = AdvancedKnappingHelper.GetDurabilityMultiplier(mistakes);
                    resolvedStack.Attributes.SetFloat("precisionknapping:durabilityRatio", durabilityMult);

                    if (maxDur > 0)
                    {
                        int newDur = Math.Max(1, (int)(maxDur * durabilityMult));
                        resolvedStack.Attributes.SetInt("durability", newDur);
                    }

                    KnappingMessageHelper.NotifyCompletionDurability(byPlayer, mistakes, durabilityMult);
                }
                else if (hasDurability && scalingEnabled)
                {
                    float durabilityMult = AdvancedKnappingHelper.GetDurabilityMultiplier(mistakes);
                    int newDur = Math.Max(1, (int)(maxDur * durabilityMult));
                    resolvedStack.Attributes.SetInt("durability", newDur);

                    KnappingMessageHelper.NotifyCompletionDurability(byPlayer, mistakes, durabilityMult);
                }
                else if (!isToolHead && !hasDurability && scalingEnabled)
                {
                    float multiplier = AdvancedKnappingHelper.GetDurabilityMultiplier(mistakes);
                    int originalQty = resolvedStack.StackSize;
                    int finalQty = Math.Max(1, (int)Math.Round(originalQty * multiplier));
                    resolvedStack.StackSize = finalQty;

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
                        int bonusPercent = (int)((multiplier - 1.0f) * 100);
                        if (byPlayer is IServerPlayer sp)
                            sp.SendMessage(0, $"[Precision Knapping] Perfect! +{bonusPercent}% (no extra item due to rounding)", EnumChatType.Notification);
                    }
                }

                // Clear mistake count before vanilla processes
                AdvancedKnappingHelper.ClearMistakeCount(entity.Pos);

                entity.Api.Logger.Debug("[COMPLETION-PATCH] State modified, letting vanilla handle completion");

                // Return true: vanilla sees a perfect voxel match + our modified output
                // Vanilla handles: giving item, removing block, firing events, tutorial notifications
                return true;
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                try
                {
                    var entity = __instance as BlockEntity;
                    entity?.Api?.Logger?.Error($"[COMPLETION-PATCH] Error: {ex.Message}\n{ex.StackTrace}");
                }
                catch { }
                Console.WriteLine($"[KnappingCompletionPatch] Error: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Postfix: Restore recipe output to original state after vanilla processed it.
        /// The recipe's ResolvedItemstack is a shared resource - must be restored for next use.
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            // Always clear re-entry guard
            _isProcessing = false;

            if (!_needsRestore || _originalResolvedStack == null) return;

            try
            {
                // Restore original stack size (for stackable items)
                _originalResolvedStack.StackSize = _originalStackSize;

                // Remove our custom attributes from the shared template
                _originalResolvedStack.Attributes.RemoveAttribute("precisionknapping:durabilityRatio");
                _originalResolvedStack.Attributes.RemoveAttribute("durability");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMPLETION-PATCH] Postfix restore error: {ex.Message}");
            }
            finally
            {
                _originalResolvedStack = null;
                _needsRestore = false;
            }
        }
    }
}