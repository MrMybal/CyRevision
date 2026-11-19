"""Validate the exact vendored SDK bytes without fetching or modifying the SDK."""
import hashlib
import json
from pathlib import Path
root = Path(__file__).resolve().parent.parent
lock = json.loads((root / "native/cymcp.lock.json").read_text(encoding="utf-8-sig"))
for name, expected in lock["files"].items():
    path = root / "native/vendor/CyMCPSDK" / name
    if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise SystemExit("CyMCPSDK snapshot mismatch: " + name)
print("CyMCPSDK source snapshot matches " + lock["revision"])
