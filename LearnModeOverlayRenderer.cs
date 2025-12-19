using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
    /// <summary>
    /// Client-side overlay that displays per-voxel safety indicators on knapping surfaces.
    /// Uses particles to show colored dots at each waste voxel position.
    /// Green = safe, Yellow = low risk, Red = high risk.
    /// </summary>
    public class LearnModeOverlayRenderer
    {
        private readonly ICoreClientAPI capi;
        private BlockPos currentKnappingPos;
        private double lastUpdateTime = 0;
        private const double UPDATE_INTERVAL_MS = 500; // Spawn particles every 500ms

        // Particle properties for each safety level
        private SimpleParticleProperties safeParticle;
        private SimpleParticleProperties riskyParticle;
        private SimpleParticleProperties dangerParticle;

        public LearnModeOverlayRenderer(ICoreClientAPI api)
        {
            capi = api;
            
            // Initialize particle templates
            InitParticles();
            
            capi.Event.RegisterGameTickListener(OnClientTick, 100);
            capi.Logger.Notification("[PrecisionKnapping] Learn Mode overlay initialized (particle-based)");
        }

        private void InitParticles()
        {
            // Safe - Green particle
            safeParticle = new SimpleParticleProperties(
                1, 1,                          // minQuantity, maxQuantity
                ColorUtil.ColorFromRgba(50, 220, 50, 180),  // color
                new Vec3d(), new Vec3d(),      // minPos, addPos (set per spawn)
                new Vec3f(0, 0.02f, 0),        // minVelocity - slight upward drift
                new Vec3f(0, 0.02f, 0),        // maxVelocity
                0.5f,                          // lifeLength
                0f,                            // gravityEffect
                0.03f, 0.05f                   // minSize, maxSize
            );
            safeParticle.ParticleModel = EnumParticleModel.Cube;
            safeParticle.SelfPropelled = true;

            // Risky - Yellow particle
            riskyParticle = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ColorFromRgba(220, 220, 50, 180),
                new Vec3d(), new Vec3d(),
                new Vec3f(0, 0.02f, 0),
                new Vec3f(0, 0.02f, 0),
                0.5f, 0f,
                0.03f, 0.05f
            );
            riskyParticle.ParticleModel = EnumParticleModel.Cube;
            riskyParticle.SelfPropelled = true;

            // Danger - Red particle
            dangerParticle = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ColorFromRgba(220, 50, 50, 180),
                new Vec3d(), new Vec3d(),
                new Vec3f(0, 0.02f, 0),
                new Vec3f(0, 0.02f, 0),
                0.5f, 0f,
                0.03f, 0.05f
            );
            dangerParticle.ParticleModel = EnumParticleModel.Cube;
            dangerParticle.SelfPropelled = true;
        }

        private void OnClientTick(float deltaTime)
        {
            var player = capi.World?.Player;
            if (player == null) return;

            BlockSelection blockSel = player.CurrentBlockSelection;
            if (blockSel == null)
            {
                currentKnappingPos = null;
                return;
            }

            var blockEntity = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (blockEntity == null || !IsKnappingSurface(blockEntity))
            {
                currentKnappingPos = null;
                return;
            }

            // Throttle particle spawns
            double now = capi.World.ElapsedMilliseconds;
            if (now - lastUpdateTime < UPDATE_INTERVAL_MS)
            {
                return;
            }

            currentKnappingPos = blockSel.Position.Copy();
            lastUpdateTime = now;
            SpawnVoxelParticles(blockEntity);
        }

        private bool IsKnappingSurface(BlockEntity entity)
        {
            return entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        private void SpawnVoxelParticles(BlockEntity knappingEntity)
        {
            try
            {
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(knappingEntity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(knappingEntity);

                if (currentVoxels == null || selectedRecipe == null) return;

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
                if (recipeVoxels == null) return;

                var config = PrecisionKnappingModSystem.Config;
                float voxelSize = 1f / 16f;

                // Spawn a particle at each waste voxel position
                for (int x = 0; x < 16; x++)
                {
                    for (int z = 0; z < 16; z++)
                    {
                        if (!currentVoxels[x, z]) continue;

                        bool isProtected = recipeVoxels[x, 0, z];
                        if (isProtected) continue; // Don't show particles on protected voxels

                        int safety = AnalyzeVoxelSafety(x, z, currentVoxels, recipeVoxels, config);
                        
                        // Calculate world position for this voxel
                        double worldX = currentKnappingPos.X + (x + 0.5) * voxelSize;
                        double worldY = currentKnappingPos.Y + 1.01; // Slightly above surface
                        double worldZ = currentKnappingPos.Z + (z + 0.5) * voxelSize;

                        // Select particle based on safety
                        SimpleParticleProperties particle;
                        if (safety == 0) particle = safeParticle;
                        else if (safety == 1) particle = riskyParticle;
                        else particle = dangerParticle;

                        // Set position and spawn
                        particle.MinPos.Set(worldX, worldY, worldZ);
                        capi.World.SpawnParticles(particle);
                    }
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug($"[PrecisionKnapping] Particle spawn error: {ex.Message}");
            }
        }

        private int AnalyzeVoxelSafety(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            // Edge voxel = Safe (0)
            if (AdvancedKnappingHelper.IsEdgeVoxel(x, z, currentVoxels, recipeVoxels))
                return 0;

            // Enclosed waste pocket = Safe (0)
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

            if (protectedInPath == 0) return 0;      // Safe
            else if (protectedInPath <= 2) return 1; // Risky
            else return 2;                           // Danger
        }
    }
}
