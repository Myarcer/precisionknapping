using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using System;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Saves the mistake count with the knapping surface (BlockEntityKnappingSurface.ToTreeAttributes).
    /// Without this, mistakes reset when the chunk unloads or the server restarts.
    /// </summary>
    [HarmonyPatch]
    public static class KnappingSaveMistakesPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var type = KnappingReflectionHelper.FindGameType("Vintagestory.GameContent.BlockEntityKnappingSurface");
            return type?.GetMethod("ToTreeAttributes", BindingFlags.Public | BindingFlags.Instance,
                null, new Type[] { typeof(ITreeAttribute) }, null);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, ITreeAttribute tree)
        {
            try
            {
                int mistakes = AdvancedKnappingHelper.GetMistakeCount(__instance as BlockEntity);
                if (mistakes > 0) tree.SetInt(AdvancedKnappingHelper.MistakeAttributeKey, mistakes);
                else tree.RemoveAttribute(AdvancedKnappingHelper.MistakeAttributeKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PrecisionKnapping] Failed to save mistake count: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads the mistake count back (BlockEntityKnappingSurface.FromTreeAttributes).
    /// </summary>
    [HarmonyPatch]
    public static class KnappingLoadMistakesPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var type = KnappingReflectionHelper.FindGameType("Vintagestory.GameContent.BlockEntityKnappingSurface");
            return type?.GetMethod("FromTreeAttributes", BindingFlags.Public | BindingFlags.Instance,
                null, new Type[] { typeof(ITreeAttribute), typeof(IWorldAccessor) }, null);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, ITreeAttribute tree)
        {
            try
            {
                AdvancedKnappingHelper.LoadMistakeCount(__instance as BlockEntity,
                    tree.GetInt(AdvancedKnappingHelper.MistakeAttributeKey, 0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PrecisionKnapping] Failed to load mistake count: {ex.Message}");
            }
        }
    }
}
