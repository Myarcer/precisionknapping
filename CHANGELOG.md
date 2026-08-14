# Precision Knapping - Changelog

## v1.4.1 (August 14, 2026)

Release version of the 1.22 update. Version 1.4.0 was a build for test only.

In-game test on Vintage Story 1.22.6, client and server: the mod loads with no error, all three
Harmony patches apply, and a full knapping session completed correctly. The log shows the completion
patch healing the voxel grid and scaling the output for 1 mistake and for 4 mistakes.

---

## v1.4.0 (August 14, 2026)

### Vintage Story 1.22 support

- Builds against .NET 10 and Harmony 2.4.2, the runtime and library that VS 1.22 uses.
- All patch targets were verified against the 1.22.6 game assemblies: `BlockEntityKnappingSurface.OnUseOver`,
  `BlockEntityKnappingSurface.CheckIfFinished`, `CollectibleObject.OnCreatedByCrafting`, and the reflected
  fields `Voxels`, `SelectedRecipe`, `Output` and `ResolvedItemstack`. No signature changed.
- Removed a dead asset patch (`assets/game/patches/knappingsurface-entity.json`). It set a block entity class
  that the mod never registered, and its target path did not exist in the game. The game logged a patch failure
  for it at every start.
- The mod icon is now in the release package. Earlier packages had no icon, so the mod manager showed none.
- The minimum game version is 1.22.0.

### Changes from v1.4.0-rc.1

- Removed the Charged Strikes mode. Its design does not fit the 1.22 knapping code.
- The completion patch heals the voxel grid and lets the game finish the recipe. This keeps the tutorial flow
  and all completion events.
- A re-entry guard stops a double completion bonus.
- The crafting patch uses the 1.22 `IRecipeBase` signature and reads parameters by position.
- Each Harmony patch is applied on its own. One failure does not stop the others.

---

## v1.3.0 (February 3, 2026)

### Charged Strikes Mode ÔÜí

**Hold-to-Charge, Release-to-Strike Mechanics**
- Enable with `ChargedStrikes: true` in config
- Hold left-click to charge, release to strike
- Audio feedback: tick sounds increase in speed as you charge
- Improved charge timing: 500ms minimum, 1.5s for full charge

**Config:**
```json
{
  "ChargedStrikes": false,     // Enable hold-to-charge mechanics
  "MinChargeTimeMs": 500,      // Minimum hold time (0.5s)
  "FullChargeTimeMs": 1500,    // Full charge time (1.5s)  
  "EnableChargeSounds": true   // Audio feedback during charge
}
```

> ÔÜá´©Å **Note**: Charge indicator overlay is not functional yet - coming in a future update.

---

## v1.2.1 (January 1, 2025)

### Feature: Uncoupled Durability Scaling

**New Config Option: `EnableDurabilityScaling`**
- Durability bonuses/penalties now work in BOTH Default and Advanced modes
- Previously, durability scaling was locked behind Advanced Mode
- New toggle allows players to get precision rewards without the hardcore fracture mechanics

**Config:**
```json
{
  "EnableDurabilityScaling": true  // true = bonuses/penalties active, false = always vanilla 100%
}
```

**What this means:**
- Default Mode + Scaling ON: Mistakes count toward breaking, perfect knapping gives bonus
- Default Mode + Scaling OFF: Vanilla behavior (no bonuses or penalties)
- Advanced Mode + Scaling ON: Full mechanics (fractures + durability curve)
- Advanced Mode + Scaling OFF: Fracture mechanics only, no quality impact

---

## v1.2.0 (December 28, 2024)

### New Feature: Precision Bonuses ­ƒÄ»

**Graduated Durability/Quantity System**
- Perfect knapping (0 mistakes) now gives **+25% bonus** durability!
- Smart curve: 0 mistakes = max bonus, ~40% of allowance = vanilla, max mistakes = minimum
- Works for both tool heads AND stackables (arrowheads, fishing hooks)

**Examples (with default 25% bonus):**
| Mistakes | Allowance 2 | Allowance 5 | Allowance 10 |
|----------|-------------|-------------|--------------|
| 0 | **125%** | **125%** | **125%** |
| 1 | 100% | 115% | 119% |
| 2 | 50% | 100% | 112% |
| 3 | - | 75% | 106% |
| 4 | - | 50% | **100%** |
| max | - | 25% | 10% |

**Stackable Bonus:**
- Quantity uses same multiplier with rounding
- 4 items ├ù 125% = 5 items, 4 items ├ù 113% = 5 items (rounds up)

### Config Addition
```json
{
  "PerfectKnappingBonus": 0.25  // +25% for 0 mistakes, set to 0 to disable
}
```

### Bug Fixes
- Fixed crafting transfer not applying bonus durability > 100%
- Fixed stackables using old penalty logic instead of graduated curve

---

## v1.1.0 (December 18, 2024)

### Major Features

**Config System**
- Added `modconfig/precisionknapping.json` with configurable settings
- Auto-generates default config on first load

**Mistake Tolerance (Both Modes)**
- `MistakeAllowance` setting (default: 1) - number of protected voxels you can accidentally hit before stone breaks
- Default mode: Mistakes count toward breaking only (no durability penalty)
  - Get 100% quality item or nothing (all-or-nothing)
  - Messages: "Mistake! X chance(s) remaining" and "Final warning!"
- Advanced mode: Mistakes reduce final item durability
  - 0 mistakes = 100%, 1 = 75%, 2 = 50%, 3 = 25%, 4+ = stone breaks
  - Messages: "Mistake! X remaining (Y% durability)"

**Advanced Mode** (`AdvancedMode: false` by default)
- Edge enforcement: Must click from outer edges inward
- Virtual edge detection: Recipe pattern holes count as edges (arrowheads work correctly)
- Non-edge click gambling: Clicking interior voxels removes a line to nearest edge
  - Risky! If protected voxels are in the path, they count as mistakes
- Durability scaling based on mistakes made during crafting

**Fracture Physics** (Advanced Mode)
- Configurable cone-shaped fracture patterns based on real flint knapping mechanics
- Settings: `FractureConeAngle`, `FractureSpreadRate`, `FractureDecay`, `FractureBaseProbability`

### Technical Changes
- Refactored into modular patch classes for maintainability
- `AdvancedKnappingHelper` class with BFS pathfinding for line-break mechanics
- Edge detection algorithm with 4-directional adjacency checking
- Mistake tracking via BlockEntity attributes
- Tool head detection for future durability/quantity penalty differentiation

### Config Reference
```json
{
  "AdvancedMode": false,           // Edge enforcement + durability scaling
  "MistakeAllowance": 1,           // Mistakes before stone breaks
  "FractureConeAngle": 90,         // Degrees (0-180)
  "FractureSpreadRate": 0.4,       // How fast cone widens (0-1)
  "FractureDecay": 0.15,           // Randomness increase per distance (0-0.5)
  "FractureBaseProbability": 0.85  // Base chance per voxel in cone (0-1)
}
```

---

## v1.0.0 (Initial Release)

### Features
- Protected voxel detection: Clicking tool pattern breaks stone
- Works with all knapping recipes automatically
- Visual and audio feedback on failure
- Server-side only (clients don't need mod)