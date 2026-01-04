using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ProtoBuf;

namespace precisionknapping
{
    // Configuration class moved to: src/Core/PrecisionKnappingConfig.cs
    // RealisticStrikes classes moved to: src/Features/RealisticStrikes.cs

    #region Main Mod System

    /// <summary>
    /// Precision Knapping mod - Harmony patching approach
    ///
    /// ARCHITECTURE: Uses Harmony patches to intercept BlockEntityKnappingSurface.OnUseOver()
    /// NOTE: JSON patch files in assets/patches/ are OBSOLETE from earlier behavior-based attempt
    ///
    /// MODES:
    /// - Default Mode: First protected voxel click breaks stone (strict)
    /// - Advanced Mode: Edge enforcement + mistake tolerance with durability scaling
    ///   - Must click from outer edges inward (virtual edges for recipe holes)
    ///   - Clicking non-edge voxel removes line to nearest edge (risky)
    ///   - Each protected voxel removed counts as mistake
    ///   - Mistakes reduce durability: 0=100%, 1=75%, 2=50%, 3=25%, 4+=break
    ///   - Tool heads get durability reduction, non-tool items lose quantity
    /// </summary>
    public class PrecisionKnappingModSystem : ModSystem
    {
        private RecipePatternManager patternManager;
        private static PrecisionKnappingModSystem instance;
        private Harmony harmony;
        private static PrecisionKnappingConfig config;
        private static ICoreServerAPI serverApi;
        private static ChargeStateTracker chargeTracker;

        public static PrecisionKnappingConfig Config => config;
        public static ICoreServerAPI ServerApi => serverApi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            instance = this;
            this.api = api;

            // Load configuration
            try
            {
                config = api.LoadModConfig<PrecisionKnappingConfig>("precisionknapping.json");
                if (config == null)
                {
                    config = new PrecisionKnappingConfig();
                }
                // Always re-save to add any new properties from updates
                api.StoreModConfig(config, "precisionknapping.json");
            }
            catch (Exception ex)
            {
                api.Logger.Error($"[PrecisionKnapping] Failed to load config: {ex.Message}");
                config = new PrecisionKnappingConfig();
            }

            // Initialize pattern manager
            patternManager = new RecipePatternManager();

            // Phase 1: API Inspection - Discover BlockEntityKnappingSurface methods (for debugging)
            InspectKnappingSurfaceAPI(api);

            // Phase 2: Apply Harmony patches
            try
            {
                harmony = new Harmony("com.precisionknapping.mod");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                api.Logger.Error("[PrecisionKnapping] Failed to apply Harmony patches: " + ex.Message);
                api.Logger.Error("[PrecisionKnapping] Stack: " + ex.StackTrace);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.precisionknapping.mod");
            base.Dispose();
        }

        /// <summary>
        /// Phase 1: Initialize reflection helpers (API inspection disabled for release)
        /// </summary>
        private void InspectKnappingSurfaceAPI(ICoreAPI api)
        {
            // API inspection disabled - development/debugging only
        }

        /// <summary>
        /// Block class inspection (disabled for release)
        /// </summary>
        private void InspectKnappingBlockClass(ICoreAPI api)
        {
            // Block inspection disabled - development/debugging only
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            serverApi = api;

            // Load all knapping recipes after server is fully initialized
            patternManager.LoadAllKnappingRecipes(api);

            // Register network channel for RealisticStrikes
            var channel = api.Network.RegisterChannel("precisionknapping")
                .RegisterMessageType<ChargeReleasePacket>();
            
            channel.SetMessageHandler<ChargeReleasePacket>(OnChargeReleasePacket);
            
            api.Logger.Notification("[PrecisionKnapping] Server network channel registered");
        }

        /// <summary>
        /// Handle charged strike packet from client.
        /// Validates charge level and executes strike with fracture physics.
        /// </summary>
        private void OnChargeReleasePacket(IServerPlayer player, ChargeReleasePacket packet)
        {
            try
            {
                var config = Config;
                if (config == null || !config.RealisticStrikes) return;

                // Validate charge level
                float minChargeRatio = config.MinChargeTimeMs / (float)config.FullChargeTimeMs;
                if (packet.ChargeLevel < minChargeRatio)
                {
                    // Not enough charge - ignore (quick click)
                    return;
                }

                // Get block entity
                var pos = new BlockPos(packet.BlockX, packet.BlockY, packet.BlockZ);
                var entity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
                if (entity == null || entity.GetType().Name != "BlockEntityKnappingSurface")
                {
                    return;
                }

                // Execute the charged strike using existing logic
                ExecuteChargedStrike(player, entity, packet);
            }
            catch (Exception ex)
            {
                serverApi.Logger.Error($"[PrecisionKnapping] OnChargeReleasePacket error: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute a charged strike on a knapping surface.
        /// Uses existing mistake tracking and fracture logic based on mode.
        /// </summary>
        private void ExecuteChargedStrike(IServerPlayer player, BlockEntity entity, ChargeReleasePacket packet)
        {
            var config = Config;
            bool advancedMode = config?.AdvancedMode ?? false;

            // Get voxel data using reflection helper
            var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(entity);
            var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(entity);

            if (selectedRecipe == null || currentVoxels == null)
            {
                return;
            }

            var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
            if (recipeVoxels == null) return;

            int voxelX = packet.VoxelX;
            int voxelZ = packet.VoxelZ;

            // Bounds check
            if (voxelX < 0 || voxelX >= 16 || voxelZ < 0 || voxelZ >= 16) return;

            bool voxelExists = currentVoxels[voxelX, voxelZ];
            if (!voxelExists) return; // Nothing to strike

            bool isProtected = recipeVoxels[voxelX, 0, voxelZ];
            var voxelPos = new Vec3i(voxelX, 0, voxelZ);

            // Use existing mode-specific logic
            if (advancedMode)
            {
                // Advanced mode: edge detection, fractures, durability scaling
                bool isEdge = AdvancedKnappingHelper.IsEdgeVoxel(voxelX, voxelZ, currentVoxels, recipeVoxels);

                if (isEdge)
                {
                    if (isProtected)
                    {
                        // Hit protected edge voxel - mistake
                        HandleChargedMistake(entity, player, voxelPos, config, 1);
                    }
                    else
                    {
                        // Safe edge removal
                        KnappingReflectionHelper.RemoveVoxel(entity, voxelX, voxelZ);
                        AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);
                        KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, player);
                    }
                }
                else
                {
                    // Non-edge click - fracture mechanics
                    // Check for enclosed waste pocket first
                    if (!isProtected)
                    {
                        var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(voxelX, voxelZ, currentVoxels, recipeVoxels);
                        if (pocket.Count > 0)
                        {
                            foreach (var pos in pocket)
                                currentVoxels[pos.X, pos.Y] = false;
                            entity.MarkDirty(true);
                            KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, player);
                            return;
                        }
                    }

                    // Calculate fracture zone
                    var path = AdvancedKnappingHelper.FindPathToNearestEdge(voxelX, voxelZ, currentVoxels, recipeVoxels);
                    if (path.Count > 0)
                    {
                        var fractureZone = FractureCalculator.CalculateFractureZone(path, currentVoxels, config);
                        int protectedCount = FractureCalculator.CountProtectedInZone(fractureZone, recipeVoxels, currentVoxels);

                        if (protectedCount > 0)
                        {
                            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
                            int newTotal = currentMistakes + protectedCount;

                            if (newTotal > config.MistakeAllowance)
                            {
                                // Stone breaks
                                HandleStoneBroken(entity, player, "Too many mistakes! Stone destroyed.");
                                return;
                            }

                            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
                            int remaining = config.MistakeAllowance - newTotal;

                            if (config.EnableDurabilityScaling)
                            {
                                float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                                KnappingMessageHelper.NotifyMistakes(player, protectedCount, remaining, durability);
                            }
                            else
                            {
                                KnappingMessageHelper.NotifyMistake(player, remaining);
                            }
                            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, player);
                        }

                        // Remove fracture zone
                        foreach (var pos in fractureZone)
                            currentVoxels[pos.X, pos.Y] = false;

                        AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);
                        KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, player);
                    }
                }
            }
            else
            {
                // Default mode: simple mistake tolerance
                if (isProtected)
                {
                    int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
                    int newTotal = currentMistakes + 1;
                    int allowance = config?.MistakeAllowance ?? 1;

                    if (newTotal > allowance)
                    {
                        HandleStoneBroken(entity, player, "Too many mistakes! Stone destroyed.");
                        return;
                    }

                    AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
                    int remaining = allowance - newTotal;

                    if (remaining > 0)
                        KnappingMessageHelper.NotifyMistake(player, remaining);
                    else
                        KnappingMessageHelper.NotifyFinalWarning(player);

                    KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, player);
                }
                else
                {
                    KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, player);
                }

                // Remove the voxel
                KnappingReflectionHelper.RemoveVoxel(entity, voxelX, voxelZ);

                // Clean up disconnected voxels (floating debris)
                AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);
            }

            entity.MarkDirty(true);

            // Trigger recipe completion check (needed since we bypass vanilla OnUseOver)
            KnappingReflectionHelper.CallCheckIfFinished(entity, player);
        }

        private void HandleChargedMistake(BlockEntity entity, IServerPlayer player, Vec3i voxelPos, PrecisionKnappingConfig config, int mistakeCount)
        {
            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
            int newTotal = currentMistakes + mistakeCount;

            if (newTotal > config.MistakeAllowance)
            {
                HandleStoneBroken(entity, player, "Too many mistakes! Stone destroyed.");
                return;
            }

            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
            int remaining = config.MistakeAllowance - newTotal;

            if (config.EnableDurabilityScaling)
            {
                float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                KnappingMessageHelper.NotifyMistake(player, remaining, durability);
            }
            else
            {
                KnappingMessageHelper.NotifyMistake(player, remaining);
            }

            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, player);
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);
        }

        private void HandleStoneBroken(BlockEntity entity, IServerPlayer player, string message)
        {
            AdvancedKnappingHelper.ClearMistakeCount(entity.Pos);
            KnappingSoundHelper.PlayStoneBreakSound(entity.Api, entity.Pos);
            KnappingMessageHelper.NotifyStoneDestroyed(player, message);

            try
            {
                entity.Api.World.BlockAccessor.BreakBlock(entity.Pos.Copy(), player);
            }
            catch (Exception ex)
            {
                entity.Api.Logger.Error($"[PrecisionKnapping] BreakBlock failed: {ex.Message}");
            }
        }

        private ICoreAPI api;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            // Initialize Learn Mode overlay if enabled
            if (Config?.LearnModeOverlay == true)
            {
                new LearnModeOverlayRenderer(api);
                api.Logger.Notification("[PrecisionKnapping] Learn Mode overlay enabled");
            }

            // Initialize RealisticStrikes charge tracker
            if (Config?.RealisticStrikes == true)
            {
                chargeTracker = new ChargeStateTracker(api);
            }
        }

        public static RecipePatternManager GetPatternManager()
        {
            return instance?.patternManager;
        }
    }

    #endregion

    #region Recipe Pattern Manager

    /// <summary>
    /// Manages loading and validation of knapping recipe patterns
    /// </summary>
    public class RecipePatternManager
    {
        private Dictionary<string, KnappingPattern> patterns = new Dictionary<string, KnappingPattern>();

        public void LoadAllKnappingRecipes(ICoreAPI api)
        {
            try
            {

                // Access KnappingRecipes via reflection from BlockEntityKnappingSurface
                Type knappingEntityType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    knappingEntityType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                    if (knappingEntityType != null) break;
                }

                if (knappingEntityType == null)
                {
                    api.Logger.Error("[PrecisionKnapping] Could not find BlockEntityKnappingSurface class");
                    return;
                }

                // Get the KnappingRecipes field
                var knappingRecipesField = knappingEntityType.GetField("KnappingRecipes", BindingFlags.Static | BindingFlags.Public);
                if (knappingRecipesField == null)
                {
                    api.Logger.Warning("[PrecisionKnapping] Could not find KnappingRecipes field, trying alternative method...");

                    // Try to get from API directly
                    var recipeRegistry = api.World.GetType().GetProperty("KnappingRecipes");
                    if (recipeRegistry != null)
                    {
                        var knappingRecipes = recipeRegistry.GetValue(api.World) as List<object>;
                        if (knappingRecipes != null)
                        {
                            LoadRecipesFromList(knappingRecipes, api);
                        }
                    }
                    return;
                }

                var recipes = knappingRecipesField.GetValue(null) as List<object>;
                if (recipes == null || recipes.Count == 0)
                {
                    api.Logger.Warning("[PrecisionKnapping] No knapping recipes found");
                    return;
                }

                LoadRecipesFromList(recipes, api);
            }
            catch (Exception ex)
            {
                api.Logger.Error("[PrecisionKnapping] Failed to load recipes: " + ex.Message);
                api.Logger.Error("[PrecisionKnapping] Stack trace: " + ex.StackTrace);
            }
        }

        private void LoadRecipesFromList(List<object> recipes, ICoreAPI api)
        {

            int loadedCount = 0;
            foreach (var recipe in recipes)
            {
                try
                {
                    var recipeType = recipe.GetType();
                    var nameProp = recipeType.GetProperty("Name");
                    var ingredientPatternProp = recipeType.GetProperty("IngredientPattern");

                    if (nameProp != null && ingredientPatternProp != null)
                    {
                        var nameObj = nameProp.GetValue(recipe);
                        string recipeName = nameObj?.ToString() ?? "unknown";

                        var pattern = ingredientPatternProp.GetValue(recipe) as string;
                        if (!string.IsNullOrEmpty(pattern))
                        {
                            LoadRecipePatternFromString(recipeName, pattern, api);
                            loadedCount++;
                        }
                    }
                }
                catch (Exception)
                {
                    // Individual recipe load failures are non-critical
                }
            }
        }

        private void LoadRecipePatternFromString(string recipeName, string patternString, ICoreAPI api)
        {
            try
            {
                // Pattern string is a single string with rows separated by newlines or commas
                // Example: "_ _ _ # # # _ _ _,_ _ # # # # # _ _,..."

                // Split by common separators
                var rows = patternString.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

                // Filter and trim to get exactly 10 rows
                var gridRows = new List<string>();
                foreach (var row in rows)
                {
                    var trimmedRow = row.Trim().Replace(" ", ""); // Remove spaces
                    if (!string.IsNullOrEmpty(trimmedRow) && gridRows.Count < 10)
                    {
                        // Ensure row is exactly 10 characters (pad or trim)
                        if (trimmedRow.Length > 10)
                            trimmedRow = trimmedRow.Substring(0, 10);
                        else if (trimmedRow.Length < 10)
                            trimmedRow = trimmedRow.PadRight(10, '_');

                        gridRows.Add(trimmedRow);
                    }
                }

                // Pad to 10 rows if needed
                while (gridRows.Count < 10)
                {
                    gridRows.Add("__________");
                }

                patterns[recipeName] = new KnappingPattern
                {
                    Name = recipeName,
                    Grid = gridRows.ToArray()
                };
            }
            catch (Exception ex)
            {
                api.Logger.Warning("[PrecisionKnapping] Could not load pattern for " + recipeName + ": " + ex.Message);
            }
        }

        private string[] CreateTestPattern()
        {
            // Create a test pattern with protected areas
            return new string[]
            {
                "##########",
                "#____####_",
                "#____####_",
                "#____####_",
                "#____####_",
                "#____####_",
                "#____####_",
                "#____####_",
                "#____####_",
                "##########"
            };
        }

        public bool IsProtectedVoxelFromPattern(int x, int y, string patternString, ICoreAPI api = null)
        {
            try
            {
                // Parse the pattern string directly
                var rows = patternString.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

                if (x < 0 || x >= 10 || y < 0 || y >= 10)
                    return false;

                if (y >= rows.Length)
                    return false;

                var row = rows[y].Trim().Replace(" ", "");

                if (x >= row.Length)
                    return false;

                char cell = row[x];
                return cell == '#';
            }
            catch
            {
                return false;
            }
        }

        public bool IsProtectedVoxel(int x, int y, string recipeName = null, ICoreAPI api = null)
        {
            // Try to get the actual recipe pattern
            KnappingPattern pattern = null;
            if (!string.IsNullOrEmpty(recipeName) && patterns.ContainsKey(recipeName))
            {
                pattern = patterns[recipeName];
            }
            else
            {
                // Fallback to test pattern if recipe not found
                pattern = new KnappingPattern { Name = "test", Grid = CreateTestPattern() };
            }

            if (x < 0 || x >= 10 || y < 0 || y >= 10)
                return false;

            var gridRow = pattern.Grid[y];
            var gridCell = gridRow[x];
            return gridCell == '#';
        }

        public int GetPatternCount()
        {
            return patterns.Count;
        }
    }

    /// <summary>
    /// Represents a knapping recipe pattern
    /// </summary>
    public class KnappingPattern
    {
        public string Name { get; set; }
        public string[] Grid { get; set; } // 10x10 grid of # (protected) and _ (safe)
    }

    #endregion

    // ============================================================================
    // HELPER CLASSES - MOVED TO SEPARATE FILES
    // ============================================================================
    // KnappingReflectionHelper -> src/Helpers/KnappingReflectionHelper.cs
    // KnappingSoundHelper      -> src/Helpers/KnappingSoundHelper.cs  
    // KnappingMessageHelper    -> src/Helpers/KnappingMessageHelper.cs
    // AdvancedKnappingHelper   -> src/Helpers/AdvancedKnappingHelper.cs
    // FractureCalculator       -> src/Features/FractureCalculator.cs
    // ============================================================================




































    #region Harmony Patches

    /// <summary>
    /// Harmony patch for BlockEntityKnappingSurface.OnUseOver
    /// Intercepts voxel removal to validate against recipe patterns
    /// </summary>
    [HarmonyPatch]
    public static class KnappingSurfacePatch
    {
        /// <summary>
        /// Target the OnUseOver method in BlockEntityKnappingSurface
        /// </summary>
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            // Find the BlockEntityKnappingSurface type
            Type targetType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                targetType = assembly.GetType("Vintagestory.GameContent.BlockEntityKnappingSurface");
                if (targetType != null) break;
            }

            if (targetType == null)
            {
                throw new Exception("[HARMONY-PATCH] BlockEntityKnappingSurface type not found!");
            }

            // Get the OnUseOver method
            var method = targetType.GetMethod("OnUseOver",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(IPlayer), typeof(Vec3i), typeof(BlockFacing), typeof(bool) },
                null);

            if (method == null)
            {
                throw new Exception("[HARMONY-PATCH] OnUseOver method not found!");
            }

            return method;
        }

        /// <summary>
        /// Prefix method - runs BEFORE vanilla OnUseOver
        /// Return false to prevent vanilla method from running
        /// 
        /// MODES:
        /// - Default: Mistake tolerance - exceeding MistakeAllowance breaks stone (no durability penalty)
        /// - Advanced: Edge enforcement + mistake tracking with durability scaling
        /// </summary>
        [HarmonyPrefix]
        static bool Prefix(object __instance, IPlayer byPlayer, Vec3i voxelPos, BlockFacing facing, bool mouseMode)
        {
            try
            {
                // Get the BlockEntity instance
                var entity = __instance as BlockEntity;
                if (entity == null) return true;

                var config = PrecisionKnappingModSystem.Config;

                // ========== REALISTIC STRIKES MODE ==========
                // When enabled, ALWAYS block vanilla OnUseOver
                // All strikes are handled via ChargeReleasePacket
                if (config?.RealisticStrikes ?? false)
                {
                    return false; // Block vanilla completely
                }

                bool advancedMode = config?.AdvancedMode ?? false;

                // Get voxel data using reflection helper
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(__instance);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(__instance);

                if (selectedRecipe == null || currentVoxels == null)
                {
                    return true; // Allow vanilla behavior - no recipe selected
                }

                // Get recipe voxel pattern
                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);

                if (recipeVoxels == null || voxelPos.X >= 16 || voxelPos.Z >= 16)
                {
                    return true; // Allow vanilla behavior
                }

                // Check if clicked voxel exists and is protected
                bool voxelExists = currentVoxels[voxelPos.X, voxelPos.Z];
                bool isPartOfFinalShape = recipeVoxels[voxelPos.X, 0, voxelPos.Z];
                bool isProtected = isPartOfFinalShape && voxelExists;

                // ==================== ADVANCED MODE ====================
                if (advancedMode)
                {
                    return HandleAdvancedMode(entity, byPlayer, voxelPos, currentVoxels, recipeVoxels, voxelExists, isProtected, config);
                }

                // ==================== DEFAULT MODE ====================
                // Mistake tolerance - count mistakes, break stone when exceeding allowance
                // No durability penalty - you get 100% item or nothing
                if (isProtected)
                {
                    return HandleDefaultModeMistake(entity, byPlayer, voxelPos, config);
                }

                return true; // Allow vanilla behavior - safe voxel

            }
            catch (Exception ex)
            {
                Console.WriteLine("[HARMONY-PATCH] Error in prefix: " + ex.Message);
                Console.WriteLine("[HARMONY-PATCH] Stack: " + ex.StackTrace);
                return true; // Allow vanilla behavior on error
            }
        }

        /// <summary>
        /// Handle Default Mode mistake - counts toward breaking, NOT durability
        /// </summary>
        static bool HandleDefaultModeMistake(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, PrecisionKnappingConfig config)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle
            }

            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
            int newTotal = currentMistakes + 1;
            int allowance = config?.MistakeAllowance ?? 1;

            // Check if this exceeds allowance
            if (newTotal > allowance)
            {
                return HandleProtectedVoxelHit(entity, byPlayer, voxelPos,
                    $"Too many mistakes ({newTotal})! Stone destroyed.");
            }

            // Register mistake
            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);

            if (byPlayer is IServerPlayer serverPlayer)
            {
                int remaining = allowance - newTotal;
                if (remaining > 0)
                {
                    KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
                }
                else
                {
                    KnappingMessageHelper.NotifyFinalWarning(byPlayer);
                }
            }

            // Play warning sound
            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);

            // Manually remove the voxel for visual feedback
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);

            // Return FALSE to prevent vanilla from running (which could break recipe validation)
            return false;
        }        /// <summary>
        /// Handle Advanced Mode mechanics: edge detection, line-breaking with fracture spread, mistake tracking
        /// </summary>
        static bool HandleAdvancedMode(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, 
            bool[,] currentVoxels, bool[,,] recipeVoxels, bool voxelExists, bool isProtected, PrecisionKnappingConfig config)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle
            }

            // Voxel must exist to be clicked
            if (!voxelExists)
            {
                return true; // Allow vanilla - clicking empty space
            }

            // Check if this is an edge voxel
            bool isEdge = AdvancedKnappingHelper.IsEdgeVoxel(voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);

            if (isEdge)
            {
                // Clicking an edge voxel - check if protected
                if (isProtected)
                {
                    return HandleMistake(entity, byPlayer, voxelPos, config, 1, "Hit protected voxel!");
                }
                return true; // Safe edge click - allow vanilla
            }
            else
            {
                // Clicking a non-edge voxel - could be risky OR could be enclosed waste pocket
                
                // FIRST: Check if this is an enclosed waste pocket (like macehead center)
                // This must be checked BEFORE path finding, because FindPathToNearestEdge
                // traverses through protected voxels and would find a path anyway
                if (!isProtected)
                {
                    var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(
                        voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);
                    if (pocket.Count > 0)
                    {
                        // Safe removal of enclosed waste pocket
                        foreach (var pos in pocket)
                            currentVoxels[pos.X, pos.Y] = false;
                        entity.MarkDirty(true);
                        KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);
                        return false;
                    }
                }

                // Find path to nearest edge and calculate fracture zone
                var path = AdvancedKnappingHelper.FindPathToNearestEdge(voxelPos.X, voxelPos.Z, currentVoxels, recipeVoxels);

                if (path.Count == 0)
                {
                    // No path to edge found and not an enclosed pocket
                    if (isProtected)
                    {
                        return HandleMistake(entity, byPlayer, voxelPos, config, 1, "Hit protected voxel!");
                    }
                    return true;
                }

                // Calculate fracture zone with spread around path
                var fractureZone = FractureCalculator.CalculateFractureZone(path, currentVoxels, config);
                
                // Count protected voxels in the entire fracture zone
                int protectedCount = FractureCalculator.CountProtectedInZone(fractureZone, recipeVoxels, currentVoxels);

                // If any protected voxels in fracture zone, register mistakes
                if (protectedCount > 0)
                {
                    // Check if this exceeds allowance
                    int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
                    int newTotal = currentMistakes + protectedCount;

                    if (newTotal > config.MistakeAllowance)
                    {
                        // Too many mistakes - break stone
                        return HandleProtectedVoxelHit(entity, byPlayer, voxelPos, 
                            $"Too many mistakes ({newTotal})! Stone destroyed.");
                    }

                    // Register mistakes but continue
                    AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
                    int remaining = config.MistakeAllowance - newTotal;

                    // Only show durability % if scaling is enabled
                    if (config?.EnableDurabilityScaling ?? true)
                    {
                        float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                        KnappingMessageHelper.NotifyMistakes(byPlayer, protectedCount, remaining, durability);
                    }
                    else
                    {
                        KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
                    }
                    KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);
                }

                // Remove all voxels in the fracture zone
                foreach (var pos in fractureZone)
                {
                    currentVoxels[pos.X, pos.Y] = false;
                }

                // Remove any voxels that became disconnected from the recipe pattern
                AdvancedKnappingHelper.RemoveDisconnectedVoxels(currentVoxels, recipeVoxels);

                // Mark entity dirty so changes persist
                entity.MarkDirty(true);

                // Play chip sound for fracture effect
                KnappingSoundHelper.PlayChipSound(entity.Api, entity.Pos, byPlayer);

                return false; // Prevent vanilla - we handled removal ourselves
            }
        }

        /// <summary>
        /// Handle a single mistake in Advanced Mode
        /// </summary>
        static bool HandleMistake(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, 
            PrecisionKnappingConfig config, int mistakeCount, string message)
        {
            if (entity.Api.Side != EnumAppSide.Server) return true;

            int currentMistakes = AdvancedKnappingHelper.GetMistakeCount(entity);
            int newTotal = currentMistakes + mistakeCount;

            if (newTotal > config.MistakeAllowance)
            {
                // Too many mistakes - break stone
                return HandleProtectedVoxelHit(entity, byPlayer, voxelPos, 
                    $"Too many mistakes ({newTotal})! Stone destroyed.");
            }

            // Register mistake
            AdvancedKnappingHelper.SetMistakeCount(entity, newTotal);
            int remaining = config.MistakeAllowance - newTotal;

            // Only show durability % if scaling is enabled
            if (config?.EnableDurabilityScaling ?? true)
            {
                float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                KnappingMessageHelper.NotifyMistake(byPlayer, remaining, durability);
            }
            else
            {
                KnappingMessageHelper.NotifyMistake(byPlayer, remaining);
            }
            KnappingSoundHelper.PlayWarningSound(entity.Api, entity.Pos, byPlayer);

            // Manually remove the voxel for visual feedback
            KnappingReflectionHelper.RemoveVoxel(entity, voxelPos.X, voxelPos.Z);
            entity.MarkDirty(true);

            // Return FALSE to prevent vanilla from running (which could break recipe validation)
            return false;
        }

        /// <summary>
        /// Handle complete failure - break the stone
        /// </summary>
        static bool HandleProtectedVoxelHit(BlockEntity entity, IPlayer byPlayer, Vec3i voxelPos, string message)
        {
            // Only process on server side
            if (entity.Api.Side != EnumAppSide.Server)
            {
                return true; // Let server handle it
            }

            // Clear mistake cache for this position
            AdvancedKnappingHelper.ClearMistakeCount(entity.Pos);

            // Play stone breaking sound
            KnappingSoundHelper.PlayStoneBreakSound(entity.Api, entity.Pos);
            KnappingMessageHelper.NotifyStoneDestroyed(byPlayer, message);

            try
            {
                BlockPos breakPos = entity.Pos.Copy();
                entity.Api.World.BlockAccessor.BreakBlock(breakPos, byPlayer);
            }
            catch (Exception ex)
            {
                entity.Api.Logger.Error($"[CLEANUP] BreakBlock failed: {ex.Message}");
            }

            return false; // Prevent vanilla OnUseOver
        }
    }

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
                    // Example: 4 items * 1.25 = 5, 4 items * 0.75 = 3, 4 items * 1.13 = 4.52 â†’ 5
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
                                sp.SendMessage(0, $"[Precision Knapping] Perfect! +{bonusPercent}% â†’ {finalQty}/{originalQty} items", EnumChatType.Notification);
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

    #endregion

    #region Crafting Durability Transfer

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

    #endregion
}
