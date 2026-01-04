using Vintagestory.API.MathTools;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
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
}
