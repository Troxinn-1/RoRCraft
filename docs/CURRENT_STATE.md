# Current state — 2026-10-09

Local installed version: **0.7.1-polish.37**.

The latest pass fixes invisible F5 skin meshes and inventory previews by using the
registered runtime texture path explicitly. Mining crack geometry now travels
through the native scene exporter, with vanilla multiplicative textures converted
to black-alpha blending. A completed wooden-pickaxe mining test removed its block.

Direct native enemy hits use a 0.65 survival multiplier and the player's real MC
armor/toughness snapshot before native damage processing. Four native hits of 20
left 58/110 HP. An actual MC iron chestplate changed a native hit from 13 to 10.49455.
DOT, falling, void and bypass damage retain their previous rules. Shield wear is
converted to Minecraft's 20-HP scale. HUD text is capped at its allocated 480-byte
region to avoid overwriting effect-queue and monster-level fields.

Validation: 22 targeted local background checks, 42 JUnit tests, visual inspection
of exported F5 models, inventory and mining cracks. The games exited normally and
foreground ownership remained outside the background test game.

Normal full-run balance and human playfeel still need acceptance. Test mining
reported `grounded=false`; sustained ground-state issues on native terrain need
separate diagnosis. No multiplayer acceptance or public installation acceptance.
