# Building and local dependencies

This first public repository is a source snapshot of the working .37 prototype.
The local Windows installation was developed in the same `outputs/...` layout.
It currently depends on local files that are intentionally not redistributed.

## Minecraft bridge

Install Java 25, set `JAVA_HOME`, then from the repository root:

```powershell
./outputs/TechnicalPolish/fabric/gradlew.bat -p outputs/TechnicalPolish/fabric build
```

Gradle/Loom obtains Minecraft development dependencies from their official
repositories. The local build passed 42 JUnit tests. The mod identifier remains
`skycraft` for compatibility with the existing bridge and saves.

## Native modules

The C# scripts currently expect:

- The player's local RoR2 managed assemblies at the standard Steam Windows path.
- A local BepInEx profile at `outputs/Minecraft-RoR2-Bridge/profile/BepInEx`.
- RiskOfOptions at `outputs/TechnicalPolish/lib/RiskOfOptions`.
- The preserved native RoRCraft host DLL, local generated UI/audio resources and
  the authored shader bundle.

Scripts presently retain those local development paths. They are not a portable
one-command build or end-user installer. Do not substitute game assemblies into
this repository. Configure equivalent local paths before using the native scripts.

### Preserved host compatibility

The validated .37 runtime still uses the earlier host 0.7 DLL, SHA256:

`239DFD460CDA86E02411506B2361C92C075C61BE5C05B95EFE194EC9E3B14B3E`

The host source under `outputs/RoRCraft-SkyCraft/src` is a later draft (0.8) and
does **not** reproduce that DLL. The active patches target the preserved host.
Recovering/reconciling the host source and making the full native build reproducible
is required before claiming a public release. Rebuilding the draft and replacing
the validated host without checking compatibility can break the bridge.

## Shader and local assets

`Build-BlockShader.ps1 -UnityEditor <path>` builds the authored shader using the
included Unity project. Its builder requires Unity 2021.3.33f1 with an activated
license; the tested game's actual engine is 2021.3.45f2.

The Python asset preparation scripts read the user's locally installed Minecraft
client/assets. Their resulting `ui-assets` contain game textures and audio and
must stay local. NumPy, Pillow and soundfile may be required for preparation.
Generated font mappings are also excluded.

The shipped local `RoRCraft.Visuals.dll` embeds those assets. It is therefore
excluded from this source-only public snapshot along with the installation bundle.
There is deliberately no downloadable game/runtime archive in this repository.
