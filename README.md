# RoRCraft — Minecraft × Risk of Rain 2

An experimental Windows singleplayer mashup: Minecraft movement, inventory, tools,
building and character skins inside Risk of Rain 2 stages, with RoR2 enemies,
items, survivor abilities and run progression.

**Development prototype, version 0.7.1-polish.37.** This repository contains source
and development documentation. There is no public, self-contained installer yet.
The current local setup uses RoR2 as the visible game and a hidden Minecraft Java
process for simulation. It is not a standalone game executable.

## Current gameplay

- Minecraft inventory, mining, recipes, resource patches and building.
- A shared health pool, shields, survivor abilities and native chest purchases.
- Your selected Minecraft skin in the lobby, inventory and F5 third-person views.
- Minecraft mobs alongside native RoR2 enemies.
- Cross-game combat, money/experience rewards and a first set of interacting item
  effects: Bleed, Chronobauble, Gasoline, Will-o'-the-wisp, Monster Tooth and Crowbar.
- Minecraft-style HUD/settings, a lobby dance and an authored block shader.

The .37 repair pass fixes runtime skin texture resolution and adds mining cracks.
Direct native enemy hits against the linked player use a 0.65 baseline multiplier
and Minecraft armor/toughness protection, followed by RoR2's native damage pipeline.
Balance across full runs is still being tuned.

## Controls

| Key | Action |
| --- | --- |
| E | Minecraft inventory |
| F5 | Cycle first-person and third-person views |
| Left mouse | Attack or mine using the selected item |
| Right mouse | Use item, place blocks or raise the equipped shield |

Survivor skills and equipment also use the bridge's configured bindings. Public
installation and complete binding documentation are still being prepared.

## Source layout

- `outputs/TechnicalPolish/src`: active native rendering, movement, skin, lobby,
  settings, gameplay patches and developer probes.
- `outputs/TechnicalPolish/fabric`: Minecraft 26.3 Fabric bridge and Java tests.
- `outputs/TechnicalPolish/ShaderProject`: authored Unity shader and bundle builder.
- `outputs/RoRCraft-SkyCraft/src`: earlier native host source. See the important
  host compatibility limitation in [BUILDING.md](docs/BUILDING.md).
- `docs`: portable project state, architecture, roadmap and known limitations.

## Development

The validated local stack uses RoR2 1.5.0#1210, Minecraft Java 26.3, Fabric Loader
0.19.5, Java 25 and a Unity 2021.3 shader build. Start with
[build prerequisites](docs/BUILDING.md) and [current state](docs/CURRENT_STATE.md).

The .37 local regression run passed 22 targeted checks plus 42 Java tests. It
covered skin export, inventory rendering, mining completion and basic native
damage/armor interactions. These results are not a claim that all survivors,
difficulties, long runs or multiplayer are finished.

## Assets and attribution

Both games must be owned separately. Game files, extracted textures/fonts/music,
downloaded player skins, account files, world saves and installation bundles are
excluded from this repository. Asset preparation scripts operate on the user's
own local game files. A distributable installer must perform that preparation
locally before a public binary release can be provided.

The Minecraft bridge is derived from [SkyCraft](https://github.com/chasmlol/SkyCraft)
by chasmlol under the MIT license. [Universal Modder](https://github.com/rehan-remade/universal-modder)
was used as reference material. See [third-party notices](THIRD_PARTY_NOTICES.md).
This is an unofficial community project.
