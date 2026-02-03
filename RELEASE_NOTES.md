## Precision Knapping

Adds precision mechanics to the knapping system. Hit the wrong spot and face consequences.

### What It Does
When knapping tools, clicking on any part of the final tool shape (the actual arrowhead, knife, or spearhead pattern) counts as a mistake. You must carefully chip away only the excess stone around the pattern.

This turns knapping from a simple "follow the pattern" task into a precision exercise that punishes careless clicks.

### Features
- **Two Game Modes**: Default mode (simple mistake counting) and Advanced mode (edge enforcement + durability scaling)
- **Realistic Fracture Physics**: Advanced mode features cone-shaped fracture patterns based on real flint knapping mechanics (Hertzian cone)
- **Configurable Difficulty**: Adjust mistake allowance from strict (1 mistake = break) to casual (5+ mistakes allowed)
- **Durability Penalties**: In Advanced Mode, mistakes reduce the durability of crafted tool heads
- **Visual and Audio Feedback**: Know immediately when you break the stone
- **Chat Notifications**: Messages explaining what went wrong
- **Works with all knapping recipes** automatically, including modded recipes

### Configuration
Config file: `VintagestoryData/ModConfig/precisionknapping.json`

See the [README](https://github.com/Myarcer/precisionknapping#configuration) for detailed configuration options including:
- `AdvancedMode` - Toggle between simple and advanced mechanics
- `MistakeAllowance` - How many mistakes before stone breaks
- Fracture physics tuning (cone angle, spread rate, decay)

### Installation
**1-Click Install** on [ModDB](https://mods.vintagestory.at/) *(coming soon)*

OR Manual:
1. Download the ZIP from this release
2. Place in your `%APPDATA%\VintagestoryData\Mods\` folder
3. Restart Vintage Story or click "Reload Mods"

### Compatibility
- Tested on **1.21.5-1.21.6**, likely backwards compatible with 1.21.0+
- Works with modded knapping recipes
- Server-side mod (clients don't need it)

### License
MIT License - see [LICENSE](https://github.com/Myarcer/precisionknapping/blob/main/LICENSE)
