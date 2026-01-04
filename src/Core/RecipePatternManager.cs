using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace precisionknapping
{
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
}
