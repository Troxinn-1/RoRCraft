# Known limitations

- Full native builds currently require a preserved host binary whose later draft
  source does not reproduce it. See BUILDING.md.
- The public repo has no portable installer or public runtime archive.
- Long-run melee balance, regeneration, creeper/DOT behavior and all difficulty/
  survivor/item combinations have not been accepted as finished.
- Test mining reported `grounded=false` on native terrain. Grounding and MC mob
  navigation need further real-terrain diagnosis beyond stationary test fixtures.
- Multiplayer and externally loaded shader packs are not completed features.
- Native game/mod-loader warnings exist even in passing local tests.
- F5/inventory runtime skin resolution and missing mining crack export were repaired
  in .37; regressions should be checked against this version.
