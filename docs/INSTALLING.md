# Local installation

The public repository is source-only. It does not redistribute Risk of Rain 2,
Minecraft, BepInEx, Prism, player skins, account data, extracted Minecraft audio,
or a prebuilt DLL/JAR bundle. Both games must be owned and installed locally.

The working `.37` package can be installed with `tools/Install-RoRCraft.ps1` once
you have built or received a local package directory containing `manifest.json`,
`Runtime/`, and `Minecraft/RoRCraft.jar`:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.<tools/Install-RoRCraft.ps1 -PackagePath 'C:\RoRCraft\package' `
  -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2' `
  -MinecraftInstance 'C:\Games\Prism\instances\SkyCraft'
```

The installer validates every manifest hash, creates a timestamped backup beside
the package, copies the native modules into `BepInEx/plugins/RoRCraft`, and copies
the bridge jar into the selected instance's `.minecraft/mods`. It never deletes an
old installation. Use `-BackupOnly` to create only the backup.

Before a normal launch, close both games, run the installer, then launch RoR2 with
BepInEx and the matching Prism instance. The first public package is not yet a
one-click installer because the preserved RoR2 host binary and game-specific assets
cannot legally or reproducibly be bundled here.

For a local prepared package, `tools/Launch-RoRCraft.ps1` can find common Steam and
Prism Launcher locations automatically, install the package, and start RoR2:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Launch-RoRCraft.ps1
```

If autodetection does not find an installation, pass `-GamePath` and
`-MinecraftInstance` explicitly. The launcher must be run beside a prepared package
with `manifest.json`, `Runtime/`, and `Minecraft/`; the public helper ZIP alone is
not a complete binary distribution. Minecraft still has to be started from its own
launcher because the public package cannot redistribute the game or account files.
