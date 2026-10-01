using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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

        private static MethodInfo _onUseOverMethod;
        private static FieldInfo _baseMaterialField;

        /// <summary>
        /// Find a game type (e.g. from VSSurvivalMod) by its full name across all loaded assemblies.
        /// </summary>
        public static Type FindGameType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>
        /// Initialize cached reflection references
        /// </summary>
        public static void Initialize()
        {
            if (_knappingSurfaceType != null) return;

            _knappingSurfaceType = FindGameType("Vintagestory.GameContent.BlockEntityKnappingSurface");

            if (_knappingSurfaceType != null)
            {
                _voxelsField = _knappingSurfaceType.GetField("Voxels", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _selectedRecipeProp = _knappingSurfaceType.GetProperty("SelectedRecipe");
                _baseMaterialField = _knappingSurfaceType.GetField("BaseMaterial", BindingFlags.Public | BindingFlags.Instance);
                _onUseOverMethod = _knappingSurfaceType.GetMethod("OnUseOver",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new Type[] { typeof(IPlayer), typeof(Vec3i), typeof(BlockFacing), typeof(bool) }, null);
            }
        }

        /// <summary>
        /// True if the block entity is a vanilla knapping surface.
        /// </summary>
        public static bool IsKnappingSurface(BlockEntity entity)
        {
            if (entity == null) return false;
            Initialize();
            return _knappingSurfaceType != null
                ? _knappingSurfaceType.IsInstanceOfType(entity)
                : entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        /// <summary>
        /// The stone the knapping surface is made of (used for particle colors).
        /// </summary>
        public static ItemStack GetBaseMaterial(object entity)
        {
            Initialize();
            return _baseMaterialField?.GetValue(entity) as ItemStack;
        }

        /// <summary>
        /// Calls the vanilla OnUseOver(IPlayer, Vec3i, BlockFacing, bool) knap handler.
        /// Goes through the Harmony-patched method, so KnappingSurfacePatch still applies.
        /// </summary>
        public static bool CallOnUseOver(object entity, IPlayer player, Vec3i voxelPos, BlockFacing facing)
        {
            Initialize();
            if (_onUseOverMethod == null || entity == null) return false;

            _onUseOverMethod.Invoke(entity, new object[] { player, voxelPos, facing, true });
            return true;
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
        /// Needed when our patches bypass vanilla OnUseOver (waste pocket/fracture removal).
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