using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace precisionknapping
{
    /// <summary>
    /// Centralized sound feedback for knapping actions.
    ///
    /// NOTE on "exceptPlayer": the game's PlaySoundAt(..., dualCallByPlayer, ...) sends the sound to
    /// every player EXCEPT dualCallByPlayer, because it assumes that player's client already played it.
    /// Server-only sounds (warnings, fractures) must therefore pass null, or the knapper never hears them.
    /// </summary>
    public static class KnappingSoundHelper
    {
        private static readonly AssetLocation WarningSound = new AssetLocation("game:sounds/effect/toolbreak");
        private static readonly AssetLocation ChipSound = new AssetLocation("game:sounds/block/rock-hit-flint");
        private static readonly AssetLocation BreakSound = new AssetLocation("game:sounds/block/heavyice");

        /// <summary>
        /// Mistake warning. Heard by everyone nearby, including the knapper.
        /// </summary>
        public static void PlayWarningSound(ICoreAPI api, BlockPos pos)
        {
            api.World.PlaySoundAt(WarningSound, pos, 0, null, true, 16, 0.5f);
        }

        /// <summary>
        /// Stone chip/crack sound for fractures and flakes.
        /// Pass exceptPlayer only when that player's client already played its own strike sound.
        /// </summary>
        public static void PlayChipSound(ICoreAPI api, BlockPos pos, IPlayer exceptPlayer = null, float volume = 1.0f)
        {
            api.World.PlaySoundAt(ChipSound, pos, 0, exceptPlayer, true, 16, volume);
        }

        public static void PlayStoneBreakSound(ICoreAPI api, BlockPos pos)
        {
            api.World.PlaySoundAt(BreakSound, pos, 0, null, true, 32, 1.0f);
        }
    }
}
