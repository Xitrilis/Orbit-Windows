# Gates: Orbit startup and truthful data repair

OWNS: engine/Extensions/Orbit/ui/**, Source/OrbitPlugin.cs, Source/Tests/**, Source/update_runtime_manifest.py, RuntimeFiles.sha256, Orbit.exe, engine/Extensions/Orbit/Orbit.Plugin.dll, GATES.md

Scope: Repair startup, replace fabricated friends and prices with supported actions and real data, verify the installed Windows build.

- [x] G1: UI starts without errors; friends, empty prices, genuine discounts, library and region handling pass regression checks.
  CHECK: node Source/Tests/repair-ui.cjs
  EXPECT: PASS repair UI
  EVIDENCE: automatic-evidence=v1; definition-sha256=01c2890ff45b53ee6738ae30226afe762b18ca7f397072130a65fd1a57e5d807; exit=0; EXPECT=matched; output-sha256=4b46860926520239cb6ca539a59d5f3d42d105a739bf5321936432c9f4de000d; output-bytes=15; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Новая папка\Orbit-Windows; path=c0f14cf478bc/53 entries

- [x] G2: The shipped runtime hashes and embedded startup manifest match the repaired files.
  CHECK: node Source/Tests/runtime-check.cjs
  EXPECT: PASS runtime integrity
  EVIDENCE: automatic-evidence=v1; definition-sha256=f2e825dddadd734fc61f07ef5697b5f68b97a953734fb2c41a756e9df7f1e4dd; exit=0; EXPECT=matched; output-sha256=aaa6b3c94faa7b397f7f0c0cbbf063f94a96c16b31ca62fee346eff39f28db6f; output-bytes=107; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Новая папка\Orbit-Windows; path=c0f14cf478bc/53 entries

- [ ] G3: Restarted Windows Orbit opens library, friends and store without new UI error events; Steam region is KZ and prices show ruble conversions.
  EVIDENCE: pending
