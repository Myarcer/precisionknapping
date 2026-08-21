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
    /// durability = BASE max * ratio in OnCreatedByCrafting. The absolute value never
    /// sees the rarity multiplier, so a perfect knapped legendary tool showed 75/80
    /// instead of 100/80.
    ///
    /// The crafting transfer patch stamps "precisionknapping:durabilityRatio" plus a
    /// "precisionknapping:durabilityPending" marker onto every finished tool it scales.
    /// This patch hooks Item Rarity's Rarity.ApplyRarity: when a pending tool comes out,
    /// the stored durability is rescaled from base max to the now-effective max.
    /// Damaged tools are never touched, and when Item Rarity is not loaded this patch
    /// is never applied (see PrecisionKnappingModSystem).
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

                // Only tools freshly crafted from precision-knapped parts carry this marker.
                // Without it (any normal craft, any item drop) we do nothing.
                if (!itemStack.Attributes.HasAttribute("precisionknapping:durabilityPending")) return;
                itemStack.Attributes.RemoveAttribute("precisionknapping:durabilityPending");

                float ratio = itemStack.Attributes.GetFloat(RatioKey, -1f);
                if (ratio <= 0f) return;

                // Base durability from the JSON property - NOT GetMaxDurability, which Item
                // Rarity has already patched to include its multiplier at this point.
                int baseMax = itemStack.Collectible.Durability;
                if (baseMax <= 0) return;

                int unscaled = Math.Max(1, (int)(baseMax * ratio));

                // Rescale only when the stored durability is still our untouched write.
                // A damaged or otherwise modified tool keeps its current value.
                int current = itemStack.Attributes.GetInt("durability", -1);
                if (current != unscaled)
                {
                    PrecisionKnappingModSystem.ServerApi?.Logger.Debug(
                        $"[RARITY-COMPAT] Skip {itemStack.Collectible.Code}: durability {current} != unscaled {unscaled}");
                    return;
                }

                int effectiveMax = itemStack.Collectible.GetMaxDurability(itemStack);
                if (effectiveMax <= 0) return;

                int newDurability = Math.Max(1, (int)(effectiveMax * ratio));
                if (newDurability == current) return;

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
