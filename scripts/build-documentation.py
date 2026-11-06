"""Compatibility command for the application's CyDoc generation pipeline."""
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
raise SystemExit(subprocess.call(["node", str(root / ".CyDocBuild/build.mjs"), *sys.argv[1:]], cwd=root))
