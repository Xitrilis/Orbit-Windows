# Gates: Orbit everyday use

OWNS: engine/Extensions/Orbit/ui/**, Source/PriceService.cs, Source/OrbitPlugin.cs, Source/Tests/**, RuntimeFiles.sha256, Orbit.exe, engine/Extensions/Orbit/Orbit.Plugin.dll, UX-GATES.md

Design: Windows game library, left aligned. Segoe UI at 13/16/24px. Background #202329, sidebar #191c21, surface #292d34, text #edf0f4, secondary #abb3bf, selection #86b7ec. Compact navigation and artwork grid; no marketing hero in the library. Restrained framing, visible focus, no card motion. Compared with the brief: remove decorative slogans and repeated branding, preserve real cover artwork and startup behavior.

- [x] G1: Automatic pagination preserves loaded nodes and filters; RUB estimates appear at the top; inherited regions are labeled.
  CHECK: node Source/Tests/ux-ui.cjs
  EXPECT: PASS UX UI
  EVIDENCE: automatic-evidence=v1; definition-sha256=59e8a7d3e8b343875c88c57cbebade09f9683712037c66827c451c205fab59db; exit=0; EXPECT=matched; output-sha256=544b5f1fa6bf230ec13fb60c2cd3f8b2408496905e78dc6d52afd8bc12d9dd55; output-bytes=11; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Новая папка\Orbit-Windows; path=3851d3f5b6a5/53 entries

- [x] G2: Existing startup, friends, prices and library behavior remains valid.
  CHECK: node Source/Tests/repair-ui.cjs
  EXPECT: PASS repair UI
  EVIDENCE: automatic-evidence=v1; definition-sha256=01c2890ff45b53ee6738ae30226afe762b18ca7f397072130a65fd1a57e5d807; exit=0; EXPECT=matched; output-sha256=4b46860926520239cb6ca539a59d5f3d42d105a739bf5321936432c9f4de000d; output-bytes=15; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Новая папка\Orbit-Windows; path=3851d3f5b6a5/53 entries

- [x] G3: Compiled plugin, runtime integrity and native scrollbar checked after restart.
  EVIDENCE: 2026-10-27: Roslyn plugin build passed; 13 pricing checks passed including shared region and explicit override; sales-ui and home-ui passed. Rebuilt launcher from current Gemini bootstrap source; Package.verify verified 876 official files, embedded manifest and 190 PE files. Native Orbit reopened. Dragged scrollbar at x=1346 from y=106 to y=510: content scrolled and window stayed 1360x880. Drag to bottom loaded another batch automatically. Live Sons of the Forest displayed 2128 KZT and approx 379.75 RUB in top summary; Epic returned HTTP 403 and GOG exact title was absent, with no configuration demand. Final restart verified after calendar separation and pagination cache fix.
