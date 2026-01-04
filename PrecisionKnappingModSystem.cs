using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using System;
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
    /// - src/Features/RealisticStrikes.cs         - Charge-to-strike mechanics
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
        private static ChargeStateTracker chargeTracker;

        public static PrecisionKnappingConfig Config => config;
        public static ICoreServerAPI ServerApi => serverApi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            instance = this;

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

            // Apply Harmony patches
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

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            serverApi = api;

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
    }
}
