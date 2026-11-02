# Gates: import supplied Orbit redesign

OWNS: engine/Extensions/Orbit/ui/**, Source/Tests/**, RuntimeFiles.sha256, Orbit.exe, REDESIGN-GATES.md

Design contract: original redesign was imported on 2026-10-27. On resume 2026-11-01 the user workspace has moved into Desktop/Projects and contains Orbit 0.18, including a newer redesign, themes and startup work. Preserve these subsequent changes; verify the current design, search and startup rather than restoring 0.11 files. Existing SHA256SUMS.txt changes belong to the user.

- [x] R1: UI regression checks pass after integrating the supplied files.
  CHECK: node Source/Tests/redesign-suite.cjs
  EXPECT: PASS redesign suite
  EVIDENCE: automatic-evidence=v1; definition-sha256=bb5e6cedb0f87ca6bcd69e197a3cd7f2dfa4e04b27f8802b02e644e898855c12; exit=0; EXPECT=matched; output-sha256=ef54a2b84c32d71bd6c8029cd976c64bcac89f8cbbc35ca4adc5c4044d87f03f; output-bytes=72; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Projects\Новая папка\Orbit-Windows; path=3ffec895190d/53 entries

- [ ] R2: Supplied design is installed, manifest rebuilt and native app screens inspected.
  EVIDENCE: pending
