"""Verify that every final package has a successful, matching privacy receipt."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys

def verify(root, version):
    expected = {f'CyRevision-{version}-{rid}-portable.{extension}' for rid, extension in (
        ('win-x64', 'zip'), ('linux-x64', 'tar.gz'), ('linux-arm64', 'tar.gz'), ('osx-x64', 'zip'), ('osx-arm64', 'zip'))}
    expected |= {f'CyRevision-{version}-{rid}.{extension}' for rid, extension in (
        ('linux-x64', 'deb'), ('linux-arm64', 'deb'), ('osx-x64', 'dmg'), ('osx-arm64', 'dmg'))}
    expected.add(f'CyRevision-Setup-{version}-win-x64.exe')
    packages = {p.name for p in root.iterdir() if p.is_file() and p.name.endswith(('.exe', '.zip', '.tar.gz', '.deb', '.dmg'))}
    if packages != expected:
        raise ValueError('Unexpected or missing release package; nothing may be published')
    for name in sorted(expected):
        path = root / name
        report = json.loads((root / (name + '.privacy.json')).read_text(encoding='utf-8'))
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        if (report.get('schema') != 1 or report.get('result') != 'passed'
                or report.get('package') != name or report.get('sha256') != digest
                or report.get('files_inspected', 0) < 100
                or report.get('unreal_variants') != [f'UE5.{v}' for v in range(2, 9)]):
            raise ValueError('Invalid or stale package privacy receipt')
    if not os.environ.get('CI'):
        print('All ten release packages have matching privacy receipts.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('version')
    args = parser.parse_args()
    try:
        verify(args.directory, args.version)
    except Exception:
        print('Package validation failed; publication stopped.' if os.environ.get('CI') else 'Release privacy receipts missing, invalid or stale; publication refused.', file=sys.stderr)
        sys.exit(1)
