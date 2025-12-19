using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
    /// <summary>
    /// Client-side overlay that displays voxel safety when looking at knapping surfaces.
    /// Renders colored quads above each waste voxel: green=safe, yellow=low risk, red=high risk.
    /// Protected voxels (final tool shape) are not colored.
    /// </summary>
    public class LearnModeOverlayRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private MeshRef overlayMeshRef;
        private BlockPos currentKnappingPos;
        private int lastVoxelHash = 0;
        private double lastUpdateTime = 0;
        private const double UPDATE_INTERVAL_MS = 150;

        public double RenderOrder => 0.91; // After opaque blocks
        public int RenderRange => 24;

        public LearnModeOverlayRenderer(ICoreClientAPI api)
        {
            capi = api;
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "precisionknapping-learnmode");
            capi.Logger.Notification("[PrecisionKnapping] Learn Mode overlay initialized");
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            var player = capi.World?.Player;
            if (player == null) return;

            BlockSelection blockSel = player.CurrentBlockSelection;
            if (blockSel == null)
            {
                ClearOverlay();
                return;
            }

            var blockEntity = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (blockEntity == null || !IsKnappingSurface(blockEntity))
            {
                ClearOverlay();
                return;
            }

            // Check if we need to update the mesh
            double now = capi.World.ElapsedMilliseconds;
            bool positionChanged = currentKnappingPos == null || !currentKnappingPos.Equals(blockSel.Position);
            bool needsUpdate = positionChanged || (now - lastUpdateTime > UPDATE_INTERVAL_MS);

            if (needsUpdate)
            {
                currentKnappingPos = blockSel.Position.Copy();
                lastUpdateTime = now;
                RebuildOverlayMesh(blockEntity);
            }

            RenderMesh();
        }

        private bool IsKnappingSurface(BlockEntity entity)
        {
            return entity.GetType().Name == "BlockEntityKnappingSurface";
        }

        private void RebuildOverlayMesh(BlockEntity knappingEntity)
        {
            try
            {
                var currentVoxels = KnappingReflectionHelper.GetCurrentVoxels(knappingEntity);
                var selectedRecipe = KnappingReflectionHelper.GetSelectedRecipe(knappingEntity);

                if (currentVoxels == null || selectedRecipe == null)
                {
                    ClearOverlay();
                    return;
                }

                var recipeVoxels = KnappingReflectionHelper.GetRecipeVoxels(selectedRecipe);
                if (recipeVoxels == null)
                {
                    ClearOverlay();
                    return;
                }

                // Generate colored overlay mesh
                var meshData = GenerateVoxelOverlayMesh(currentVoxels, recipeVoxels);
                
                if (meshData != null && meshData.VerticesCount > 0)
                {
                    overlayMeshRef?.Dispose();
                    overlayMeshRef = capi.Render.UploadMesh(meshData);
                }
                else
                {
                    ClearOverlay();
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug($"[PrecisionKnapping] Overlay mesh error: {ex.Message}");
                ClearOverlay();
            }
        }

        private MeshData GenerateVoxelOverlayMesh(bool[,] currentVoxels, bool[,,] recipeVoxels)
        {
            var config = PrecisionKnappingModSystem.Config;
            var quads = new List<(int x, int z, int color)>();

            // Analyze each voxel
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    if (!currentVoxels[x, z]) continue;

                    bool isProtected = recipeVoxels[x, 0, z];
                    if (isProtected) continue; // Don't overlay protected voxels

                    int safety = AnalyzeVoxelSafety(x, z, currentVoxels, recipeVoxels, config);
                    int color = GetColorForSafety(safety);
                    quads.Add((x, z, color));
                }
            }

            if (quads.Count == 0) return null;

            // Build mesh with colored quads
            // Each quad: 4 vertices, 6 indices (2 triangles)
            var mesh = new MeshData(quads.Count * 4, quads.Count * 6, false, false, true, false);
            
            float voxelSize = 1f / 16f;
            float yOffset = 1.002f; // Slightly above the stone surface (stone is at y+1)

            int idx = 0;
            foreach (var (vx, vz, color) in quads)
            {
                float x0 = vx * voxelSize;
                float x1 = (vx + 1) * voxelSize;
                float z0 = vz * voxelSize;
                float z1 = (vz + 1) * voxelSize;

                // Add 4 vertices (position + color)
                mesh.AddVertexSkipTex(x0, yOffset, z0, color);
                mesh.AddVertexSkipTex(x1, yOffset, z0, color);
                mesh.AddVertexSkipTex(x1, yOffset, z1, color);
                mesh.AddVertexSkipTex(x0, yOffset, z1, color);

                // Two triangles
                int baseIdx = idx * 4;
                mesh.AddIndex(baseIdx);
                mesh.AddIndex(baseIdx + 1);
                mesh.AddIndex(baseIdx + 2);
                mesh.AddIndex(baseIdx);
                mesh.AddIndex(baseIdx + 2);
                mesh.AddIndex(baseIdx + 3);

                idx++;
            }

            return mesh;
        }

        private int AnalyzeVoxelSafety(int x, int z, bool[,] currentVoxels, bool[,,] recipeVoxels, PrecisionKnappingConfig config)
        {
            // Check 1: Edge voxel = Safe
            if (AdvancedKnappingHelper.IsEdgeVoxel(x, z, currentVoxels, recipeVoxels))
                return 0;

            // Check 2: Enclosed waste pocket = Safe
            var pocket = AdvancedKnappingHelper.FindConnectedWastePocket(x, z, currentVoxels, recipeVoxels);
            if (pocket.Count > 0)
                return 0;

            // Check 3: Path to edge - count protected voxels in path
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

        private int GetColorForSafety(int safety)
        {
            // Colors with alpha for transparency (RGBA format)
            switch (safety)
            {
                case 0: // Safe - green
                    return ColorUtil.ColorFromRgba(50, 220, 50, 120);
                case 1: // Low risk - yellow
                    return ColorUtil.ColorFromRgba(220, 220, 50, 120);
                case 2: // High risk - red
                    return ColorUtil.ColorFromRgba(220, 50, 50, 120);
                default:
                    return ColorUtil.ColorFromRgba(100, 100, 100, 80);
            }
        }

        private void RenderMesh()
        {
            if (overlayMeshRef == null || currentKnappingPos == null) return;

            var player = capi.World.Player;
            if (player == null) return;

            IRenderAPI rapi = capi.Render;
            
            // Calculate position relative to camera
            Vec3d camPos = player.Entity.CameraPos;
            
            Matrixf modelMat = new Matrixf();
            modelMat.Identity();
            modelMat.Translate(
                (float)(currentKnappingPos.X - camPos.X),
                (float)(currentKnappingPos.Y - camPos.Y),
                (float)(currentKnappingPos.Z - camPos.Z)
            );

            // Use the standard shader with prepared uniforms
            IShaderProgram shader = rapi.PreparedStandardShader(
                currentKnappingPos.X, 
                currentKnappingPos.Y, 
                currentKnappingPos.Z
            );
            
            shader.Uniform("rgbaAmbientIn", rapi.AmbientColor);
            shader.Uniform("rgbaBlockIn", ColorUtil.WhiteArgbVec);
            shader.Uniform("extraGlow", 0);
            shader.UniformMatrix("modelMatrix", modelMat.Values);
            shader.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
            
            rapi.RenderMesh(overlayMeshRef);
            shader.Stop();
        }

        private void ClearOverlay()
        {
            overlayMeshRef?.Dispose();
            overlayMeshRef = null;
            currentKnappingPos = null;
        }

        public void Dispose()
        {
            ClearOverlay();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
        }
    }
}
