"""Validate release package names and hashes produced by native build jobs."""
import argparse
import hashlib
import re
from pathlib import Path
import sys

PLATFORMS = ('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')

def expected_packages(version, rid):
    if rid == 'win-x64':
        return {f'CyRevision-Setup-{version}-{rid}.exe', f'CyRevision-{version}-{rid}-portable.zip'}
    if rid.startswith('linux-'):
        return {f'CyRevision-{version}-{rid}.deb', f'CyRevision-{version}-{rid}-portable.tar.gz'}
    return {f'CyRevision-{version}-{rid}.dmg', f'CyRevision-{version}-{rid}-portable.zip'}

def verify(root, version):
    expected = set().union(*(expected_packages(version, rid) for rid in PLATFORMS))
    manifests = {f'SHA256SUMS-{rid}.txt' for rid in PLATFORMS}
    entries = list(root.iterdir())
    if any(not p.is_file() or p.is_symlink() for p in entries):
        raise ValueError('Unexpected package entry')
    if {p.name for p in entries} != expected | manifests:
        raise ValueError('Unexpected or missing package')
    for rid in PLATFORMS:
        declared = {}
        for line in (root / f'SHA256SUMS-{rid}.txt').read_text(encoding='utf-8-sig').splitlines():
            match = re.fullmatch(r'([0-9a-fA-F]{64})  ([^/\\\\]+)', line)
            if not match or match[2] in declared:
                raise ValueError('Invalid checksum manifest')
            declared[match[2]] = match[1].lower()
        if set(declared) != expected_packages(version, rid):
            raise ValueError('Unexpected platform package')
        for name, expected_digest in declared.items():
            with (root / name).open('rb') as stream:
                actual = hashlib.file_digest(stream, 'sha256').hexdigest()
            if actual != expected_digest:
                raise ValueError('Package checksum mismatch')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('version')
    args = parser.parse_args()
    try:
        verify(args.directory, args.version)
    except Exception:
        print('Package validation failed; publication stopped.', file=sys.stderr)
        sys.exit(1)
