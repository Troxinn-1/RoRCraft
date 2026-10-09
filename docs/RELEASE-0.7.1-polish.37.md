# RoRCraft 0.7.1-polish.37

První veřejný development release RoRCraftu — Minecraft × Risk of Rain 2.

## Co je v této verzi

- Minecraft skin je vidět v lobby, inventáři i ve F5 pohledu.
- Opravený inventář po stisknutí E.
- Viditelné praskliny při těžení bloků a dokončení rozbití vanilla krumpáčem.
- Přímé zásahy RoR2 nepřátel mají základní melee redukci a respektují Minecraft
  armor/toughness.
- Štít používá správnější Minecraft měřítko opotřebení.
- Minecraft inventory, stavění, crafting, mobové a první propojené RoR2 item procy.
- Veřejný zdrojový build, shader projekt, testy a lokální instalátor.

## Ověření

- 22 cílených background kontrol prošlo s exit code 0.
- 42 Fabric/JUnit testů prošlo.
- Ověřený build zahrnuje F5 vpředu i vzadu, inventory skin a mining cracks.
- Ověřený běh nepřevzal focus ani vstup uživatele.

## Instalace

Stáhni si zdrojový ZIP z této release stránky a postupuj podle
[docs/INSTALLING.md](https://github.com/Troxinn-1/RoRCraft/blob/main/docs/INSTALLING.md).
Instalátor vyžaduje vlastní lokální instalaci RoR2, BepInEx, Minecraft Java a
Prism/Fabric. Použij `tools/Verify-RoRCraftPackage.ps1` před instalací.

Tento release neobsahuje herní DLL/JAR, Minecraft textury, hudbu, účty ani světy.
Tyto soubory musí zůstat u hráče kvůli licencím a jsou připravené lokálně při buildu.

## Stav projektu

Je to vývojová verze pro singleplayer. Dlouhé runy, kompletní balance, multiplayer,
Wither boss, summon pes a externí shader packy jsou další práce.
