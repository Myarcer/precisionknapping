# Precision Knapping

A Vintage Story mod that adds realistic fracture mechanics to stone knapping. Make mistakes while knapping and face consequences - from reduced tool durability to completely shattered stones.

## Features

- **Mistake Tracking**: Accidentally remove protected voxels and accumulate mistakes
- **Realistic Fracture System**: Clicking interior voxels causes cone-shaped fracture patterns based on real flint knapping physics (Hertzian cone)
- **Durability Penalties**: In Advanced Mode, mistakes reduce the durability of crafted tool heads
- **Edge Enforcement**: Advanced Mode requires working from edges inward, like real knapping
- **Charged Strikes** (optional): Hold to pull the stone back, release to strike. Light strikes are precise, full power strikes knock off whole flakes

## Installation

Requires Vintage Story 1.22.0 or later. Version 1.4.1 was verified against game build 1.22.6.

1. Download the latest release ZIP file
2. Place it in your `%APPDATA%\VintagestoryData\Mods\` folder
3. Restart Vintage Story

## Game Modes

### Default Mode (`AdvancedMode: false`)
- Simple mistake counting
- Exceed `MistakeAllowance` ÔåÆ stone breaks
- No durability penalty (all-or-nothing)
- Good for casual play

### Advanced Mode (`AdvancedMode: true`)
- Must work from edges inward
- Clicking interior voxels causes fracture cone toward nearest edge
- Each mistake reduces final tool durability
- More realistic and challenging

### Charged Strikes (`ChargedStrikes: true`)
Works with both modes.

1. **Hold** left click on the stone. Your arm pulls back and holds there.
2. **Aim** at the voxel you want while holding.
3. **Release** to strike.

| Hold time | Result |
|-----------|--------|
| Less than `MinChargeTimeMs` | Tap. Nothing happens. |
| Up to `FullChargeTimeMs` | Controlled strike: one voxel, like a normal click. In Advanced Mode, a lighter hit makes a smaller fracture. |
| `FullChargeTimeMs` or longer | Power strike: knocks off a flake around the voxel (`PowerStrikeRadius`). Protected voxels in the flake count as mistakes. In Advanced Mode, an interior power strike makes a wider fracture. |

You hear a soft tick when the strike is ready and a higher tick when it is fully charged.
Use power strikes on waste far from the tool shape, and controlled strikes near it.

---

## Configuration

Config file location: `VintagestoryData/ModConfig/precisionknapping.json`

**Delete the config file to regenerate with new defaults after mod updates.**

### Core Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `AdvancedMode` | bool | `false` | Enable edge enforcement and durability scaling |
| `MistakeAllowance` | int | `1` | Protected voxels you can break before stone shatters |

#### MistakeAllowance Examples
- `1` = Strict - first mistake breaks stone (vanilla-like)
- `3` = Forgiving - allows some mistakes with durability penalty
- `5` = Casual - very forgiving, good for learning

### Charged Strikes Settings

The server's values for the first four settings are sent to every player, so a client's own
config can't disagree with the server. The last four are personal client settings.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ChargedStrikes` | bool | `false` | Hold to charge, release to strike |
| `MinChargeTimeMs` | int | `350` | Shorter holds are taps that do nothing |
| `FullChargeTimeMs` | int | `1200` | Hold time for a power strike |
| `PowerStrikeRadius` | int | `1` | Power strike flake size: 0 = one voxel, 1 = plus shape (5 voxels), 2 = diamond (13 voxels), max 3 |
| `EnableChargeSounds` | bool | `true` | Charge cues and the heavier power strike sound |
| `EnableStrikeCameraShake` | bool | `true` | Small camera shake on strong strikes |
| `ChargeAnimation` | string | `"aim"` | Player animation held while charging (the game's throw wind-up) |
| `StrikeAnimation` | string | `"knap"` | Player animation played once on release |

### Fracture Physics (Advanced Mode Only)

These settings control how fractures spread when clicking interior (non-edge) voxels.

```
Impact Point (where you clicked)
     |
     v
    /|\     <- Narrow at impact
   / | \      (deterministic)
  /  |  \   
 /   |   \  <- Widening cone
/    |    \   (probabilistic edges)
-----+-----
  Nearest Edge
```

| Setting | Type | Default | Range | Description |
|---------|------|---------|-------|-------------|
| `FractureConeAngle` | int | `90` | 0-180 | Spread angle in degrees |
| `FractureSpreadRate` | float | `0.4` | 0-1 | How fast cone widens with distance |
| `FractureDecay` | float | `0.15` | 0-0.5 | Randomness increase per voxel distance |
| `FractureBaseProbability` | float | `0.85` | 0-1 | Base chance for voxels in cone |

#### FractureConeAngle
How wide the fracture spreads from the impact point.
- `0` = No spread, only direct line to edge
- `60` = Narrow cone (precise, lower risk)
- `90` = Balanced (default)
- `120` = Wide cone (dangerous, high risk)

Real flint has ~100-136┬░ cone angle, but gameplay values are tuned for fun.

#### FractureSpreadRate
How quickly the cone widens as distance from impact increases.
- `0` = Constant width (like a laser)
- `0.4` = Moderate spread (default)
- `1.0` = Aggressive spread (wide destruction)

#### FractureDecay
Fractures become less predictable further from the impact point.
- `0` = Fully deterministic (same result every time)
- `0.15` = Slight randomness at edges (default)
- `0.3` = Chaotic edges (unpredictable)

This simulates material imperfections in real stone.

#### FractureBaseProbability
Base chance for each voxel within the cone to break.
- `1.0` = Everything in cone breaks (100%)
- `0.85` = 85% chance per voxel (default)
- `0.5` = Only half the cone breaks

---

## Durability Scaling (Advanced Mode)

When mistakes are made in Advanced Mode, tool head durability is reduced:

| Mistakes | Durability (Allowance=3) |
|----------|-------------------------|
| 0 | 100% |
| 1 | 75% |
| 2 | 50% |
| 3 | 25% |
| 4+ | Stone breaks |

The scaling adjusts based on `MistakeAllowance`:
- Low allowance (1-2): Larger penalty per mistake, minimum 40-50%
- Medium allowance (3-5): Moderate penalty, minimum 20-25%
- High allowance (6+): Small penalty per mistake, minimum 10%

---

## Example Configurations

### Vanilla-Like (Strict)
```json
{
  "AdvancedMode": false,
  "MistakeAllowance": 1
}
```

### Realistic Challenge
```json
{
  "AdvancedMode": true,
  "MistakeAllowance": 3,
  "FractureConeAngle": 90,
  "FractureSpreadRate": 0.4,
  "FractureDecay": 0.15,
  "FractureBaseProbability": 0.85
}
```

### Casual/Learning
```json
{
  "AdvancedMode": true,
  "MistakeAllowance": 5,
  "FractureConeAngle": 60,
  "FractureSpreadRate": 0.2,
  "FractureDecay": 0.1,
  "FractureBaseProbability": 0.7
}
```

### Hardcore
```json
{
  "AdvancedMode": true,
  "MistakeAllowance": 2,
  "FractureConeAngle": 120,
  "FractureSpreadRate": 0.6,
  "FractureDecay": 0.2,
  "FractureBaseProbability": 0.95
}
```

---

## Technical Notes

- Fracture mechanics only apply when clicking **interior voxels** (non-edge)
- Edge voxels work normally (single voxel removal)
- "Virtual edges" are recognized around recipe pattern holes
- Durability transfer works for tool heads ÔåÆ finished tools via crafting grid
- Mistakes are saved with the knapping surface, so leaving and coming back keeps the count
- Charged Strikes needs the mod on both the server and every client

## License

MIT License - see [LICENSE](LICENSE) for details.

## Source Code

[GitHub Repository](https://github.com/Myarcer/precisionknapping)

## Credits

Fracture physics inspired by real conchoidal fracture mechanics and Hertzian cone theory used in archaeological lithic analysis.

---

## Dev Reference — Shared VS Source

**MANDATORY when inspecting VS API, finding method signatures, or studying game internals.**
Decompiled VS source, game DLLs, and third-party mod source are in the shared references folder — **do not search for VS API source elsewhere, it is already here**:

```
Y:\ClaudeWINDOWS\projects\vintage_story\references\
├── 1.21\   ← VS 1.21.6 DLLs + decompiled\ source + mod sources
└── 1.22\   ← VS 1.22.6 DLLs + vsapi/ vssurvivalmod/ vsessentialsmod/ source clones (1.22.5)
```

See `references\README.md` for full index.