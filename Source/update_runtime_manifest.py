import os, hashlib, struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Step 1: Run powershell to generate RuntimeFiles.sha256
manifest_script = ROOT / "Source" / "Write-RuntimeManifest.ps1"
import subprocess
print("Running Write-RuntimeManifest.ps1...")
res = subprocess.run(["powershell", "-File", str(manifest_script)], cwd=str(ROOT), capture_output=True, text=True)
print(res.stdout)
if res.stderr:
    print("Stderr:", res.stderr)

# Step 2: Read RuntimeFiles.sha256 and enforce LF line endings
manifest_file = ROOT / "RuntimeFiles.sha256"
content = manifest_file.read_bytes().replace(b"\r\n", b"\n")
if not content.endswith(b"\n"):
    content += b"\n"
manifest_file.write_bytes(content)
print(f"Manifest ready: {len(content)} bytes ({len(content.splitlines())} files)")

# Step 3: Patch Orbit.exe
exe_file = ROOT / "Orbit.exe"
exe_data = bytearray(exe_file.read_bytes())

# Find old manifest position in exe
# Look for a known file in manifest
marker = b"\tengine/Extensions/Orbit/Orbit.Plugin.dll\n"
idx = exe_data.find(marker)
assert idx >= 0, "Could not find manifest marker in Orbit.exe"

# The manifest start is at offset 31600
manifest_start = 31600
old_len = struct.unpack("<I", exe_data[manifest_start - 4 : manifest_start])[0]
print(f"Found manifest in Orbit.exe at offset {manifest_start}, recorded length = {old_len}")

# Let's check how long the resource area is
# If new content fits in the same area or if length differs:
assert old_len == 154230, f"Expected old_len 154230, got {old_len}"

# If len(content) == old_len:
if len(content) == old_len:
    exe_data[manifest_start : manifest_start + old_len] = content
    print("Manifest replaced in-place!")
else:
    print(f"Manifest size changed: {old_len} -> {len(content)}")
    # update length header
    struct.pack_into("<I", exe_data, manifest_start - 4, len(content))
    # replace slice
    exe_data[manifest_start : manifest_start + old_len] = content[:old_len]
    if len(content) < old_len:
        # pad with zeros
        pad = old_len - len(content)
        exe_data[manifest_start + len(content) : manifest_start + old_len] = b"\x00" * pad
    # If greater: we can pad or check
    assert len(content) <= old_len, f"New manifest is larger than allocated slot ({len(content)} > {old_len})"

exe_file.write_bytes(exe_data)
print("Orbit.exe updated successfully!")

# Step 4: Verify with Package.py
import sys
sys.path.insert(0, str(ROOT / "Source"))
import Package
Package.verify()
print("ALL RUNTIME INTEGRITY VERIFICATIONS PASSED!")
