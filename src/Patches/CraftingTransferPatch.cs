using Vintagestory.API.Common;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for CollectibleObject.OnCreatedByCrafting
    /// Transfers durability ratio from precision-knapped tool heads to finished tools.
    ///
    /// SAFETY DESIGN:
    /// - Postfix with Priority.Last: Runs after ALL other mods' patches
    /// - Attribute-gated: Only acts if our specific namespaced attribute exists
    /// - Non-destructive: Won't overwrite if another mod already reduced durability
    /// - Tool-type check: Only affects outputs that are actually tools (have Tool attribute)
    /// - Silent fallback: Any error silently falls through to vanilla behavior
    ///
    /// Why this is needed:
    /// - Vanilla durability averaging (GridRecipe.AverageDurability) skips IsTool ingredients
    /// - Tool heads are marked as IsTool, so their durability is ignored when crafting
    /// - We store "precisionknapping:durabilityRatio" on damaged tool heads during knapping
    /// - This patch detects that attribute and applies it to the finished tool
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.Last)] // Run after other mods to avoid conflicts
    public static class CraftingDurabilityTransferPatch
    {
        // Namespaced attribute key to avoid collision with other mods
        private const string DURABILITY_RATIO_KEY = "precisionknapping:durabilityRatio";

        /// <summary>
        /// Target CollectibleObject.OnCreatedByCrafting with explicit signature
        /// </summary>
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var method = typeof(CollectibleObject).GetMethod("OnCreatedByCrafting",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(ItemSlot[]), typeof(ItemSlot), typeof(GridRecipe) },
                null);

            if (method == null)
            {
                Console.WriteLine("[CRAFTING-PATCH] ERROR: Could not find OnCreatedByCrafting method!");
            }
            else
            {
                Console.WriteLine("[CRAFTING-PATCH] Found OnCreatedByCrafting method: " + method);
            }

            return method;
        }

        /// <summary>
        /// Postfix runs AFTER vanilla and other mod patches.
        /// Only modifies output if:
        /// 1. Output is a tool-type item (has Tool attribute in JSON)
        /// 2. An input has our specific durability ratio attribute
        /// 3. Output doesn't already have reduced durability (another mod didn't touch it)
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(
            ItemSlot[] allInputslots,
            ItemSlot outputSlot,
            GridRecipe byRecipe,
            CollectibleObject __instance)
        {
            try
            {
                // === GUARD CLAUSE 1: Valid output ===
                if (outputSlot?.Itemstack == null) return;

                var outputCollectible = outputSlot.Itemstack.Collectible;
                if (outputCollectible == null) return;

                // === GUARD CLAUSE 2: Output uses durability ===
                int outputMaxDur = outputCollectible.GetMaxDurability(outputSlot.Itemstack);
                if (outputMaxDur <= 0) return;

                // === GUARD CLAUSE 3: Output must be a tool-type item ===
                bool isToolOutput = outputCollectible.Tool != null ||
                                    IsToolByCode(outputCollectible.Code?.Path);
                if (!isToolOutput) return;

                // === GUARD CLAUSE 4: Check if another mod already modified durability ===
                int currentDur = outputCollectible.GetRemainingDurability(outputSlot.Itemstack);
                if (currentDur < outputMaxDur && currentDur > 0)
                {
                    return; // Another mod already reduced durability - respect that
                }

                // === SEARCH: Find our durability ratio in inputs ===
                float? foundRatio = null;

                foreach (var slot in allInputslots)
                {
                    if (slot?.Itemstack?.Attributes == null) continue;

                    float ratio = slot.Itemstack.Attributes.GetFloat(DURABILITY_RATIO_KEY, -1f);

                    // Accept any valid ratio that's not exactly 1.0 (both penalties < 1 and bonuses > 1)
                    if (ratio > 0f && Math.Abs(ratio - 1.0f) > 0.001f)
                    {
                        foundRatio = ratio;
                        break;
                    }
                }

                // === APPLY: Transfer ratio to output ===
                // Note: The ratio attribute may persist on the tool via vanilla CopyAttributesFrom.
                // This is harmless - it's namespaced metadata that doesn't affect gameplay.
                if (foundRatio.HasValue)
                {
                    int newDurability = Math.Max(1, (int)(outputMaxDur * foundRatio.Value));
                    outputSlot.Itemstack.Attributes.SetInt("durability", newDurability);
                }
            }
            catch (Exception)
            {
                // Silent fail - never break crafting for any reason
            }
        }

        /// <summary>
        /// Fallback check for tool-type items by code path.
        /// Some modded tools might not have the Tool attribute but are still tools.
        /// </summary>
        private static bool IsToolByCode(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return false;

            string lower = codePath.ToLowerInvariant();

            // Common tool patterns
            return lower.Contains("axe") || lower.Contains("pickaxe") ||
                   lower.Contains("shovel") || lower.Contains("hoe") ||
                   lower.Contains("knife") || lower.Contains("sword") ||
                   lower.Contains("scythe") || lower.Contains("hammer") ||
                   lower.Contains("chisel") || lower.Contains("saw") ||
                   lower.Contains("cleaver") || lower.Contains("shears") ||
                   lower.Contains("spear") ||
                   lower.Contains("sickle") || lower.Contains("mattock");
        }
    }
}
