"""Build verified release archives after Build.ps1. Requires Python 3.10+."""
from pathlib import Path
import hashlib
import os
import struct
import zipfile

APP = Path(__file__).resolve().parent.parent
DEST = APP.parent
OFFICIAL_RUNTIME = {"engine/" + row.split("  ", 1)[1] for row in (APP / "Notices/playnite-runtime.sha256").read_text().splitlines()}
OMIT = {"qa", "node_modules", "bin", "obj", "OrbitData", "ExtensionsData", "library", "cache", "webcache", "logs", "browsercache"}

def sha(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def ship(path):
    rel = path.relative_to(APP)
    if rel.as_posix() in OFFICIAL_RUNTIME:
        return path.is_file()
    return (path.is_file() and not path.name.lower().startswith("orbit-report-") and ".partial-" not in path.name.lower() and not any(s in OMIT or s.startswith(".") for s in rel.parts)
            and path.suffix.lower() not in {".pdb", ".log", ".tmp", ".zip"} and rel.as_posix() not in
            {"engine/config.json", "engine/safeStartup.flag", "Orbit-startup-report.txt"})

def pe_complete(path):
    b = path.read_bytes()
    assert b[:2] == b"MZ" and len(b) >= 64, path
    offset = struct.unpack_from("<I", b, 60)[0]
    assert b[offset:offset + 4] == b"PE\0\0", path
    machine, sections = struct.unpack_from("<HH", b, offset + 4)
    optional = struct.unpack_from("<H", b, offset + 20)[0]
    table = offset + 24 + optional
    assert table + sections * 40 <= len(b), path
    for i in range(sections):
        size, start = struct.unpack_from("<II", b, table + i * 40 + 16)
        assert start + size <= len(b), "Incomplete PE section: " + str(path)
    return machine, b

def verify():
    baseline = (APP / "Notices/playnite-runtime.sha256").read_text().splitlines()
    for row in baseline:
        digest, rel = row.split("  ", 1)
        assert ship(APP / "engine" / rel), "Official runtime omitted from package: " + rel
        assert sha(APP / "engine" / rel) == digest, "Runtime differs from official baseline: " + rel
    manifest = (APP / "RuntimeFiles.sha256").read_bytes()
    for row in manifest.decode().splitlines():
        digest, size, rel = row.split("\t")
        p = APP / rel
        assert ship(p) and p.stat().st_size == int(size) and sha(p) == digest, "Rebuild Orbit.exe after changes: " + rel
    own = {"Orbit.exe": 0x8664, "engine/Extensions/Orbit/Orbit.Plugin.dll": 0x14c,
           "engine/Extensions/Orbit/Orbit.InstallHelper.exe": 0x8664}
    for rel, machine in own.items():
        actual, b = pe_complete(APP / rel)
        assert actual == machine, rel
        index = b.find(bytes.fromhex("bd04effe"))
        assert index >= 0 and struct.unpack_from("<II", b, index + 8) == (11, 0), "Wrong version: " + rel
    assert manifest in (APP / "Orbit.exe").read_bytes(), "Embedded startup manifest is stale"
    count = 0
    for p in APP.rglob("*"):
        if ship(p) and p.suffix.lower() in {".dll", ".exe"}:
            pe_complete(p)
            count += 1
    print(f"Verified {len(baseline)} official runtime files, embedded manifest and {count} complete PE files.", flush=True)

def archive(name, files, digests):
    target = DEST / name
    temporary = target.with_suffix(".zip.tmp")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for p in files:
                z.write(p, APP.name + "/" + p.relative_to(APP).as_posix())
        # Do not publish an open ZIP or accept CRC alone for the runtime.
        with zipfile.ZipFile(temporary) as z:
            assert z.testzip() is None, name
            assert len(z.infolist()) == len(files), name
            for p in files:
                member = APP.name + "/" + p.relative_to(APP).as_posix()
                with z.open(member) as stream:
                    h = hashlib.sha256()
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        h.update(chunk)
                assert h.hexdigest() == digests[p], member
        os.replace(temporary, target)
        print(f"{name}: {target.stat().st_size} bytes; {len(files)} files; SHA-256 {sha(target)}", flush=True)
    finally:
        if temporary.exists():
            temporary.unlink()

if __name__ == "__main__":
    verify()
    files = sorted(p for p in APP.rglob("*") if ship(p) and p.name != "SHA256SUMS.txt")
    digests = {p: sha(p) for p in files}
    sums = APP / "SHA256SUMS.txt"
    sums.write_text("".join(digests[p] + "  " + p.relative_to(APP).as_posix() + "\n" for p in files))
    files.append(sums)
    digests[sums] = sha(sums)
    source = [p for p in files if p.relative_to(APP).parts[0] != "engine"
              or p.relative_to(APP).as_posix().startswith("engine/Extensions/Orbit/")]
    archive("Orbit-source-checkpoint.zip", source, digests)
    archive("Orbit-Windows.zip", files, digests)
