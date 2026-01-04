using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace precisionknapping
{
    /// <summary>
    /// Centralized sound feedback for knapping actions
    /// </summary>
    public static class KnappingSoundHelper
    {
        private static readonly AssetLocation WarningSound = new AssetLocation("game:sounds/effect/toolbreak");
        private static readonly AssetLocation ChipSound = new AssetLocation("game:sounds/block/rock-hit-flint");
        private static readonly AssetLocation BreakSound = new AssetLocation("game:sounds/block/heavyice");
        private static readonly AssetLocation CancelSound = new AssetLocation("game:sounds/effect/receptionbell");

        public static void PlayWarningSound(ICoreAPI api, BlockPos pos, IPlayer player)
        {
            api.World.PlaySoundAt(WarningSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, player, true, 16, 0.5f);
        }

        public static void PlayChipSound(ICoreAPI api, BlockPos pos, IPlayer player)
        {
            api.World.PlaySoundAt(ChipSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, player, true, 16, 1.0f);
        }

        public static void PlayStoneBreakSound(ICoreAPI api, BlockPos pos)
        {
            api.World.PlaySoundAt(BreakSound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 32, 1.0f);
        }

        public static void PlayCancelSound(IWorldAccessor world, EntityAgent entity)
        {
            if (world.Side == EnumAppSide.Client)
            {
                world.PlaySoundAt(CancelSound, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, null, false, 8, 0.3f);
            }
        }
    }
}
