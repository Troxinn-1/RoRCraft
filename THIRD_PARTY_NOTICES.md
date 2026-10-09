# Third-party notices

## SkyCraft

The Minecraft bridge and protocol implementation are derived from SkyCraft by
chasmlol: https://github.com/chasmlol/SkyCraft

Copyright (c) 2026 chasmlol. MIT License. The full upstream notice is preserved in
`outputs/RoRCraft-SkyCraft/THIRD-PARTY-LICENSE.txt` and the Fabric `LICENSE` file.
Upstream naming (`skycraft`, Skyrim-related class names) remains in portions of
the implementation for compatibility and attribution.

## Build/runtime dependencies

- Gradle wrapper: Gradle project, Apache License 2.0; https://github.com/gradle/gradle
- Fabric Loader/API and Loom: obtained through their Maven dependencies; retain
  their own licenses. https://fabricmc.net/
- Harmony/BepInEx and RiskOfOptions are external native runtime dependencies.
  RiskOfOptions uses LGPL-3.0; it is not vendored in this public source snapshot.
- Unity and both games are separately installed proprietary dependencies.

Game textures, audio (including Pigstep), fonts, client libraries, native game
assemblies and player account/skin files are not licensed by this repository's MIT
license and are not included. Generated binaries embedding game assets stay local.

Universal Modder was consulted as reference material:
https://github.com/rehan-remade/universal-modder . This does not imply affiliation.
