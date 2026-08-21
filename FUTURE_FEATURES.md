# Precision Knapping - Future Features and Known Gaps

Status after the v1.4.3 bug-fix round (August 21, 2026).

## Known Incompatibilities

### Item Rarity (ModDB report, unconfirmed fix)
Item Rarity calculates its durability bonus after the tool is crafted. Precision Knapping
writes its durability at crafting time, so the two stages overwrite each other: an epic
rarity tool can start "damaged" (for example 160/300) instead of getting both bonuses.
Needs the Item Rarity source to see which method it patches and in which order. Possible
fix on our side: detect Item Rarity and re-apply our ratio after its stage, or expose the
ratio as a documented attribute other mods can read. Not started.

## Core Logic Bugs - all fixed in v1.4.3

- Stale mistake resurrection (position-keyed counts leaked into replacement surfaces) - fixed
- Multiplayer completion race (global re-entry guard) - fixed
- Tool head detection missed durable parts without "head"/"blade" in the code - improved
  (durable ingredients now carry the ratio; the name heuristic stays as fallback for
  zero-durability heads, where recipe scanning cannot distinguish a head from a stick)
- Quantity bonus lost to rounding (per-player fractional carry) - fixed
- Different-quality heads stacking into one pile (ModDB report) - fixed via StackMergePatch

## Feature Candidates

### Mistake HUD
Show remaining mistakes on screen (action bar or HUD element). Feedback is chat-only, and
`ShowMessages: false` leaves no feedback except sounds.

### Config reload command
Add `/precisionknapping reload` (server-side, permission-gated). Config changes need a
game restart now.

### Fracture zone preview
The Learn Mode overlay colors risky voxels, but does not show the fracture cone a click
will trigger. Preview the actual zone before removal.

### Charged Strikes redesign
Removed in v1.4.0 because the design did not fit the 1.22 knapping code. Re-design for
the current patch approach if wanted.

### Separate sound toggle
Sounds are always on. Add `EnableSounds` config so players can silence sounds but keep
messages, or the reverse.

### Data-driven tool head detection
Replace the name heuristic fully. Plain recipe scanning is ambiguous (a stick and a head
both appear as ingredients of durable-output recipes). Needs a smarter signal, for example
ingredient count per recipe plus output tool type.
