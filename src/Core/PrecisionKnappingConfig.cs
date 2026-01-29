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
        /// Real flint has ~100-136° but we use gameplay-tuned values.
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

        /// <summary>
        /// Enable Realistic Strikes: hold-to-charge, release-to-strike mechanics.
        /// When enabled, replaces instant-click with charge-based knapping.
        /// Works with BOTH Default and Advanced modes:
        /// - RealisticStrikes + Default: Must charge strikes, mistake tolerance
        /// - RealisticStrikes + Advanced: Must charge strikes, edge enforcement + fractures
        /// Default: false
        /// </summary>
        public bool RealisticStrikes { get; set; } = false;

        /// <summary>
        /// Minimum charge time in milliseconds before strike can execute.
        /// Quick clicks (below this threshold) do nothing.
        /// Default: 250 (quarter second minimum)
        /// </summary>
        public int MinChargeTimeMs { get; set; } = 250;

        /// <summary>
        /// Time in milliseconds to reach full charge.
        /// Charge level = (hold time) / (full charge time), clamped to 1.0
        /// Longer charges can affect fracture size in Advanced Mode.
        /// Default: 800 (less than a second for full charge)
        /// </summary>
        public int FullChargeTimeMs { get; set; } = 800;

        /// <summary>
        /// Enable charge animation (arm pulling back during charge).
        /// Requires custom animation or uses vanilla fallback.
        /// Default: true
        /// </summary>
        public bool EnableChargeAnimation { get; set; } = true;

        /// <summary>
        /// Enable charge sounds (pitch-scaling charge sound, swoosh on release).
        /// Default: true
        /// </summary>
        public bool EnableChargeSounds { get; set; } = true;

        /// <summary>
        /// Minimum pitch for charge sound (at 0% charge).
        /// Range: 0.5 - 2.0
        /// Default: 0.8
        /// </summary>
        public float ChargeSoundMinPitch { get; set; } = 0.8f;

        /// <summary>
        /// Maximum pitch for charge sound (at 100% charge).
        /// Range: 0.5 - 2.0
        /// Default: 1.5
        /// </summary>
        public float ChargeSoundMaxPitch { get; set; } = 1.5f;
    }
}
