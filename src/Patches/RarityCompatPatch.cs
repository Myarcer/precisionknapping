using Vintagestory.API.Common;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Compatibility patch for Item Rarity.
    ///
    /// Item Rarity scales max durability VIRTUALLY: its postfix on GetMaxDurability
    /// multiplies the result by its "maxdurabilitymul" attribute. Rarity is assigned in
    /// a postfix on ConsumeCraftingIngredients - AFTER our crafting transfer patch wrote
    /// durability = BASE max * ratio in OnCreatedByCrafting.
    ///
    /// A first attempt patched ConsumeCraftingIngredients as well, but Harmony executes
    /// higher-priority postfixes first: our Priority.Last postfix ran BEFORE Item
    /// Rarity's priority-0 postfix, recomputed against the pre-rarity max, found the
    /// stored value unchanged and skipped. Result stayed 75/84 instead of 105/84.
    ///
    /// This patch hooks Item Rarity's Rarity.ApplyRarity directly instead. The postfix
    /// runs inside their apply, immediately after the multipliers are written, with no
    /// ordering dependency on any third-party patch. When Item Rarity is not loaded,
    /// IsAvailable is false and this patch is never applied.
    /// </summary>
    [HarmonyPatch]
    public static class RarityCompatPatch
    {
        private const string RatioKey = "precisionknapping:durabilityRatio";
        private const string RarityType = "ItemRarity.Rarities.Rarity";

        private static readonly bool _isAvailable =
            AccessTools.TypeByName(RarityType) != null;

        /// <summary>True only when Item Rarity is loaded.</summary>
        public static bool IsAvailable => _isAvailable;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var rarityType = AccessTools.TypeByName(RarityType);
            if (rarityType == null)
            {
                Console.WriteLine("[RARITY-COMPAT] ERROR: ItemRarity.Rarities.Rarity not found!");
                return null;
            }

            var method = AccessTools.Method(rarityType, "ApplyRarity");
            if (method == null)
            {
                Console.WriteLine("[RARITY-COMPAT] ERROR: Could not find Rarity.ApplyRarity!");
            }

            return method;
        }

        [HarmonyPostfix]
        public static void Postfix(ItemStack itemStack)
        {
            try
            {
                if (itemStack?.Attributes == null || itemStack.Collectible == null) return;

                float ratio = itemStack.Attributes.GetFloat(RatioKey, -1f);
                if (ratio <= 0f) return;

                int effectiveMax = itemStack.Collectible.GetMaxDurability(itemStack);
                if (effectiveMax <= 0) return;

                int newDurability = Math.Max(1, (int)(effectiveMax * ratio));
                int current = itemStack.Attributes.GetInt("durability", -1);
                if (current == newDurability) return;

                itemStack.Attributes.SetInt("durability", newDurability);
                PrecisionKnappingModSystem.ServerApi?.Logger.Debug(
                    $"[RARITY-COMPAT] Re-applied ratio {ratio} -> durability {newDurability}/{effectiveMax} on {itemStack.Collectible.Code}");
            }
            catch (Exception)
            {
                // Silent fail - never break another mod's rarity application
            }
        }
    }
}
