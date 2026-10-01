using System;

namespace precisionknapping
{
    /// <summary>
    /// Configuration for Precision Knapping mod
    /// Loaded from modconfig/precisionknapping.json
    /// </summary>
    public class PrecisionKnappingConfig
    {
        /// <summary>
        /// When enabled, activates edge enforcement and durability scaling on mistakes.
        /// When disabled (Default Mode), mistakes still count but only toward breaking - no durability penalty.
        /// Default: false
        /// </summary>
        public bool AdvancedMode { get; set; } = false;

        /// <summary>
        /// Number of protected voxels that can be accidentally removed before stone breaks.
        /// Used in BOTH modes:
        /// - Default Mode: Mistakes allowed before stone breaks (no durability penalty, get 100% or nothing)
        /// - Advanced Mode: 0 = 100%, 1 = 75%, 2 = 50%, 3 = 25% durability, exceeding = stone breaks
        /// Default: 1 (original strict behavior - first mistake breaks stone)
        /// </summary>
        public int MistakeAllowance { get; set; } = 1;

        /// <summary>
        /// Cone angle in degrees for fracture spread (Hertzian cone simulation).
        /// Real flint has ~100-136┬░ but we use gameplay-tuned values.
        /// 0 = no spread (only direct line), 60 = narrow cone, 120 = wide cone
        /// Default: 90 (balanced)
        /// </summary>
        public int FractureConeAngle { get; set; } = 90;

        /// <summary>
        /// How much the fracture widens per voxel of distance from impact.
        /// 0 = constant width, 0.5 = moderate spread, 1.0 = aggressive spread
        /// Default: 0.4
        /// </summary>
        public float FractureSpreadRate { get; set; } = 0.4f;

        /// <summary>
        /// Probability decay per voxel distance - fractures become less predictable further from impact.
        /// 0 = fully deterministic, 0.15 = slight randomness, 0.3 = chaotic edges
        /// Default: 0.15
        /// </summary>
        public float FractureDecay { get; set; } = 0.15f;

        /// <summary>
        /// Base probability for voxels within the cone to be included.
        /// 1.0 = all voxels in cone break, 0.7 = 70% chance per voxel
        /// Default: 0.85
        /// </summary>
        public float FractureBaseProbability { get; set; } = 0.85f;

        /// <summary>
        /// Enable Learn Mode visual overlay when knapping.
        /// Colors waste voxels by safety: green = safe (edge/enclosed), yellow/red = risky (would cause fracture).
        /// Protected voxels (final tool shape) remain uncolored.
        /// Client-side only, no performance impact on server.
        /// Default: false
        /// </summary>
        public bool LearnModeOverlay { get; set; } = false;

        /// <summary>
        /// Enable durability/quantity scaling based on knapping precision.
        /// When enabled: 0 mistakes = bonus, more mistakes = penalty.
        /// Works in BOTH Default and Advanced modes.
        /// When disabled: always get vanilla 100% durability/quantity.
        /// Default: true
        /// </summary>
        public bool EnableDurabilityScaling { get; set; } = true;

        /// <summary>
        /// Bonus durability multiplier for PERFECT knapping (0 mistakes).
        /// This is ADDED to base durability: 0.25 = +25% durability (1.25x total)
        /// Set to 0 to disable bonuses (penalties still apply).
        /// Default: 0.25 (25% bonus)
        /// </summary>
        public float PerfectKnappingBonus { get; set; } = 0.25f;

        // ==================== CHARGED STRIKES ====================

        /// <summary>
        /// Enable Charged Strikes: hold left-click to pull the stone back, release to strike.
        /// Works with both Default and Advanced mode. The server's value is sent to every client.
        /// Default: false
        /// </summary>
        public bool ChargedStrikes { get; set; } = false;

        /// <summary>
        /// Minimum hold time in milliseconds. Releasing earlier is a "tap" that does nothing.
        /// Default: 350
        /// </summary>
        public int MinChargeTimeMs { get; set; } = 350;

        /// <summary>
        /// Hold time in milliseconds for a full charge (a power strike).
        /// Charge level = hold time / FullChargeTimeMs, capped at 100%.
        /// Default: 1200
        /// </summary>
        public int FullChargeTimeMs { get; set; } = 1200;

        /// <summary>
        /// Size of the flake a fully charged power strike knocks off.
        /// 0 = power strikes remove one voxel (impact effects only), 1 = target + 4 neighbours,
        /// 2 = a diamond of 13 voxels. Protected voxels inside the flake count as mistakes.
        /// In Advanced Mode, a power strike on an interior voxel widens the fracture cone instead.
        /// Range: 0 - 3. Default: 1
        /// </summary>
        public int PowerStrikeRadius { get; set; } = 1;

        /// <summary>
        /// Client: play charge cues (strike ready / power ready) and heavier strike sounds.
        /// Default: true
        /// </summary>
        public bool EnableChargeSounds { get; set; } = true;

        /// <summary>
        /// Client: shake the camera a little on strong strikes.
        /// Default: true
        /// </summary>
        public bool EnableStrikeCameraShake { get; set; } = true;

        /// <summary>
        /// Client: the player animation held while charging (arm pulled back).
        /// "aim" is the game's throw wind-up pose. Change only if you know another animation code.
        /// Default: "aim"
        /// </summary>
        public string ChargeAnimation { get; set; } = "aim";

        /// <summary>
        /// Client: the player animation played once on release.
        /// "knap" is the game's knapping swing.
        /// Default: "knap"
        /// </summary>
        public string StrikeAnimation { get; set; } = "knap";
    }
}