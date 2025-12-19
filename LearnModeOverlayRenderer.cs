using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
    /// <summary>
    /// Client-side overlay that displays voxel safety when looking at knapping surfaces.
    /// Uses the HighlightBlocks API (proven working approach from InstabilityHeatmap).
    /// Shows the knapping block highlighted with a color based on overall safety.
    /// </summary>
    public class LearnModeOverlayRenderer
    {
        private readonly ICoreClientAPI capi;
        private BlockPos currentKnappingPos;
        private double lastUpdateTime = 0;
        private const double UPDATE_INTERVAL_MS = 200;
        private const int HIGHLIGHT_SLOT = 82001;
        
        private bool hasActiveHighlight = false;

        public LearnModeOverlayRenderer(ICoreClientAPI api)
        {
            capi = api;
            capi.Event.RegisterGameTickListener(OnClientTick, 50);
            capi.Logger.Notification("[PrecisionKnapping] Learn Mode overlay initialized");
        }

        private void OnClientTick(float deltaTime)
        {
            var player = capi.World?.Player;
            if (player == null) return;

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
                return;
            }

            currentKnappingPos = blockSel.Position.Copy();
            lastUpdateTime = now;
            UpdateHighlights(blockEntity);
        }

        private bool IsKnappingSurface(BlockEntity entity)
        {
            return entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        private void UpdateHighlights(BlockEntity knappingEntity)
        {
            try
            {
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

                // Analyze all voxels
                var analysis = AnalyzeAllVoxels(currentVoxels, recipeVoxels);
                
                // Show highlight on the block with color based on overall safety
                ApplyBlockHighlight(analysis);
            }
            catch (Exception ex)
            {
                capi.Logger.Debug($"[PrecisionKnapping] Learn mode error: {ex.Message}");
                ClearHighlights();
            }
        }

        private (int safe, int risky, int danger) AnalyzeAllVoxels(bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var config = PrecisionKnappingModSystem.Config;
            int safeCount = 0;
            int riskyCount = 0;
            int dangerCount = 0;

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (!currentVoxels[x, z]) continue;

                    bool isProtected = recipeVoxels[x, 0, z];
                    if (isProtected) continue;

                    int safety = AnalyzeVoxelSafety(x, z, currentVoxels, recipeVoxels, config);
                    
                    if (safety == 0) safeCount++;
                    else if (safety == 1) riskyCount++;
                    else dangerCount++;
                }
            }

            return (safeCount, riskyCount, dangerCount);
        }

        private int AnalyzeVoxelSafety(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            // Edge voxel = Safe
            if (AdvancedKnappingHelper.IsEdgeVoxel(x, z, currentVoxels, recipeVoxels))
                return 0;

            // Enclosed waste pocket = Safe
            var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(x, z, currentVoxels, recipeVoxels);
            if (pocket.Count > 0)
                return 0;

            // Path to edge - count protected voxels
            var path = AdvancedKnappingHelper.FindPathToNearestEdge(x, z, currentVoxels, recipeVoxels);
            if (path.Count == 0)
                return 0;

            int protectedInPath = 0;
            foreach (var pos in path)
            {
                if (recipeVoxels[pos.X, 0, pos.Y] && currentVoxels[pos.X, pos.Y])
                    protectedInPath++;
            }

            if (protectedInPath == 0) return 0;
            else if (protectedInPath <= 2) return 1;
            else return 2;
        }

        private void ApplyBlockHighlight((int safe, int risky, int danger) analysis)
        {
            var player = capi.World.Player;
            if (player == null || currentKnappingPos == null) return;

            var positions = new List<BlockPos> { currentKnappingPos.Copy() };
            var colors = new List<int>();

            // Determine overall color based on remaining voxels
            int total = analysis.safe + analysis.risky + analysis.danger;
            int color;
            
            if (total == 0 || analysis.safe == total)
            {
                // All safe - green
                color = ColorUtil.ColorFromRgba(50, 220, 50, 80);
            }
            else if (analysis.danger > analysis.risky)
            {
                // Mostly dangerous - red
                color = ColorUtil.ColorFromRgba(220, 50, 50, 80);
            }
            else if (analysis.risky > 0 || analysis.danger > 0)
            {
                // Some risk - yellow
                color = ColorUtil.ColorFromRgba(220, 220, 50, 80);
            }
            else
            {
                // Safe - green
                color = ColorUtil.ColorFromRgba(50, 220, 50, 80);
            }
            
            colors.Add(color);

            capi.World.HighlightBlocks(
                player,
                HIGHLIGHT_SLOT,
                positions,
                colors,
                EnumHighlightBlocksMode.Absolute,
                EnumHighlightShape.Arbitrary,
                1f
            );

            hasActiveHighlight = true;

            // Also show status in chat (throttled)
            string status = $"[Learn Mode] Safe: {analysis.safe} | Risky: {analysis.risky} | Danger: {analysis.danger}";
            // Only show message occasionally to not spam
            if (capi.World.ElapsedMilliseconds % 2000 < 100)
            {
                capi.ShowChatMessage(status);
            }
        }

        private void ClearHighlights()
        {
            if (!hasActiveHighlight) return;

            var player = capi.World?.Player;
            if (player == null) return;

            capi.World.HighlightBlocks(
                player,
                HIGHLIGHT_SLOT,
                new List<BlockPos>(),
                EnumHighlightBlocksMode.Absolute,
                EnumHighlightShape.Arbitrary
            );

            hasActiveHighlight = false;
            currentKnappingPos = null;
        }
    }
}
