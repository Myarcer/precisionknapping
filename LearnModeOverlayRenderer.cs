using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
    /// <summary>
    /// Client-side renderer that displays a visual overlay on knapping surfaces.
    /// Colors waste voxels by safety: green = safe, yellow/red = risky.
    /// Protected voxels (final tool shape) remain uncolored.
    /// </summary>
    public class LearnModeOverlayRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private MeshRef overlayMeshRef;
        private BlockPos currentKnappingPos;
        private bool meshNeedsUpdate = true;
        private double lastUpdateTime = 0;
        private const double UPDATE_INTERVAL_MS = 100; // Throttle updates

        // Color constants (RGBA)
        private static readonly int COLOR_SAFE = ColorUtil.ColorFromRgba(50, 200, 50, 100);      // Green - safe edge/enclosed
        private static readonly int COLOR_LOW_RISK = ColorUtil.ColorFromRgba(200, 200, 50, 100); // Yellow - low risk
        private static readonly int COLOR_HIGH_RISK = ColorUtil.ColorFromRgba(200, 50, 50, 100); // Red - high risk

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public LearnModeOverlayRenderer(ICoreClientAPI api)
        {
            capi = api;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "precisionknapping-learnmode");
            capi.Logger.Debug("[PrecisionKnapping] Learn Mode overlay renderer registered");
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
                ClearMesh();
                return;
            }

            var blockEntity = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (blockEntity == null || !IsKnappingSurface(blockEntity))
            {
                ClearMesh();
                return;
            }

            // Throttle updates
            double now = capi.World.ElapsedMilliseconds;
            if (now - lastUpdateTime < UPDATE_INTERVAL_MS && !meshNeedsUpdate)
            {
                RenderOverlay(blockSel.Position);
                return;
            }

            // Update or create mesh
            if (currentKnappingPos == null || !currentKnappingPos.Equals(blockSel.Position) || meshNeedsUpdate)
            {
                currentKnappingPos = blockSel.Position.Copy();
                UpdateOverlayMesh(blockEntity);
                lastUpdateTime = now;
                meshNeedsUpdate = false;
            }

            RenderOverlay(blockSel.Position);
        }

        private bool IsKnappingSurface(BlockEntity entity)
        {
            return entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        private void UpdateOverlayMesh(BlockEntity knappingEntity)
        {
            try
            {
                // Get voxel data using reflection (same as main mod)
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(knappingEntity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(knappingEntity);

                if (currentVoxels == null || selectedRecipe == null)
                {
                    ClearMesh();
                    return;
                }

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
                if (recipeVoxels == null)
                {
                    ClearMesh();
                    return;
                }

                // Analyze each voxel and build mesh
                var meshData = GenerateOverlayMesh(currentVoxels, recipeVoxels);
                
                if (meshData != null && meshData.VerticesCount > 0)
                {
                    overlayMeshRef?.Dispose();
                    overlayMeshRef = capi.Render.UploadMesh(meshData);
                }
                else
                {
                    ClearMesh();
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug($"[PrecisionKnapping] Learn mode overlay error: {ex.Message}");
                ClearMesh();
            }
        }

        private MeshData GenerateOverlayMesh(bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var quads = new List<(float x, float z, int color)>();
            var config = PrecisionKnappingModSystem.Config;

            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (!currentVoxels[x, z]) continue; // No voxel here

                    bool isProtected = recipeVoxels[x, 0, z];
                    if (isProtected) continue; // Don't overlay protected voxels

                    // Analyze safety of this waste voxel
                    int color = AnalyzeVoxelSafety(x, z, currentVoxels, recipeVoxels, config);
                    quads.Add((x, z, color));
                }
            }

            if (quads.Count == 0) return null;

            // Build mesh: one quad per voxel, slightly above the surface
            int vertexCount = quads.Count * 4;
            int indexCount = quads.Count * 6;

            var meshData = new MeshData(vertexCount, indexCount, false, true, true, false);
            meshData.SetMode(EnumDrawMode.Triangles);

            float voxelSize = 1f / 16f;
            float yOffset = 0.002f; // Slightly above surface to prevent z-fighting

            int vertexIndex = 0;
            foreach (var (vx, vz, color) in quads)
            {
                float x0 = vx * voxelSize;
                float x1 = (vx + 1) * voxelSize;
                float z0 = vz * voxelSize;
                float z1 = (vz + 1) * voxelSize;

                // Add 4 vertices for this quad
                meshData.AddVertex(x0, yOffset, z0, 0, 0, color);
                meshData.AddVertex(x1, yOffset, z0, 1, 0, color);
                meshData.AddVertex(x1, yOffset, z1, 1, 1, color);
                meshData.AddVertex(x0, yOffset, z1, 0, 1, color);

                // Add 2 triangles (6 indices)
                int baseIdx = vertexIndex;
                meshData.AddIndex(baseIdx);
                meshData.AddIndex(baseIdx + 1);
                meshData.AddIndex(baseIdx + 2);
                meshData.AddIndex(baseIdx);
                meshData.AddIndex(baseIdx + 2);
                meshData.AddIndex(baseIdx + 3);

                vertexIndex += 4;
            }

            return meshData;
        }

        private int AnalyzeVoxelSafety(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            // Check 1: Is this an edge voxel? (Safe)
            if (AdvancedKnappingHelper.IsEdgeVoxel(x, z, currentVoxels, recipeVoxels))
            {
                return COLOR_SAFE;
            }

            // Check 2: Is this part of an enclosed waste pocket? (Safe)
            var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(x, z, currentVoxels, recipeVoxels);
            if (pocket.Count > 0)
            {
                return COLOR_SAFE;
            }

            // Check 3: Calculate fracture damage if clicked
            var path = AdvancedKnappingHelper.FindPathToNearestEdge(x, z, currentVoxels, recipeVoxels);
            if (path.Count == 0)
            {
                return COLOR_SAFE; // Isolated, treated as safe
            }

            // Calculate fracture zone
            var fractureZone = FractureCalculator.CalculateFractureZone(path, currentVoxels, config);
            int protectedCount = FractureCalculator.CountProtectedInZone(fractureZone, recipeVoxels, currentVoxels);

            // Color based on damage potential
            if (protectedCount == 0)
                return COLOR_SAFE;
            else if (protectedCount <= 2)
                return COLOR_LOW_RISK;
            else
                return COLOR_HIGH_RISK;
        }

        private void RenderOverlay(BlockPos pos)
        {
            if (overlayMeshRef == null) return;

            IRenderAPI render = capi.Render;
            IShaderProgram shader = render.CurrentActiveShader;

            // Position the mesh at the knapping surface
            var modelMat = new Matrixf();
            modelMat.Identity();
            modelMat.Translate(pos.X - capi.World.Player.Entity.CameraPos.X,
                              pos.Y - capi.World.Player.Entity.CameraPos.Y + 1f, // +1 to be on top of surface
                              pos.Z - capi.World.Player.Entity.CameraPos.Z);

            shader?.UniformMatrix("modelMatrix", modelMat.Values);
            render.RenderMesh(overlayMeshRef);
        }

        private void ClearMesh()
        {
            if (overlayMeshRef != null)
            {
                overlayMeshRef.Dispose();
                overlayMeshRef = null;
            }
            currentKnappingPos = null;
        }

        public void Dispose()
        {
            ClearMesh();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
