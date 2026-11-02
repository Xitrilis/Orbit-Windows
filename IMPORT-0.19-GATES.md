# Gates: import Orbit 0.19 from Desktop/123123

OWNS: engine/Extensions/Orbit/Orbit.Plugin.dll, IMPORT-0.19-GATES.md

Scope: Compare the complete supplied package with the main workspace, copy changed release files while preserving local data and workspace-only files, verify package integrity and the new EA/Ubisoft implementation, then open the Windows launcher.

- [x] I1: Imported package passes its embedded manifest and official runtime checks.
  CHECK: C:/Users/tabal/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe -c "import sys;sys.path.insert(0,'Source');import Package;Package.verify()"
  EXPECT: Verified 876 official runtime files, embedded manifest and 191 complete PE files.
  EVIDENCE: automatic-evidence=v1; definition-sha256=d618ab6f63ebaeb482a11f401c229116745259d9a75423eb7e60d4a8de73b8ec; exit=0; EXPECT=matched; output-sha256=d37c140d9dccb9f5269b9b22d8d85826846f0e0706cdf6971946dbf9365b01cf; output-bytes=83; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Projects\Новая папка\Orbit-Windows; path=3ffec895190d/53 entries

- [x] I2: New publisher price UI passes regression checks.
  CHECK: node Source/Tests/publisher-prices-ui.cjs
  EXPECT: Publisher UI passed:
  EVIDENCE: automatic-evidence=v1; definition-sha256=99dc7d0dc1378a0664bde598a481fc0b832e025c8bbacefb2f8d5a143b6be1e0; exit=0; EXPECT=matched; output-sha256=2a1b819f697c275fe3638a5b3606abfe4781b38d0c9482860c67e6f7c1932de6; output-bytes=175; shell=C:\WINDOWS\system32\cmd.exe; cwd=C:\Users\tabal\Desktop\Projects\Новая папка\Orbit-Windows; path=3ffec895190d/53 entries

- [x] I3: File comparison and live Windows launch verified.
  EVIDENCE: qa/import_v019.py reports 1452 source files and changed=0 after import; destination-only local configuration and workspace files preserved. Playnite.DesktopApp.exe PID 7540 and BrowserProcess children are running from the main Orbit-Windows path.
