using Vintagestory.API.Common;
using System;
using System.Reflection;

namespace precisionknapping
{
    /// <summary>
    /// Centralized reflection helper for accessing vanilla Vintage Story types.
    /// Caches FieldInfo/PropertyInfo for performance.
    /// </summary>
    public static class KnappingReflectionHelper
    {
        private static Type _knappingSurfaceType;
        private static FieldInfo _voxelsField;
        private static PropertyInfo _selectedRecipeProp;
        private static FieldInfo _recipeVoxelsField;
        private static FieldInfo _recipeOutputField;
        private static FieldInfo _resolvedStackField;
        private static MethodInfo _resolveMethod;
        private static MethodInfo _checkIfFinishedMethod;

        /// <summary>
        /// Initialize cached reflection references
        /// </summary>
        public static void Initialize()
        {
            if (_knappingSurfaceType != null) return;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _knappingSurfaceType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                if (_knappingSurfaceType != null) break;
            }

            if (_knappingSurfaceType != null)
            {
                _voxelsField = _knappingSurfaceType.GetField("Voxels", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _selectedRecipeProp = _knappingSurfaceType.GetProperty("SelectedRecipe");
            }
        }

        public static bool[,] GetCurrentVoxels(object entity)
        {
            Initialize();
            return _voxelsField?.GetValue(entity) as bool[,];
        }

        public static object GetSelectedRecipe(object entity)
        {
            Initialize();
            return _selectedRecipeProp?.GetValue(entity);
        }

        public static bool[,,] GetRecipeVoxels(object recipe)
        {
            if (recipe == null) return null;
            
            if (_recipeVoxelsField == null)
            {
                var recipeType = recipe.GetType();
                _recipeVoxelsField = recipeType.GetField("Voxels", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            
            return _recipeVoxelsField?.GetValue(recipe) as bool[,,];
        }

        public static void SetVoxel(object entity, int x, int z, bool value)
        {
            var voxels = GetCurrentVoxels(entity);
            if (voxels != null && x >= 0 && x < 16 && z >= 0 && z < 16)
            {
                voxels[x, z] = value;
            }
        }

        public static void RemoveVoxel(object entity, int x, int z)
        {
            SetVoxel(entity, x, z, false);
        }

        public static ItemStack GetRecipeOutput(object recipe, IWorldAccessor world)
        {
            if (recipe == null) return null;

            var recipeType = recipe.GetType();
            
            // Get Output field (JsonItemStack)
            if (_recipeOutputField == null)
                _recipeOutputField = recipeType.GetField("Output", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            var output = _recipeOutputField?.GetValue(recipe);
            if (output == null) return null;

            var outputType = output.GetType();
            
            // Get ResolvedItemstack field
            if (_resolvedStackField == null)
                _resolvedStackField = outputType.GetField("ResolvedItemstack", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var resolvedStack = _resolvedStackField?.GetValue(output) as ItemStack;

            // Try to resolve if not already resolved
            if (resolvedStack == null && world != null)
            {
                if (_resolveMethod == null)
                    _resolveMethod = outputType.GetMethod("Resolve", new Type[] { typeof(IWorldAccessor), typeof(string), typeof(bool) });

                if (_resolveMethod != null)
                {
                    try
                    {
                        _resolveMethod.Invoke(output, new object[] { world, "[PrecisionKnapping]", false });
                        resolvedStack = _resolvedStackField?.GetValue(output) as ItemStack;
                    }
                    catch { }
                }
            }

            return resolvedStack;
        }

        /// <summary>
        /// Calls the vanilla CheckIfFinished method to trigger recipe completion check.
        /// This is needed in RealisticStrikes mode since we bypass vanilla OnUseOver.
        /// </summary>
        public static void CallCheckIfFinished(object entity, IPlayer player)
        {
            if (entity == null || player == null) return;

            try
            {
                if (_checkIfFinishedMethod == null)
                {
                    var entityType = entity.GetType();
                    _checkIfFinishedMethod = entityType.GetMethod("CheckIfFinished",
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                }

                _checkIfFinishedMethod?.Invoke(entity, new object[] { player });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PrecisionKnapping] CallCheckIfFinished failed: {ex.Message}");
            }
        }
    }
}
