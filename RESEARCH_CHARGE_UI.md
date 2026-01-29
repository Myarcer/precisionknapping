# Research: Charge Indicator UI for Precision Knapping

## Executive Summary

This document researches implementation approaches for a visual charge indicator in Vintage Story, specifically for the knapping minigame mechanic.

---

## Existing Implementation: ChargeSoundManager

**File**: `src/Features/ChargeSoundManager.cs`

We already have audio feedback infrastructure:
```csharp
// Current implementation
- StartChargeSound(BlockPos pos)     // Start looping sound
- UpdatePitch(float chargePercent)   // Pitch scales 0.8 -> 1.5 with charge
- StopChargeSound()                  // Cleanup
- PlaySwooshSound(BlockPos, float)   // Release feedback
```

**Config options already exist** (in PrecisionKnappingConfig.cs):
```csharp
public bool EnableChargeSounds { get; set; } = true;
public float ChargeSoundMinPitch { get; set; } = 0.8f;
public float ChargeSoundMaxPitch { get; set; } = 1.5f;
```

**Gap**: No visual indicator - only audio feedback currently.

---

## VS Rendering Approaches

### Option A: IRenderer Interface (VSBullseye Pattern)

**Source**: VSBullseye's `BullseyeReticleRenderer.cs`

```csharp
public class ChargeIndicatorRenderer : IRenderer
{
    public double RenderOrder => 0.98;  // Near final render pass
    public int RenderRange => 9999;      // Always render

    private LoadedTexture _barTexture;
    private LoadedTexture _markerTexture;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Ortho) return;

        // Screen center calculation (from VSBullseye)
        float screenWidth = capi.Render.FrameWidth;
        float screenHeight = capi.Render.FrameHeight;
        float scale = RuntimeEnv.GUIScale;

        // Position near bottom-center (like a stamina bar)
        float x = (screenWidth / 2) - (barWidth * scale / 2);
        float y = screenHeight - (100 * scale);  // 100px from bottom

        // Render bar background
        capi.Render.Render2DTexture(_barTexture.TextureId, x, y, barWidth * scale, barHeight * scale);

        // Render charge marker
        float markerX = x + (chargeLevel * barWidth * scale);
        capi.Render.Render2DTexture(_markerTexture.TextureId, markerX, y, ...);
    }
}
```

**Pros**:
- Full control over positioning
- Can attach to any screen location
- Efficient (direct OpenGL calls)

**Cons**:
- Must manage texture loading/disposal
- More code complexity

---

### Option B: HudElement Class

**Source**: VS API `Vintagestory.API.Client.HudElement`

```csharp
public class ChargeIndicatorHud : HudElement
{
    private GuiElementStatbar _chargeBar;

    public ChargeIndicatorHud(ICoreClientAPI capi) : base(capi)
    {
        SetupDialog();
    }

    private void SetupDialog()
    {
        ElementBounds barBounds = ElementBounds.Fixed(
            EnumDialogArea.CenterBottom,  // Anchor point
            0, -80,                        // Offset from anchor
            200, 20                        // Size
        );

        SingleComposer = capi.Gui.CreateCompo("chargeIndicator", barBounds)
            .AddStatbar(barBounds, new double[] { 0, 1, 0 }, "chargebar")  // Green
            .Compose();

        _chargeBar = SingleComposer.GetStatbar("chargebar");
    }

    public void UpdateCharge(float level, ChargeZone zone)
    {
        // Update bar value
        _chargeBar.SetValue(level);

        // Change color based on zone
        double[] color = zone switch
        {
            ChargeZone.Under => new[] { 1.0, 0.3, 0.3 },    // Red
            ChargeZone.Sweet => new[] { 0.3, 1.0, 0.3 },    // Green
            ChargeZone.Over => new[] { 1.0, 0.5, 0.0 },     // Orange
            _ => new[] { 1.0, 1.0, 1.0 }
        };
        // Note: May need custom stat bar for dynamic colors
    }
}
```

**Pros**:
- Uses VS's built-in GUI system
- Automatic scaling/positioning
- Built-in stat bar element

**Cons**:
- Less flexible positioning
- May not support dynamic color changes easily

---

### Option C: Cursor-Relative Rendering (RECOMMENDED)

**Approach**: Render charge bar near mouse cursor with offset to the right.

**Why this works well**:
- Player is already looking at cursor position (where they'll strike)
- Follows attention naturally
- Simple to implement
- Works regardless of hand animations

#### Implementation
```csharp
public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
{
    if (stage != EnumRenderStage.Ortho) return;
    if (!IsCharging) return;

    // Get mouse position
    int mouseX = capi.Input.MouseX;
    int mouseY = capi.Input.MouseY;

    // Offset to right of cursor (configurable)
    float offsetX = 30f * RuntimeEnv.GUIScale;
    float offsetY = -10f * RuntimeEnv.GUIScale;  // Slightly above

    float barX = mouseX + offsetX;
    float barY = mouseY + offsetY;

    // Render charge bar at this position
    RenderChargeBar(barX, barY);
}
```

#### Alternative Positions (Future)
- **Hand bone tracking**: More immersive but complex
  ```csharp
  var pose = playerEntity.AnimManager.Animator.GetAttachmentPointPose("RightHand");
  var screenPos = WorldToScreen(pose.Position);
  ```
- **On-stone world-space**: Bar floats above knapping stone
- **Static screen position**: Belt area like ToolBelt mod

**Current choice**: Cursor-relative is simplest and most intuitive for v1.

---

## VSBullseye Implementation Deep Dive

**Key patterns from their reticle renderer**:

### Texture State Management
```csharp
// Multiple textures for different states
LoadedTexture currentAimTexFullCharge;
LoadedTexture currentAimTexPartialCharge;
LoadedTexture currentAimTexBlocked;

// Selection based on state
LoadedTexture texture = clientAimingSystem.WeaponReadiness ==
    BullseyeEnumWeaponReadiness.FullCharge ? currentAimTexFullCharge : ...
```

### Screen Positioning Formula
```csharp
// Center on screen with aim offset
float x = (FrameWidth / 2) - (TextureWidth * scale / 2) + aimOffset.X;
float y = (FrameHeight / 2) - (TextureHeight * scale / 2) + aimOffset.Y;
```

### Render Depth Layering
```csharp
// Primary indicator
capi.Render.Render2DTexture(texture, x, y, w, h, 10000f);
// Secondary overlay (higher depth = renders on top)
capi.Render.Render2DTexture(texture2, x2, y2, w2, h2, 10001f);
```

---

## StatusHUD Pattern (For Reference)

Uses modular element system:
- Each HUD element is independent
- Position configured via JSON
- Conditional visibility based on game state
- Native GUI configuration ([U] key)

**Applicable ideas**:
- Allow players to reposition the charge bar
- Show/hide based on knapping state (only visible when charging)
- Scale option in config

---

## ToolBelt Analysis

**Status**: DLL-only distribution, no public source.

**Observed behavior** (from gameplay):
- Renders hotbar-style slots on screen edge
- Uses GuiDialog-based approach (has config GUI)
- Positions relative to existing HUD elements

**Takeaway**: Standard HudElement approach works for belt-style UI.

---

## Recommended Implementation

### Architecture

```
ChargeIndicatorRenderer (IRenderer)
├── Renders charge bar on screen
├── Multiple texture states (zones)
├── Smooth animation between states
└── Position: configurable (default: near crosshair or bottom-center)

ChargeIndicatorManager (integration)
├── Receives charge updates from ChargeStateTracker
├── Calculates zone (under/sweet/over)
├── Updates renderer state
└── Coordinates with ChargeSoundManager for audio
```

### Visual Design

```
┌────────────────────────────────────────────────────┐
│                                                    │
│   ┌──────┬──────────────────────┬──────────────┐   │
│   │ RED  │       GREEN          │   ORANGE     │   │
│   │UNDER │       SWEET          │    OVER      │   │
│   │ 0-40%│       40-75%         │   75-100%    │   │
│   └──────┴──────────────────────┴──────────────┘   │
│          ▲                                         │
│          │ Marker (current charge)                 │
│                                                    │
└────────────────────────────────────────────────────┘
```

### Zone Definitions (From User Requirements)

| Zone | Charge % | Effect | Visual |
|------|----------|--------|--------|
| Dead | 0-25% | No action (does nothing) | Dark red / Gray |
| Under | 25-40% | Slight fracture bonus | Light red / Yellow |
| Sweet | 40-75% | Minimal fracture | Bright green |
| Over-slight | 75-90% | Slight fracture penalty | Yellow / Orange |
| Over-full | 90-100% | Maximum fracture | Bright red |

### Texture Assets Needed

1. `charge_bar_background.png` - Bar outline/track
2. `charge_bar_zones.png` - Color zones (or generate programmatically)
3. `charge_marker.png` - Current position indicator
4. Optional: `charge_glow.png` - Pulse effect for sweet spot

---

## Implementation Priority

1. **Phase 1**: Basic IRenderer with static texture bar
2. **Phase 2**: Zone-based coloring and marker
3. **Phase 3**: Animation/polish (pulse at sweet spot, shake at extremes)
4. **Phase 4**: Configurable positioning
5. **Phase 5**: Hand-proximity rendering (if feasible)

---

## Config Options to Add

```csharp
// Visual indicator settings
public bool EnableChargeIndicator { get; set; } = true;
public string ChargeIndicatorPosition { get; set; } = "bottom-center"; // or "crosshair", "hand"
public float ChargeIndicatorScale { get; set; } = 1.0f;
public float ChargeIndicatorOpacity { get; set; } = 0.8f;

// Zone boundaries (configurable for tuning)
public float DeadZoneEnd { get; set; } = 0.25f;      // 0-25% = no action
public float SweetSpotStart { get; set; } = 0.40f;   // 40% = sweet spot begins
public float SweetSpotEnd { get; set; } = 0.75f;     // 75% = sweet spot ends
public float OverchargeStart { get; set; } = 0.90f;  // 90% = heavy penalty begins
```

---

## References

- **VSBullseye**: https://github.com/TeacupAngel/VSBullseye
- **StatusHUD**: https://github.com/Gravydigger/statushud
- **VS API Docs**: https://apidocs.vintagestory.at/api/Vintagestory.API.Client.IRenderer.html
- **VS Wiki GUIs**: https://wiki.vintagestory.at/index.php/Modding:GUIs

---

*Research completed: 2026-01-05*
