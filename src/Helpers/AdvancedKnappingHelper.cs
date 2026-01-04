using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using System;
using System.Collections.Generic;

namespace precisionknapping
{
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
}
