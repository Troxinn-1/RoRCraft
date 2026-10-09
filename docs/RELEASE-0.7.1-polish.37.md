# RoRCraft 0.7.1-polish.37

The first public development release of RoRCraft — Minecraft × Risk of Rain 2.

## What's included

- The Minecraft skin is visible in the lobby, inventory, and F5 views.
- Fixed the inventory character preview after pressing E.
- Visible block-breaking cracks and completed vanilla pickaxe mining.
- Direct RoR2 enemy hits use a melee reduction and respect Minecraft armor/toughness.
- Shield durability uses a more appropriate Minecraft-scale conversion.
- Minecraft inventory, building, crafting, mobs, and the first connected RoR2 item procs.
- Public source, shader project, tests, and a local installer.

## Validation

- 22 targeted background checks passed with exit code 0.
- 42 Fabric/JUnit tests passed.
- The validated build covers front and back F5 views, the inventory skin, and mining cracks.
- The background run did not take focus or user input.

## Installation

Download the source ZIP from this release page and follow
[docs/INSTALLING.md](https://github.com/Troxinn-1/RoRCraft/blob/main/docs/INSTALLING.md).
The installer requires your own local installations of RoR2, BepInEx, Minecraft
Java, and Prism/Fabric. Run `tools/Verify-RoRCraftPackage.ps1` before installation.

This release does not include game DLLs/JARs, Minecraft textures, music, accounts,
or worlds. These files remain with the player for licensing reasons and are prepared
locally during the build.

## Project status

This is a singleplayer development release. Long-run balance, multiplayer, a Wither
boss, a dog summon, and external shader packs remain future work.
