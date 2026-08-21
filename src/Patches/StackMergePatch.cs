using Vintagestory.API.Common;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Harmony patch for CollectibleObject.GetMergableQuantity
    ///
    /// Fixes the ModDB stacking report: knapped tool heads carry their quality in the
    /// "precisionknapping:durabilityRatio" attribute, but that attribute is registered in
    /// GlobalConstants.IgnoredStackAttributes (required so recipe ingredient matching and
    /// tutorial stack checks accept knapped heads). Stack equality therefore ignores it,
    /// and perfect heads merged with damaged heads into one "perfect" stack.
    ///
    /// This postfix blocks a merge only when the two stacks disagree on our ratio:
    /// - both have the same ratio (e.g. two perfect heads) -> merge as normal
    /// - one has a ratio, the other none (perfect vs vanilla head) -> blocked
    /// - ratios differ (perfect vs damaged) -> blocked
    /// Recipe matching is untouched - it does not go through GetMergableQuantity.
    /// </summary>
    [HarmonyPatch]
    public static class StackMergePatch
    {
        private const string RatioKey = "precisionknapping:durabilityRatio";

        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var method = typeof(CollectibleObject).GetMethod("GetMergableQuantity",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(ItemStack), typeof(ItemStack), typeof(EnumMergePriority) },
                null);

            if (method == null)
            {
                Console.WriteLine("[STACK-MERGE-PATCH] ERROR: Could not find GetMergableQuantity!");
            }

            return method;
        }

        [HarmonyPostfix]
        static void Postfix(ItemStack sinkStack, ItemStack sourceStack, ref int __result)
        {
            try
            {
                if (__result == 0 || sinkStack?.Attributes == null || sourceStack?.Attributes == null) return;

                float sinkRatio = sinkStack.Attributes.GetFloat(RatioKey, -1f);
                float sourceRatio = sourceStack.Attributes.GetFloat(RatioKey, -1f);
                bool sinkHas = sinkRatio > 0f;
                bool sourceHas = sourceRatio > 0f;

                if (sinkHas != sourceHas)
                {
                    __result = 0;
                    return;
                }

                if (sinkHas && Math.Abs(sinkRatio - sourceRatio) > 0.001f)
                {
                    __result = 0;
                }
            }
            catch (Exception)
            {
                // Never break item movement for any reason
            }
        }
    }
}
