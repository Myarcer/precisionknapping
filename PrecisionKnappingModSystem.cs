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

namespace precisionknapping
{
    #region Configuration

    /// <summary>
    /// Configuration for Precision Knapping mod
    /// Loaded from modconfig/precisionknapping.json
    /// </summary>
    public class PrecisionKnappingConfig
    {
        /// <summary>
        /// When enabled, activates edge enforcement and durability scaling on mistakes.
        /// When disabled (Default Mode), mistakes still count but only toward breaking - no durability penalty.
        /// Default: false
        /// </summary>
        public bool AdvancedMode { get; set; } = false;

        /// <summary>
        /// Number of protected voxels that can be accidentally removed before stone breaks.
        /// Used in BOTH modes:
        /// - Default Mode: Mistakes allowed before stone breaks (no durability penalty, get 100% or nothing)
        /// - Advanced Mode: 0 = 100%, 1 = 75%, 2 = 50%, 3 = 25% durability, exceeding = stone breaks
        /// Default: 1 (original strict behavior - first mistake breaks stone)
        /// </summary>
        public int MistakeAllowance { get; set; } = 1;

        /// <summary>
        /// Cone angle in degrees for fracture spread (Hertzian cone simulation).
        /// Real flint has ~100-136° but we use gameplay-tuned values.
        /// 0 = no spread (only direct line), 60 = narrow cone, 120 = wide cone
        /// Default: 90 (balanced)
        /// </summary>
        public int FractureConeAngle { get; set; } = 90;

        /// <summary>
        /// How much the fracture widens per voxel of distance from impact.
        /// 0 = constant width, 0.5 = moderate spread, 1.0 = aggressive spread
        /// Default: 0.4
        /// </summary>
        public float FractureSpreadRate { get; set; } = 0.4f;

        /// <summary>
        /// Probability decay per voxel distance - fractures become less predictable further from impact.
        /// 0 = fully deterministic, 0.15 = slight randomness, 0.3 = chaotic edges
        /// Default: 0.15
        /// </summary>
        public float FractureDecay { get; set; } = 0.15f;

        /// <summary>
        /// Base probability for voxels within the cone to be included.
        /// 1.0 = all voxels in cone break, 0.7 = 70% chance per voxel
        /// Default: 0.85
        /// </summary>
        public float FractureBaseProbability { get; set; } = 0.85f;

        /// <summary>
        /// Enable Learn Mode visual overlay when knapping.
        /// Colors waste voxels by safety: green = safe (edge/enclosed), yellow/red = risky (would cause fracture).
        /// Protected voxels (final tool shape) remain uncolored.
        /// Client-side only, no performance impact on server.
        /// Default: false
        /// </summary>
        public bool LearnModeOverlay { get; set; } = false;

        /// <summary>
        /// Bonus durability multiplier for PERFECT knapping (0 mistakes).
        /// This is ADDED to base durability: 0.25 = +25% durability (1.25x total)
        /// Set to 0 to disable bonuses (penalties still apply).
        /// Default: 0.25 (25% bonus)
        /// </summary>
        public float PerfectKnappingBonus { get; set; } = 0.25f;
    }

    #endregion

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

        public static PrecisionKnappingConfig Config => config;

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
                    api.StoreModConfig(config, "precisionknapping.json");
                }
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

            // Load all knapping recipes after server is fully initialized
            patternManager.LoadAllKnappingRecipes(api);
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

    #region Helper Classes

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
    }

    /// <summary>
    /// Centralized sound feedback for knapping actions
    /// </summary>
    public static class KnappingSoundHelper
    {
        private static readonly AssetLocation WarningSound = new AssetLocation("game:sounds/effect/toolbreak");
        private static readonly AssetLocation ChipSound = new AssetLocation("game:sounds/block/rock-hit-flint");
        private static readonly AssetLocation BreakSound = new AssetLocation("game:sounds/block/heavyice");
        private static readonly AssetLocation CancelSound = new AssetLocation("game:sounds/effect/receptionbell");

        public static void PlayWarningSound(ICoreAPI api, BlockPos pos, IPlayer player)
        {
            api.World.PlaySoundAt(WarningSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, player, true, 16, 0.5f);
        }

        public static void PlayChipSound(ICoreAPI api, BlockPos pos, IPlayer player)
        {
            api.World.PlaySoundAt(ChipSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, player, true, 16, 1.0f);
        }

        public static void PlayStoneBreakSound(ICoreAPI api, BlockPos pos)
        {
            api.World.PlaySoundAt(BreakSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 32, 1.0f);
        }

        public static void PlayCancelSound(IWorldAccessor world, EntityAgent entity)
        {
            if (world.Side == EnumAppSide.Client)
            {
                world.PlaySoundAt(CancelSound, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, null, false, 8, 0.3f);
            }
        }
    }

    /// <summary>
    /// Centralized player messaging for knapping feedback
    /// </summary>
    public static class KnappingMessageHelper
    {
        private const string Prefix = "[Precision Knapping]";

        public static void NotifyMistake(IPlayer player, int remaining, float durability = -1)
        {
            if (player is IServerPlayer serverPlayer)
            {
                string msg = durability > 0
                    ? $"{Prefix} Mistake! {remaining} remaining ({durability * 100:0}% durability)"
                    : $"{Prefix} Mistake! {remaining} chance(s) remaining.";
                serverPlayer.SendMessage(0, msg, EnumChatType.Notification);
            }
        }

        public static void NotifyMistakes(IPlayer player, int count, int remaining, float durability)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} {count} mistake(s)! {remaining} remaining ({durability * 100:0}% durability)",
                    EnumChatType.Notification);
            }
        }

        public static void NotifyFinalWarning(IPlayer player)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} Final warning! One more mistake breaks the stone.",
                    EnumChatType.Notification);
            }
        }

        public static void NotifyStoneDestroyed(IPlayer player, string reason)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(0, $"{Prefix} {reason}", EnumChatType.Notification);
            }
        }

        public static void NotifyCompletionDurability(IPlayer player, int mistakes, float durability)
        {
            if (player is IServerPlayer serverPlayer)
            {
                string message;
                if (durability > 1.0f)
                {
                    // Bonus durability!
                    int bonusPercent = (int)((durability - 1.0f) * 100);
                    message = $"{Prefix} Perfect! +{bonusPercent}% bonus durability";
                }
                else if (durability >= 0.99f)
                {
                    // Vanilla durability
                    message = $"{Prefix} Completed - standard durability";
                }
                else
                {
                    // Penalty
                    message = $"{Prefix} Completed with {mistakes} mistake(s) - {durability * 100:0}% durability";
                }
                serverPlayer.SendMessage(0, message, EnumChatType.Notification);
            }
        }

        public static void NotifyCompletionQuantity(IPlayer player, int mistakes, int got, int max)
        {
            if (player is IServerPlayer serverPlayer && got < max)
            {
                serverPlayer.SendMessage(0,
                    $"{Prefix} Completed with {mistakes} mistake(s) - received {got}/{max}",
                    EnumChatType.Notification);
            }
        }
    }

    /// <summary>
    /// Calculates fracture spread patterns for non-edge clicks in Advanced Mode.
    /// 
    /// REALISTIC FRACTURE MODEL (Simplified Hertzian Cone):
    /// Real flint fractures in a cone pattern from the impact point. The cone:
    /// - Originates at the strike point
    /// - Spreads outward toward the nearest edge
    /// - Widens with distance (configurable spread rate)
    /// - Has random variation at the edges (simulating material inconsistencies)
    /// 
    /// This creates more organic, directional fractures instead of uniform blobs.
    /// </summary>
    public static class FractureCalculator
    {
        private static Random _random = new Random();

        /// <summary>
        /// Calculate fracture zone using cone-based propagation from impact point to edge.
        /// Models the Hertzian cone fracture pattern seen in real conchoidal fracture.
        /// </summary>
        /// <param name="impactPoint">Where the player clicked (interior voxel)</param>
        /// <param name="nearestEdge">The edge point the fracture propagates toward</param>
        /// <param name="currentVoxels">Current voxel state (16x16)</param>
        /// <param name="config">Config with cone angle and spread settings</param>
        /// <returns>HashSet of all voxel positions to remove</returns>
        public static HashSet<Vec2i> CalculateFractureZone(Vec2i impactPoint, Vec2i nearestEdge, 
            bool[,] currentVoxels, PrecisionKnappingConfig config)
        {
            var fractureZone = new HashSet<Vec2i>();
            
            // Always include impact point
            fractureZone.Add(impactPoint);
            
            // Calculate primary direction vector (impact -> edge)
            float dx = nearestEdge.X - impactPoint.X;
            float dz = nearestEdge.Y - impactPoint.Y;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            
            if (distance < 0.1f)
            {
                // Impact and edge are same point
                return fractureZone;
            }
            
            // Normalize direction
            float dirX = dx / distance;
            float dirZ = dz / distance;
            
            // Perpendicular vector for cone width
            float perpX = -dirZ;
            float perpZ = dirX;
            
            // Convert cone angle to radians and calculate tangent for width
            float coneAngleRad = config.FractureConeAngle * (float)Math.PI / 180f;
            float coneHalfTan = (float)Math.Tan(coneAngleRad / 2f);
            
            // Process each distance step from impact to edge
            int steps = (int)Math.Ceiling(distance);
            for (int step = 0; step <= steps; step++)
            {
                float t = step / distance; // 0 at impact, 1 at edge
                
                // Current center point along the fracture line
                float centerX = impactPoint.X + dirX * step;
                float centerZ = impactPoint.Y + dirZ * step;
                
                // Cone width at this distance (starts narrow, widens with distance)
                // Width = distance * tan(halfAngle) * spreadRate
                float baseWidth = step * coneHalfTan * config.FractureSpreadRate;
                float width = Math.Max(0.5f, baseWidth); // Minimum width of 0.5
                
                // Probability decreases with distance (fracture becomes less predictable)
                float probability = config.FractureBaseProbability - (step * config.FractureDecay);
                probability = Math.Max(0.3f, probability); // Minimum 30% chance
                
                // Sample voxels across the cone width at this distance
                int halfWidth = (int)Math.Ceiling(width);
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    // Position perpendicular to main direction
                    int voxelX = (int)Math.Round(centerX + perpX * w);
                    int voxelZ = (int)Math.Round(centerZ + perpZ * w);
                    
                    // Bounds check
                    if (voxelX < 0 || voxelX >= 16 || voxelZ < 0 || voxelZ >= 16) continue;
                    
                    // Must be existing voxel
                    if (!currentVoxels[voxelX, voxelZ]) continue;
                    
                    // Distance from cone centerline affects probability
                    float distFromCenter = Math.Abs(w) / Math.Max(1f, width);
                    float localProbability = probability * (1f - distFromCenter * 0.5f);
                    
                    // Core of cone (center line) is deterministic
                    bool inCore = Math.Abs(w) <= 0.5f;
                    
                    if (inCore || _random.NextDouble() < localProbability)
                    {
                        fractureZone.Add(new Vec2i(voxelX, voxelZ));
                    }
                }
            }
            
            // Add some "spalling" - small random chips adjacent to the main fracture
            // This simulates secondary fractures and material imperfections
            AddSpalling(fractureZone, currentVoxels, config);
            
            return fractureZone;
        }

        /// <summary>
        /// Legacy overload for compatibility - extracts edge from path.
        /// </summary>
        public static HashSet<Vec2i> CalculateFractureZone(List<Vec2i> path, bool[,] currentVoxels, PrecisionKnappingConfig config)
        {
            if (path == null || path.Count == 0)
                return new HashSet<Vec2i>();
            
            // Path goes from impact point to edge
            Vec2i impactPoint = path[0];
            Vec2i nearestEdge = path[path.Count - 1];
            
            return CalculateFractureZone(impactPoint, nearestEdge, currentVoxels, config);
        }

        /// <summary>
        /// Add small random chips adjacent to main fracture zone (spalling effect).
        /// Real fractures often have small secondary breaks at the edges.
        /// </summary>
        private static void AddSpalling(HashSet<Vec2i> fractureZone, bool[,] currentVoxels, PrecisionKnappingConfig config)
        {
            // Lower probability = less spalling
            float spallProbability = config.FractureDecay * 0.5f;
            if (spallProbability < 0.05f) return;
            
            var toAdd = new List<Vec2i>();
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };
            
            foreach (var pos in fractureZone)
            {
                for (int i = 0; i < 4; i++)
                {
                    int nx = pos.X + dx[i];
                    int nz = pos.Y + dz[i];
                    
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16) continue;
                    if (!currentVoxels[nx, nz]) continue;
                    if (fractureZone.Contains(new Vec2i(nx, nz))) continue;
                    
                    if (_random.NextDouble() < spallProbability)
                    {
                        toAdd.Add(new Vec2i(nx, nz));
                    }
                }
            }
            
            foreach (var pos in toAdd)
            {
                fractureZone.Add(pos);
            }
        }

        /// <summary>
        /// Count protected voxels within a fracture zone.
        /// </summary>
        public static int CountProtectedInZone(HashSet<Vec2i> zone, bool[,,] recipeVoxels, bool[,] currentVoxels)
        {
            int count = 0;
            foreach (var pos in zone)
            {
                if (pos.X >= 0 && pos.X < 16 && pos.Y >= 0 && pos.Y < 16)
                {
                    bool isProtected = recipeVoxels[pos.X, 0, pos.Y] && currentVoxels[pos.X, pos.Y];
                    if (isProtected) count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Helper class for Advanced Mode mechanics: edge detection, path finding, mistake tracking
    /// </summary>
    public static class AdvancedKnappingHelper
    {
        /// <summary>
        /// Check if a voxel is on the edge of the current work piece.
        /// A voxel is an edge if: at boundary OR has at least one empty adjacent cell.
        /// Also considers "virtual edges" - voxels adjacent to recipe pattern holes.
        /// </summary>
        public static bool IsEdgeVoxel(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            // Must be within 16x16 grid
            if (x < 0 || x >= 16 || z < 0 || z >= 16) return false;

            // Voxel must exist to be an edge
            if (!currentVoxels[x, z]) return false;

            // Check 4-directional adjacency
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int nz = z + dz[i];

                // Edge of grid counts as edge
                if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16)
                    return true;

                // Adjacent to empty voxel = edge
                if (!currentVoxels[nx, nz])
                    return true;

                // Virtual edge: adjacent to recipe pattern hole that's already been carved
                // Recipe voxels: true = part of final tool, false = should be removed
                if (recipeVoxels != null && nz < recipeVoxels.GetLength(2) && nx < recipeVoxels.GetLength(0))
                {
                    bool neighborIsRecipeHole = !recipeVoxels[nx, 0, nz]; // not part of tool
                    bool neighborIsCarved = !currentVoxels[nx, nz];
                    if (neighborIsRecipeHole && neighborIsCarved)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Find the nearest edge from a given position using BFS.
        /// Returns the path from start to nearest edge (inclusive).
        /// </summary>
        public static List<Vec2i> FindPathToNearestEdge(int startX, int startZ, bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var visited = new bool[16, 16];
            var parent = new Vec2i[16, 16];
            var queue = new Queue<Vec2i>();

            queue.Enqueue(new Vec2i(startX, startZ));
            visited[startX, startZ] = true;
            parent[startX, startZ] = null;

            Vec2i edgeFound = null;

            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            while (queue.Count > 0 && edgeFound == null)
            {
                var current = queue.Dequeue();

                // Check if current is an edge (but not the starting position for edge check)
                if ((current.X != startX || current.Y != startZ) && IsEdgeVoxel(current.X, current.Y, currentVoxels, recipeVoxels))
                {
                    edgeFound = current;
                    break;
                }

                // Explore neighbors
                for (int i = 0; i < 4; i++)
                {
                    int nx = current.X + dx[i];
                    int nz = current.Y + dz[i];

                    if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16 && 
                        !visited[nx, nz] && currentVoxels[nx, nz])
                    {
                        visited[nx, nz] = true;
                        parent[nx, nz] = current;
                        queue.Enqueue(new Vec2i(nx, nz));
                    }
                }
            }

            // Reconstruct path
            var path = new List<Vec2i>();
            if (edgeFound != null)
            {
                var pos = edgeFound;
                while (pos != null)
                {
                    path.Add(pos);
                    pos = parent[pos.X, pos.Y];
                }
                path.Reverse();
            }

            return path;
        }

        /// <summary>
        /// Get or initialize the mistake count for a BlockEntity.
        /// Uses static dictionary as primary (reliable), with BlockEntity attributes as backup for persistence
        /// </summary>
        private static Dictionary<BlockPos, int> mistakeCountCache = new Dictionary<BlockPos, int>();

        public static int GetMistakeCount(BlockEntity entity)
        {
            try
            {
                // Primary: Use position-based cache (fast, reliable within session)
                if (mistakeCountCache.TryGetValue(entity.Pos, out int cached))
                {
                    return cached;
                }
                
                // Fallback: Check BlockEntity attributes (persists if player walks away and back)
                // BlockEntity has a public Attributes property we can access directly
                if (entity is BlockEntity be && be.Block != null)
                {
                    var attrs = be.GetType().GetProperty("Attributes")?.GetValue(be) as ITreeAttribute;
                    if (attrs != null)
                    {
                        int stored = attrs.GetInt("precisionknapping:mistakes", 0);
                        if (stored > 0)
                        {
                            mistakeCountCache[entity.Pos] = stored; // Sync to cache
                            return stored;
                        }
                    }
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                entity.Api?.Logger.Error($"[MISTAKE-TRACKING] Error getting mistake count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Set the mistake count for a BlockEntity.
        /// Stores in both cache and BlockEntity attributes for reliability
        /// </summary>
        public static void SetMistakeCount(BlockEntity entity, int count)
        {
            try
            {
                // Always update cache (primary)
                mistakeCountCache[entity.Pos] = count;
                
                // Also try to persist to BlockEntity attributes (backup)
                if (entity is BlockEntity be)
                {
                    var attrs = be.GetType().GetProperty("Attributes")?.GetValue(be) as ITreeAttribute;
                    if (attrs != null)
                    {
                        attrs.SetInt("precisionknapping:mistakes", count);
                        be.MarkDirty(true);
                    }
                }
            }
            catch (Exception ex)
            {
                entity.Api?.Logger.Error($"[MISTAKE-TRACKING] Error setting mistake count: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear mistake count when stone is broken or recipe completed
        /// </summary>
        public static void ClearMistakeCount(BlockPos pos)
        {
            mistakeCountCache.Remove(pos);
        }

        /// <summary>
        /// Calculate durability multiplier based on mistakes and allowance.
        /// Uses a GRADUATED CURVE with BONUSES:
        /// 
        /// - 0 mistakes: Maximum bonus (e.g., 1.25 for +25%)
        /// - "Breakeven" point (~40% of allowance): 100% vanilla durability
        /// - Max mistakes (=allowance): Minimum durability
        /// 
        /// Examples (with default 25% bonus):
        /// - Allowance 2: 0=125%, 1=100%, 2=50%
        /// - Allowance 5: 0=125%, 1=115%, 2=100%, 3=75%, 4=50%, 5=25%
        /// - Allowance 10: 0=125%, 1-3=bonus zone, 4=~100%, 5-10=penalty zone down to 10%
        /// 
        /// Minimum durability scales with allowance:
        /// - Low allowance (1-2): min 40-50%
        /// - Medium allowance (3-5): min 20-25%
        /// - High allowance (6+): min 10%
        /// </summary>
        public static float GetDurabilityMultiplier(int mistakes, int allowance = -1)
        {
            var config = PrecisionKnappingModSystem.Config;
            
            // Get allowance from config if not provided
            if (allowance < 0)
            {
                allowance = config?.MistakeAllowance ?? 1;
            }
            
            // Ensure allowance is at least 1
            allowance = Math.Max(1, allowance);
            
            // Get bonus amount from config (default 25%)
            float bonusAmount = config?.PerfectKnappingBonus ?? 0.25f;
            float maxMultiplier = 1.0f + bonusAmount;  // e.g., 1.25 for 25% bonus
            
            // Calculate minimum durability based on allowance
            float minDurability;
            if (allowance <= 1)
                minDurability = 0.50f;  // Allowance 1: min 50%
            else if (allowance <= 2)
                minDurability = 0.40f;  // Allowance 2: min 40%
            else if (allowance <= 3)
                minDurability = 0.25f;  // Allowance 3: min 25%
            else if (allowance <= 5)
                minDurability = 0.20f;  // Allowance 4-5: min 20%
            else
                minDurability = 0.10f;  // Allowance 6+: min 10%
            
            // SMART GRADUATED CURVE:
            // - breakeven = where durability = 100% (vanilla)
            // - For low allowance: breakeven at 1 mistake (halfway point)
            // - For high allowance: breakeven at ~40% of allowance
            //
            // The curve goes: maxMultiplier (0 mistakes) -> 1.0 (breakeven) -> minDurability (max mistakes)
            
            float breakeven;
            if (allowance <= 2)
            {
                // Low allowance: breakeven at 1 mistake
                // 0=bonus, 1=vanilla, 2=min
                breakeven = 1.0f;
            }
            else
            {
                // Higher allowance: breakeven at ~40% of allowance
                // This gives room for both bonus and penalty zones
                breakeven = (float)Math.Round(allowance * 0.4f);
                breakeven = Math.Max(1.0f, breakeven);  // At least 1
            }
            
            float durability;
            
            if (mistakes <= 0)
            {
                // Perfect knapping: maximum bonus
                durability = maxMultiplier;
            }
            else if (mistakes < breakeven)
            {
                // BONUS ZONE: between max bonus and vanilla (linear interpolation)
                // 0 mistakes = maxMultiplier, breakeven mistakes = 1.0
                float t = (float)mistakes / breakeven;
                durability = maxMultiplier - (maxMultiplier - 1.0f) * t;
            }
            else if (mistakes == (int)breakeven)
            {
                // Exactly at breakeven: vanilla durability
                durability = 1.0f;
            }
            else
            {
                // PENALTY ZONE: between vanilla and minimum (linear interpolation)
                // breakeven = 1.0, allowance = minDurability
                float penaltyRange = allowance - breakeven;
                if (penaltyRange <= 0) penaltyRange = 1;
                float mistakesIntoPenalty = mistakes - breakeven;
                float t = mistakesIntoPenalty / penaltyRange;
                durability = 1.0f - (1.0f - minDurability) * t;
            }
            
            // Clamp to valid range
            return Math.Max(minDurability, Math.Min(maxMultiplier, durability));
        }

        /// <summary>
        /// Check if an item is a tool head (gets durability reduction) vs stackable item (loses quantity).
        /// Uses generic pattern detection to support modded items automatically.
        /// Tool heads are items that get crafted into tools with durability.
        /// </summary>
        public static bool IsToolHead(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode)) return false;
            
            string lower = itemCode.ToLowerInvariant();
            
            // === EXCLUSIONS (stackable consumables, not tool heads) ===
            // Arrowheads are stackable ammo - NOT tool heads
            if (lower.Contains("arrow")) return false;
            
            // === GENERIC PATTERN DETECTION ===
            // This catches both vanilla and modded tool heads automatically
            
            // Pattern 1: Contains "head" (axehead, pickaxehead, macehead, spearhead, etc.)
            // Already excluded arrowhead above
            if (lower.Contains("head")) return true;
            
            // Pattern 2: Contains "blade" (knifeblade, swordblade, etc.)
            if (lower.Contains("blade")) return true;
            
            // Pattern 3: Common tool part keywords that become tools with durability
            if (lower.Contains("pickaxe")) return true;  // pickaxe-head variants
            if (lower.Contains("shovel")) return true;
            if (lower.Contains("scythe")) return true;
            if (lower.Contains("hammer")) return true;
            if (lower.Contains("chisel")) return true;
            if (lower.Contains("cleaver")) return true;
            
            // Pattern 4: Modded part prefixes (e.g., "part-macehead-flint")
            if (lower.Contains("part-") && !lower.Contains("arrow")) return true;
            
            return false;
        }

        /// <summary>
        /// Find all waste voxels connected to a starting position that are enclosed
        /// (cannot reach an edge without crossing protected voxels).
        /// Returns the pocket for safe removal, or empty if pocket reaches an edge.
        /// Used for recipes like macehead that have interior waste surrounded by protected voxels.
        /// </summary>
        public static HashSet<Vec2i> FindConnectedWastePocket(int startX, int startZ, 
            bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var pocket = new HashSet<Vec2i>();
            var visited = new bool[16, 16];
            var queue = new Queue<Vec2i>();
            bool reachesEdge = false;

            queue.Enqueue(new Vec2i(startX, startZ));
            visited[startX, startZ] = true;

            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                
                // Only include waste voxels (not protected by recipe)
                bool isProtected = recipeVoxels != null && recipeVoxels[current.X, 0, current.Y];
                if (isProtected)
                    continue; // Stop at protected voxels
                
                pocket.Add(current);

                for (int i = 0; i < 4; i++)
                {
                    int nx = current.X + dx[i];
                    int nz = current.Y + dz[i];

                    // Check if we reached the grid edge or an empty space
                    if (nx < 0 || nx >= 16 || nz < 0 || nz >= 16 || !currentVoxels[nx, nz])
                    {
                        reachesEdge = true; // This pocket is NOT enclosed
                        continue;
                    }

                    if (!visited[nx, nz])
                    {
                        visited[nx, nz] = true;
                        queue.Enqueue(new Vec2i(nx, nz));
                    }
                }
            }

            // Only return the pocket if it's truly enclosed (doesn't reach an edge)
            return reachesEdge ? new HashSet<Vec2i>() : pocket;
        }

        /// <summary>
        /// Remove voxels that are disconnected from the recipe pattern.
        /// Uses flood-fill from recipe-required voxels to find all connected voxels.
        /// Any remaining voxels NOT connected to the pattern are removed as debris.
        /// </summary>
        /// <returns>Number of debris voxels removed</returns>
        public static int RemoveDisconnectedVoxels(bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            if (recipeVoxels == null) return 0;

            var connected = new bool[16, 16];
            var queue = new Queue<Vec2i>();

            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            // Start flood-fill from all recipe pattern voxels that still exist
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (currentVoxels[x, z] && recipeVoxels[x, 0, z])
                    {
                        connected[x, z] = true;
                        queue.Enqueue(new Vec2i(x, z));
                    }
                }
            }

            // Flood-fill to find all voxels connected to the recipe pattern
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                for (int i = 0; i < 4; i++)
                {
                    int nx = current.X + dx[i];
                    int nz = current.Y + dz[i];

                    if (nx >= 0 && nx < 16 && nz >= 0 && nz < 16 &&
                        currentVoxels[nx, nz] && !connected[nx, nz])
                    {
                        connected[nx, nz] = true;
                        queue.Enqueue(new Vec2i(nx, nz));
                    }
                }
            }

            // Remove any voxels that exist but aren't connected to pattern
            int removed = 0;
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (currentVoxels[x, z] && !connected[x, z])
                    {
                        currentVoxels[x, z] = false;
                        removed++;
                    }
                }
            }

            return removed;
        }
    }

    #endregion

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
                    float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
                    int remaining = config.MistakeAllowance - newTotal;

                    KnappingMessageHelper.NotifyMistakes(byPlayer, protectedCount, remaining, durability);
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
            float durability = AdvancedKnappingHelper.GetDurabilityMultiplier(newTotal);
            int remaining = config.MistakeAllowance - newTotal;

            KnappingMessageHelper.NotifyMistake(byPlayer, remaining, durability);
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

                // If no mistakes, let vanilla handle it normally
                if (mistakes == 0) return true;

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
                bool bonusEnabled = config?.PerfectKnappingBonus > 0f && config?.AdvancedMode == true;
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

                // Apply durability scaling based on item type (Advanced Mode handles both bonuses and penalties)
                if (AdvancedKnappingHelper.IsToolHead(itemCode) && config?.AdvancedMode == true)
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
                else if (!AdvancedKnappingHelper.IsToolHead(itemCode) && config?.AdvancedMode == true)
                {
                    // Stackable items (arrowheads) in Advanced Mode: adjust quantity
                    int originalQty = outStack.StackSize;
                    int bonusQty = mistakes == 0 && config?.PerfectKnappingBonus > 0 ? 1 : 0; // +1 for perfect
                    int penaltyQty = mistakes;
                    int finalQty = Math.Max(1, originalQty + bonusQty - penaltyQty);
                    outStack.StackSize = finalQty;
                    
                    if (finalQty != originalQty)
                    {
                        if (finalQty > originalQty)
                        {
                            if (byPlayer is IServerPlayer sp)
                                sp.SendMessage(0, $"[Precision Knapping] Perfect! Bonus: {finalQty}/{originalQty}", EnumChatType.Notification);
                        }
                        else
                        {
                            KnappingMessageHelper.NotifyCompletionQuantity(byPlayer, mistakes, finalQty, originalQty);
                        }
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