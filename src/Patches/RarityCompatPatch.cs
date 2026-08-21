using Vintagestory.API.Common;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for CollectibleObject.ConsumeCraftingIngredients
    ///
    /// Compatibility with Item Rarity (and any mod that scales max durability after crafting).
    ///
    /// Order of events for one grid craft:
    /// 1. RecipeBase.Resolve calls OnCreatedByCrafting: our transfer patch writes
    ///    durability = BASE max * precisionknapping:durabilityRatio.
    /// 2. ConsumeCraftingIngredients: Item Rarity's postfix assigns the rarity and
    ///    multiplies MAX durability virtually (its own GetMaxDurability postfix).
    /// The absolute durability written in step 1 never saw that multiplier, so a perfect
    /// knapped legendary tool showed e.g. 75/80 instead of 100/80.
    ///
    /// This postfix runs after Item Rarity's (Priority.Last vs their priority 0) and
    /// re-applies our ratio to the now-effective max durability. Without a rarity the
    /// recomputed value equals the stored one and nothing is written.
    /// </summary>
    [HarmonyPatch]
    public static class RarityCompatPatch
    {
        private const string RatioKey = "precisionknapping:durabilityRatio";

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            Type recipeBaseType = typeof(CollectibleObject).Assembly.GetType("Vintagestory.API.Common.IRecipeBase");
            if (recipeBaseType == null)
            {
                Console.WriteLine("[RARITY-COMPAT] ERROR: IRecipeBase type not found!");
                return null;
            }

            var method = typeof(CollectibleObject).GetMethod("ConsumeCraftingIngredients",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(ItemSlot[]), typeof(ItemSlot), recipeBaseType },
                null);

            if (method == null)
            {
                Console.WriteLine("[RARITY-COMPAT] ERROR: Could not find ConsumeCraftingIngredients!");
            }

            return method;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(ItemSlot outputSlot)
        {
            try
            {
                ItemStack stack = outputSlot?.Itemstack;
                if (stack?.Attributes == null || stack.Collectible == null) return;

                float ratio = stack.Attributes.GetFloat(RatioKey, -1f);
                if (ratio <= 0f) return;

                int effectiveMax = stack.Collectible.GetMaxDurability(stack);
                if (effectiveMax <= 0) return;

                int newDurability = Math.Max(1, (int)(effectiveMax * ratio));
                int current = stack.Attributes.GetInt("durability", -1);
                if (current == newDurability) return;

                stack.Attributes.SetInt("durability", newDurability);
            }
            catch (Exception)
            {
                // Silent fail - never break crafting for any reason
            }
        }
    }
}
