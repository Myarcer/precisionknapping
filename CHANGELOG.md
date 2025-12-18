# Precision Knapping - Changelog

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

**Advanced Swings** (`AdvancedSwings: false` by default)
- Hold-to-charge mechanic: Must hold left mouse for `ChargeTimeSeconds` (default 0.4s) before knap triggers
- Early release cancels action with feedback sound
- Prevents accidental rapid clicks

### Technical Changes
- Refactored into modular patch classes for maintainability
- `AdvancedKnappingHelper` class with BFS pathfinding for line-break mechanics
- Edge detection algorithm with 4-directional adjacency checking
- Mistake tracking via BlockEntity attributes
- Tool head detection for future durability/quantity penalty differentiation

### Config Reference
```json
{
  "AdvancedMode": false,        // Edge enforcement + durability scaling
  "MistakeAllowance": 1,        // Mistakes before stone breaks
  "AdvancedSwings": false,      // Hold-to-charge timing
  "ChargeTimeSeconds": 0.4      // Seconds to hold for charged swing
}
```

---

## v1.0.0 (Initial Release)

### Features
- Protected voxel detection: Clicking tool pattern breaks stone
- Works with all knapping recipes automatically
- Visual and audio feedback on failure
- Server-side only (clients don't need mod)
