"""Restore only the pinned, unchanged engine files. Never executes upstream scripts."""
from pathlib import Path
from urllib.request import urlopen
import hashlib
import json
import zipfile
import io

ROOT = Path(__file__).resolve().parent
COMMIT = "6b4375fa04f6bd7017eec379e16e462d3ea8df66"
hashes = json.loads((ROOT / "engine-integrity.json").read_text(encoding="utf-8"))
for name, expected in hashes.items():
    destination = ROOT / "engines" / "zapret2" / name
    if destination.exists():
        data = destination.read_bytes()
    elif name in ("WinDivert.dll", "WinDivert64.sys"):
        url = "https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip"
        with urlopen(url, timeout=45) as response:
            archive = zipfile.ZipFile(io.BytesIO(response.read()))
        data = archive.read("WinDivert-2.2.2-A/x64/" + name)
    else:
        url = f"https://raw.githubusercontent.com/bol-van/zapret-win-bundle/{COMMIT}/zapret-winws/{name}"
        with urlopen(url, timeout=45) as response:
            data = response.read()
    if hashlib.sha256(data).hexdigest().lower() != expected.lower():
        raise SystemExit(f"Integrity mismatch: {name}. Refusing to overwrite or trust the file.")
    if not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
    print(f"Verified: {name}")
