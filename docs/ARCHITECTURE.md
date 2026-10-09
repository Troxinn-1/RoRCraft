# Architecture

RoR2 owns the visible Unity game, native stages/enemies, survivor combat, item
inventory and run progression. A hidden Minecraft Java/Fabric process owns MC
physics, inventory, building, recipes, mobs and character rendering data.

A local shared-memory bridge carries input, player/camera state, exported native
collision, Minecraft meshes/textures, UI frames and combat/reward events. One
movement owner at a time is essential: the native motor must not simultaneously
move the linked Minecraft-controlled body.

The preserved native host 0.7 is patched by active movement/visual modules using
Harmony. Its source compatibility limitation is documented in BUILDING.md.
Minecraft level rendering is replaced by native exported geometry; inventory and
HUD are exported separately. Third-person player meshes use the same runtime skin
texture as inventory previews. The authored Unity shader lights MC block surfaces.

Shared health and armor use sequence-checked snapshots. RoR2 items must act through
real native attackers and proc/damage pipelines; cross-game effects and rewards
require ownership and run/stage deduplication. HUD text must not overlap gameplay
protocol fields.

Protocol details remain in source (`Proto.java`, `SkyMemory.cs`, RoRSync and active
patches). Keep readers and writers synchronized when changing memory layouts.
