using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace precisionknapping
{
    /// <summary>
    /// Precision Knapping mod - Harmony patching approach
    ///
    /// ARCHITECTURE: Uses Harmony patches to intercept BlockEntityKnappingSurface.OnUseOver()
    ///
    /// MODES:
    /// - Default Mode: First protected voxel click breaks stone (strict)
    /// - Advanced Mode: Edge enforcement + mistake tolerance with durability scaling
    ///   - Must click from outer edges inward (virtual edges for recipe holes)
    ///   - Clicking non-edge voxel removes line to nearest edge (risky)
    ///   - Each protected voxel removed counts as mistake
    ///   - Mistakes reduce durability: 0=100%, 1=75%, 2=50%, 3=25%, 4+=break
    ///   - Tool heads get durability reduction, non-tool items lose quantity
    ///
    /// FILE STRUCTURE:
    /// - src/Core/PrecisionKnappingConfig.cs      - Configuration class
    /// - src/Helpers/KnappingReflectionHelper.cs  - Vanilla reflection access
    /// - src/Helpers/KnappingSoundHelper.cs       - Sound feedback
    /// - src/Helpers/KnappingMessageHelper.cs     - Player messages
    /// - src/Helpers/AdvancedKnappingHelper.cs    - Edge detection, mistakes
    /// - src/Features/FractureCalculator.cs       - Fracture zone mechanics
    /// - src/Features/LearnModeOverlayRenderer.cs - Visual overlay
    /// - src/Patches/KnappingSurfacePatch.cs      - OnUseOver Harmony patch
    /// - src/Patches/KnappingCompletionPatch.cs   - CheckIfFinished patch
    /// - src/Patches/CraftingTransferPatch.cs     - Crafting durability patch
    /// </summary>
    public class PrecisionKnappingModSystem : ModSystem
    {
        private static PrecisionKnappingModSystem instance;
        private Harmony harmony;
        private static PrecisionKnappingConfig config;
        private static ICoreServerAPI serverApi;

        public static PrecisionKnappingConfig Config => config;
        public static ICoreServerAPI ServerApi => serverApi;
        public static ICoreClientAPI Capi { get; private set; }
        public static PrecisionKnappingModSystem Instance => instance;
        public ICoreAPI Api { get; private set; }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            instance = this;
            Api = api;

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

                 // Register our attribute to be ignored by stack comparisons (fixes Tutorial issues)
                var ignored = new List<string>(GlobalConstants.IgnoredStackAttributes);
                if (!ignored.Contains("precisionknapping:durabilityRatio"))
                {
                    ignored.Add("precisionknapping:durabilityRatio");
                    GlobalConstants.IgnoredStackAttributes = ignored.ToArray();
                    api.Logger.Notification("[PrecisionKnapping] Registered 'precisionknapping:durabilityRatio' as ignored attribute.");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error($"[PrecisionKnapping] Failed to load config: {ex.Message}");
                config = new PrecisionKnappingConfig();
            }

            // Apply Harmony patches individually so one failure doesn't block all others
            harmony = new Harmony("com.precisionknapping.mod");
            var patchTypes = new Type[]
            {
                typeof(KnappingSurfacePatch),
                typeof(KnappingCompletionPatch),
                typeof(CraftingDurabilityTransferPatch)
            };

            foreach (var patchType in patchTypes)
            {
                try
                {
                    var processor = harmony.CreateClassProcessor(patchType);
                    processor.Patch();
                    api.Logger.Notification($"[PrecisionKnapping] Applied patch: {patchType.Name}");
                }
                catch (Exception ex)
                {
                    api.Logger.Error($"[PrecisionKnapping] Failed to apply {patchType.Name}: {ex.Message}");
                    api.Logger.Error($"[PrecisionKnapping] Stack: {ex.StackTrace}");
                }
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.precisionknapping.mod");
            base.Dispose();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            serverApi = api;
            api.Logger.Notification("[PrecisionKnapping] Server initialized");
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            Capi = api;

            // Initialize Learn Mode overlay if enabled
            if (Config?.LearnModeOverlay == true)
            {
                new LearnModeOverlayRenderer(api);
                api.Logger.Notification("[PrecisionKnapping] Learn Mode overlay enabled");
            }
        }
    }
}