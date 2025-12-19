using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
    /// <summary>
    /// Client-side overlay that displays voxel safety when looking at knapping surfaces.
    /// Uses VS highlight system for reliable rendering.
    /// Colors waste voxels by safety: green = safe, yellow/red = risky.
    /// </summary>
    public class LearnModeOverlayRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private BlockPos currentKnappingPos;
        private double lastUpdateTime = 0;
        private const double UPDATE_INTERVAL_MS = 200; // Throttle updates
        private const int HIGHLIGHT_SLOT = 82001; // Unique slot for our highlights
        
        private bool hasActiveHighlight = false;

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public LearnModeOverlayRenderer(ICoreClientAPI api)
        {
            capi = api;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "precisionknapping-learnmode");
            capi.Logger.Notification("[PrecisionKnapping] Learn Mode overlay renderer initialized");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;

            var player = capi.World.Player;
            if (player == null) return;

            // Find knapping surface player is looking at
            BlockSelection blockSel = player.CurrentBlockSelection;
            if (blockSel == null)
            {
                ClearHighlights();
                return;
            }

            var blockEntity = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (blockEntity == null || !IsKnappingSurface(blockEntity))
            {
                ClearHighlights();
                return;
            }

            // Throttle updates
            double now = capi.World.ElapsedMilliseconds;
            if (currentKnappingPos != null && currentKnappingPos.Equals(blockSel.Position) && 
                now - lastUpdateTime < UPDATE_INTERVAL_MS)
            {
                return; // Skip update, keep existing highlights
            }

            // Update highlights
            currentKnappingPos = blockSel.Position.Copy();
            lastUpdateTime = now;
            UpdateHighlights(blockEntity, blockSel.Position);
        }

        private bool IsKnappingSurface(BlockEntity entity)
        {
            return entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        private void UpdateHighlights(BlockEntity knappingEntity, BlockPos pos)
        {
            try
            {
                // Get voxel data using reflection
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(knappingEntity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(knappingEntity);

                if (currentVoxels == null || selectedRecipe == null)
                {
                    ClearHighlights();
                    return;
                }

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
                if (recipeVoxels == null)
                {
                    ClearHighlights();
                    return;
                }

                // Analyze voxels and create text-based feedback via chat
                // (Block highlight system doesn't support sub-block positions)
                AnalyzeAndShowFeedback(currentVoxels, recipeVoxels);
            }
            catch (Exception ex)
            {
                capi.Logger.Debug($"[PrecisionKnapping] Learn mode error: {ex.Message}");
                ClearHighlights();
            }
        }

        private void AnalyzeAndShowFeedback(bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var config = PrecisionKnappingModSystem.Config;
            
            int safeEdgeCount = 0;
            int safeEnclosedCount = 0;
            int riskyCount = 0;
            int highRiskCount = 0;
            int protectedCount = 0;

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (!currentVoxels[x, z]) continue;

                    bool isProtected = recipeVoxels[x, 0, z];
                    if (isProtected)
                    {
                        protectedCount++;
                        continue;
                    }

                    // Analyze safety of this waste voxel
                    int safety = AnalyzeVoxelSafety(x, z, currentVoxels, recipeVoxels, config);
                    
                    if (safety == 0) safeEdgeCount++;
                    else if (safety == 1) safeEnclosedCount++;
                    else if (safety == 2) riskyCount++;
                    else highRiskCount++;
                }
            }

            // Show info bar message (less intrusive than chat)
            string status = $"Safe: {safeEdgeCount + safeEnclosedCount} | Risky: {riskyCount} | Danger: {highRiskCount}";
            capi.ShowChatMessage($"[Learn Mode] {status}");
            hasActiveHighlight = true;
        }

        /// <summary>
        /// Returns: 0 = safe edge, 1 = safe enclosed, 2 = low risk, 3 = high risk
        /// Uses DETERMINISTIC checks only - no random fracture simulation
        /// </summary>
        private int AnalyzeVoxelSafety(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            // Check 1: Is this an edge voxel? (Safe)
            if (AdvancedKnappingHelper.IsEdgeVoxel(x, z, currentVoxels, recipeVoxels))
            {
                return 0;
            }

            // Check 2: Is this part of an enclosed waste pocket? (Safe)
            var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(x, z, currentVoxels, recipeVoxels);
            if (pocket.Count > 0)
            {
                return 1;
            }

            // Check 3: Use PATH LENGTH as proxy for danger (deterministic, no randomness)
            // Longer path = more voxels crossed = higher chance of hitting protected voxels
            var path = AdvancedKnappingHelper.FindPathToNearestEdge(x, z, currentVoxels, recipeVoxels);
            if (path.Count == 0)
            {
                return 0; // Isolated, treated as safe
            }

            // Count protected voxels ALONG THE PATH (deterministic)
            int protectedInPath = 0;
            foreach (var pos in path)
            {
                if (recipeVoxels[pos.X, 0, pos.Y] && currentVoxels[pos.X, pos.Y])
                {
                    protectedInPath++;
                }
            }

            // Categorize by damage potential
            if (protectedInPath == 0) return 0;      // Path is clear
            else if (protectedInPath <= 2) return 2; // Low risk
            else return 3;                           // High risk
        }

        private void ClearHighlights()
        {
            if (hasActiveHighlight)
            {
                hasActiveHighlight = false;
                currentKnappingPos = null;
            }
        }

        public void Dispose()
        {
            ClearHighlights();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
