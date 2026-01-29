# Feature: Charge-Based Knapping Minigame

## Overview
Transform knapping from click-spam into a skill-based timing minigame where charge level determines fracture control.

**Status**: Design Finalized, Ready for Implementation

---

## Core Mechanic (FINALIZED)

### Charge Zones & Effects

```
Charge:    0%═══[25%]═══[40%]════════[75%]═══[90%]═══[100%]
Zone:      │ DEAD │UNDER│    SWEET SPOT    │OVER-S│OVER-FULL│
Effect:    │NOTHING│mild │    MINIMAL      │ mild │ MAXIMUM │
           │      │fract│    fracture     │fract │fracture │
Color:     │ GRAY │YELLOW│     GREEN       │ORANGE│   RED   │
```

### Zone Definitions

| Zone | Range | Behavior |
|------|-------|----------|
| **Dead Zone** | 0-25% | **NO ACTION** - click does nothing, no voxel removed |
| **Undercharge** | 25-40% | Voxel removed with mild fracture spread |
| **Sweet Spot** | 40-75% | Minimal/no fracture - controlled removal |
| **Overcharge (slight)** | 75-90% | Mild fracture spread |
| **Overcharge (full)** | 90-100% | Maximum fracture spread (worst outcome) |

### Key Design Decisions (Per User)

1. **Medium sweet spot** (35% window: 40-75%)
2. **Overcharge caps** - stays at 100%, doesn't oscillate or auto-fire
3. **Undercharge = NO ACTION** - forces player to learn timing
4. **Both edge AND interior hits** get fracture scaling

---

## Fracture Multipliers

```csharp
// Zone-based fracture multipliers
public float GetFractureMultiplier(float chargeLevel)
{
    if (chargeLevel < 0.25f)      return 0f;     // Dead - no action
    if (chargeLevel < 0.40f)      return 1.3f;   // Under - 30% larger fracture
    if (chargeLevel <= 0.75f)     return 0.5f;   // Sweet - 50% smaller fracture
    if (chargeLevel < 0.90f)      return 1.3f;   // Over-slight - 30% larger
    return 1.8f;                                  // Over-full - 80% larger (worst)
}
```

### Application to Different Hit Types

**Edge Hits** (currently single-voxel in vanilla):
```
Dead Zone:    No removal
Sweet Spot:   1 voxel removed (vanilla behavior)
Under/Over:   1-3 voxel splash toward exterior
Full Over:    2-4 voxel splash
```

**Interior Hits** (fracture cone):
```
Dead Zone:    No removal
Sweet Spot:   Narrow cone (50% width)
Under/Over:   Standard cone (100% width)
Full Over:    Wide cone (180% width)
```

---

## Visual Indicator Design

### Recommended Approach: Cursor-Relative via IRenderer

Based on research (see `RESEARCH_CHARGE_UI.md`), implementing via `IRenderer` interface with **cursor-relative positioning**.

**Why cursor-relative**:
- Player is already looking at cursor (where they'll strike)
- Follows attention naturally
- Simple to implement
- Works regardless of hand animations

### Visual Layout

```
                    Screen
    ┌─────────────────────────────────────────┐
    │                                         │
    │              ╳ ┌──────────────┐         │
    │              │ │▓▓▓▓░░░░░░░░░│         │  <- Bar follows cursor
    │              │ └──────────────┘         │
    │              └─ Cursor                  │
    │                                         │
    └─────────────────────────────────────────┘

    Bar detail:
    ┌────┬─────┬─────────────────┬─────┬─────┐
    │GRAY│YELLO│      GREEN      │ORANG│ RED │
    │    │  W  │                 │  E  │     │
    │0-25│25-40│     40-75       │75-90│90+  │
    └────┴─────┴─────────────────┴─────┴─────┘
         ▲
         █ <- Current charge marker (moves right as you hold)
```

### Color Scheme

```csharp
// RGBA colors for each zone
var zoneColors = new Dictionary<ChargeZone, Vec4f>
{
    [ChargeZone.Dead] = new Vec4f(0.3f, 0.3f, 0.3f, 0.6f),     // Gray (dimmed)
    [ChargeZone.Under] = new Vec4f(1.0f, 0.8f, 0.2f, 0.9f),    // Yellow
    [ChargeZone.Sweet] = new Vec4f(0.2f, 1.0f, 0.3f, 1.0f),    // Bright Green
    [ChargeZone.OverSlight] = new Vec4f(1.0f, 0.5f, 0.0f, 0.9f), // Orange
    [ChargeZone.OverFull] = new Vec4f(1.0f, 0.2f, 0.2f, 1.0f)  // Red
};
```

### Animation Ideas

1. **Marker pulse** when entering sweet spot (subtle scale animation)
2. **Bar shake** when in overcharge zone
3. **Glow effect** on sweet spot section
4. **Fade in/out** - bar appears when charging begins, fades when released

---

## Audio Integration

### Existing Infrastructure (ChargeSoundManager.cs)

Already have:
- `StartChargeSound()` - looping draw sound
- `UpdatePitch(float)` - pitch scales with charge
- `PlaySwooshSound()` - release feedback

### Additions Needed

```csharp
// Zone-specific audio cues
public void PlayZoneEnterSound(ChargeZone zone)
{
    switch (zone)
    {
        case ChargeZone.Sweet:
            // Subtle positive "ding" or chime
            PlaySound("game:sounds/effect/latch");
            break;
        case ChargeZone.OverFull:
            // Warning sound
            PlaySound("game:sounds/effect/woodcreak");
            break;
    }
}

// Pitch mapping to reinforce zones
public float GetPitchForZone(ChargeZone zone, float chargeLevel)
{
    return zone switch
    {
        ChargeZone.Dead => 0.6f,           // Low, discouraging
        ChargeZone.Under => 0.8f + (chargeLevel * 0.5f),  // Rising
        ChargeZone.Sweet => 1.2f,          // Stable, pleasant
        ChargeZone.OverSlight => 1.4f,     // Higher, tense
        ChargeZone.OverFull => 1.6f + Random(0.1f)  // High, unstable
    };
}
```

---

## Configuration

### New Config Options

```csharp
// === CHARGE MINIGAME ===

/// <summary>
/// Enable charge-based timing mechanic. Requires RealisticStrikes = true.
/// </summary>
public bool EnableChargeMinigame { get; set; } = true;

/// <summary>
/// Zone boundaries (0.0 - 1.0)
/// </summary>
public float DeadZoneEnd { get; set; } = 0.25f;
public float SweetSpotStart { get; set; } = 0.40f;
public float SweetSpotEnd { get; set; } = 0.75f;
public float FullOverchargeStart { get; set; } = 0.90f;

/// <summary>
/// Fracture multipliers per zone
/// </summary>
public float UnderchargeFractureMultiplier { get; set; } = 1.3f;
public float SweetSpotFractureMultiplier { get; set; } = 0.5f;
public float OverchargeFractureMultiplier { get; set; } = 1.3f;
public float FullOverchargeFractureMultiplier { get; set; } = 1.8f;

// === VISUAL INDICATOR ===

public bool EnableChargeIndicator { get; set; } = true;
public string ChargeIndicatorPosition { get; set; } = "cursor";  // "cursor", "hand", "bottom-center"
public float ChargeIndicatorOffsetX { get; set; } = 30f;  // Pixels right of cursor
public float ChargeIndicatorOffsetY { get; set; } = -10f; // Pixels above cursor (negative = up)
public float ChargeIndicatorScale { get; set; } = 1.0f;
public float ChargeIndicatorOpacity { get; set; } = 0.85f;
```

---

## Implementation Plan

### Phase 1: Core Mechanics
- [ ] Add zone enum and calculation
- [ ] Modify `ChargeReleasePacket` to include zone
- [ ] Implement "dead zone = no action" logic
- [ ] Apply fracture multipliers to existing FractureCalculator

### Phase 2: Edge Hit Splash
- [ ] Implement edge splash calculation for non-sweet-spot hits
- [ ] Respect protected voxels in splash zone
- [ ] Sound/message feedback for edge splash

### Phase 3: Visual Indicator
- [ ] Create `ChargeIndicatorRenderer : IRenderer`
- [ ] Load/render zone textures
- [ ] Animate charge marker
- [ ] Position configuration

### Phase 4: Audio Enhancement
- [ ] Zone-enter sounds
- [ ] Pitch mapping per zone
- [ ] Sweet spot "success" audio cue

### Phase 5: Polish
- [ ] Bar animations (pulse, shake)
- [ ] Tutorial/first-time hints
- [ ] Config GUI integration

---

## Testing Checklist

- [ ] Dead zone correctly prevents all voxel removal
- [ ] Sweet spot gives minimal/no fracture
- [ ] Full overcharge gives maximum fracture
- [ ] Edge hits now have splash in non-sweet zones
- [ ] Protected voxel detection works with splash
- [ ] Visual bar appears only when charging
- [ ] Audio cues play at zone transitions
- [ ] Multiplayer: indicator is client-only, mechanics server-authoritative

---

## Open Questions (RESOLVED)

| Question | Resolution |
|----------|------------|
| Sweet spot size? | Medium (35% window: 40-75%) |
| Overcharge behavior? | Caps at 100% |
| Undercharge behavior? | **Does nothing** - no voxel removed |
| Edge vs interior scaling? | Both get fracture scaling |
| Visual style? | IRenderer bar with zone colors |
| Bar position? | **Cursor-relative** - follows mouse with offset to right |

---

## References

- `RESEARCH_CHARGE_UI.md` - Technical research on VS rendering
- `src/Features/ChargeSoundManager.cs` - Existing audio infrastructure
- `src/Features/FractureCalculator.cs` - Current fracture logic
- VSBullseye mod - Reference implementation for IRenderer

---

*Design finalized: 2026-01-05*
*Status: Ready for implementation*
