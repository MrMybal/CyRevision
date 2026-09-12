"""Fail-closed inspection of release payloads; never print matched private values."""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('repository_privacy', ROOT / 'scripts/check-repository-privacy.py')
privacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(privacy)
MANIFEST = ROOT / 'scripts/release-unreal-sha256.json'
PUBLIC_VENDOR_RECORDS = json.loads((ROOT / 'scripts/public-vendor-debug-records.json').read_text(encoding='utf-8'))
PUBLIC_MACOS_RECORDS = json.loads((ROOT / 'scripts/public-macos-runtime-records.json').read_text(encoding='utf-8'))['files']
EXTRACTOR_URL = 'https://github.com/UserUnknownFactor/innoextract_win/releases/download/670/innoextract670.zip'
EXTRACTOR_SHA = '79b69b9b1fcd98f42ccd4b245efdf6a03bcfb674ba6af482f5a46891c9ed4d14'
MAX_MEMBER = 512 * 1024 * 1024
MAX_TOTAL = 8 * 1024 * 1024 * 1024
EXTRA_RULES = {
    'home directory': re.compile(rb'(?i)/(?:Users|home)/[a-z0-9_.-]+/'),
    'personal mailbox': re.compile(rb'(?i)[a-z0-9._%+-]+@(?:gmail|hotmail|outlook|yahoo)\.[a-z]{2,}'),
    'access token': re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|sk-proj-[A-Za-z0-9_-]{30,})'),
    'WireGuard private key': re.compile(rb'(?im)^\s*PrivateKey\s*=\s*[A-Za-z0-9+/]{43}='),
}
PRIVATE_NAMES = {'config.xml', 'cert.pem', 'key.pem', 'identity.json', 'credentials.json', 'projects.json', 'preferences.json'}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def file_sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def safe_name(name):
    name = name.replace('\\', '/')
    p = PurePosixPath(name)
    if p.is_absolute() or '..' in p.parts or re.match(r'^[A-Za-z]:', name):
        raise ValueError('Unsafe archive member path (value withheld)')
    return name

def issues_for(name, data):
    issues = privacy.inspect(name, data)
    base = PurePosixPath(name).name.lower()
    if base in PRIVATE_NAMES or base.endswith(('.pdb', '.db', '.sqlite', '.sqlite3', '.db-wal', '.db-shm', '.log', '.pem', '.key', '.pfx', '.p12')):
        issues.append('runtime data, credential file or debug symbols')
    if base.endswith(('.zip', '.7z', '.rar', '.tar', '.tar.gz', '.bak', '.backup')):
        issues.append('undeclared nested archive or backup in payload')
    # Decode both alignments: .NET user strings and native wide strings need not
    # start at even file offsets. Scan UTF-16LE and UTF-16BE in addition to bytes.
    texts = [data]
    for encoding in ('utf-16-le', 'utf-16-be'):
        for offset in (0, 1):
            texts.append(data[offset:].decode(encoding, errors='ignore').encode('utf-8'))
    for text in texts[1:]:
        for label, pattern in (('Windows user profile path', privacy.PROFILE_PATH), ('local project path', privacy.LOCAL_PROJECT), ('private key', privacy.PRIVATE_KEY)):
            if pattern.search(text):
                issues.append(label)
    for label, pattern in EXTRA_RULES.items():
        if any(pattern.search(text) for text in texts):
            issues.append(label)
    vendor = PUBLIC_VENDOR_RECORDS.get(PurePosixPath(name).name)
    digest = sha(data)
    mac_vendor = PUBLIC_MACOS_RECORDS.get(digest)
    if (vendor and digest == vendor['sha256']) or (mac_vendor and mac_vendor[0] == PurePosixPath(name).name):
        # These exact upstream NuGet DLLs contain public upstream build records.
        # Do not patch signed third-party binaries. No other rule is exempted,
        # and any rebuild/replacement invalidates this narrow hash-bound review.
        issues = [issue for issue in issues if issue != 'home directory']
    return sorted(set(issues))

class Audit:
    def __init__(self):
        self.expected = json.loads(MANIFEST.read_text(encoding='utf-8'))
        self.unreal = set()
        self.files = 0
        self.bytes = 0
        self.failures = []
        self.seen = set()

    def consume(self, name, data):
        name = safe_name(name)
        if name in self.seen:
            raise ValueError('Duplicate archive entry')
        self.seen.add(name)
        self.files += 1
        self.bytes += len(data)
        if len(data) > MAX_MEMBER or self.bytes > MAX_TOTAL:
            raise ValueError('Release inspection size limit exceeded')
        for reason in issues_for(name, data):
            # Names can themselves contain a secret: identify by ordinal only.
            self.failures.append(f'file #{self.files}: {reason}')
        if PurePosixPath(name).name == 'UnrealEditor-CyRevisionEditor.dll':
            match = re.search(r'Variants/(UE5\.[2-8])/Win64/', name)
            if not match or sha(data) != self.expected.get(match.group(1)):
                self.failures.append(f'file #{self.files}: Unreal DLL is not an approved privacy-reviewed build')
            else:
                self.unreal.add(match.group(1))

    def tree(self, root):
        for path in sorted(root.rglob('*')):
            if path.is_symlink():
                target = path.resolve()
                # Only explicit OS installation shortcuts may point outside.
                if str(target) == '/Applications' and path.name == 'Applications':
                    continue
                if not target.is_relative_to(root.resolve()):
                    raise ValueError('External link in release payload')
                continue
            if path.is_file():
                if path.stat().st_size > MAX_MEMBER:
                    raise ValueError('Oversized release member')
                self.consume(path.relative_to(root).as_posix(), path.read_bytes())

    def zip(self, path):
        with zipfile.ZipFile(path) as archive:
            for member in archive.infolist():
                safe_name(member.filename)
                if member.is_dir():
                    continue
                if member.file_size > MAX_MEMBER:
                    raise ValueError('Oversized ZIP member')
                if (member.external_attr >> 16) & 0o170000 == 0o120000:
                    target = archive.read(member).decode('utf-8')
                    if target.startswith('/') or '..' in PurePosixPath(target).parts:
                        raise ValueError('Unsafe ZIP link')
                self.consume(member.filename, archive.read(member))

    def tar(self, stream):
        with tarfile.open(fileobj=stream, mode='r|*') as archive:
            for member in archive:
                safe_name(member.name)
                if member.issym() or member.islnk():
                    # Debian launcher links are validated without following them.
                    target = member.linkname
                    if target.startswith('/') or '..' in PurePosixPath(target).parts:
                        if not (member.name.startswith('./usr/bin/') and target.startswith('../lib/cyrevision/')):
                            raise ValueError('Unsafe TAR link')
                    continue
                if not member.isfile():
                    if not member.isdir():
                        raise ValueError('Special file in TAR')
                    continue
                if member.size > MAX_MEMBER:
                    raise ValueError('Oversized TAR member')
                self.consume(member.name, archive.extractfile(member).read())

    def finish(self):
        if self.unreal != set(self.expected):
            self.failures.append('Expected seven reviewed Unreal DLL variants in payload')
        if not self.files:
            self.failures.append('Empty payload')
        if self.failures:
            raise ValueError('\n'.join(self.failures))

def run_tool(args):
    # Tool logs may contain host paths: do not echo them into public audit logs.
    result = subprocess.run([str(x) for x in args], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise ValueError(f'Extraction/inspection tool failed (exit {result.returncode}); no package approved')

def get_extractor():
    folder = ROOT / '.tools/privacy-innoextract'
    archive = folder / 'tool.zip'
    folder.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        with urllib.request.urlopen(EXTRACTOR_URL) as response:
            archive.write_bytes(response.read())
    if file_sha(archive) != EXTRACTOR_SHA:
        raise ValueError('Installer extractor checksum mismatch')
    # Re-extract the exact checked tools, never trust a stale local executable.
    with zipfile.ZipFile(archive) as z:
        for item in z.infolist():
            name = safe_name(item.filename)
            if name not in ('innoextract.exe', 'libbz2-1.dll'):
                raise ValueError('Unexpected extractor content')
            (folder / name).write_bytes(z.read(item))
    return folder / 'innoextract.exe'

def audit_package(path):
    audit = Audit()
    if path.name.endswith('.zip'):
        audit.zip(path)
    elif path.name.endswith('.tar.gz'):
        with path.open('rb') as stream:
            audit.tar(stream)
    elif path.suffix == '.deb':
        with tempfile.TemporaryDirectory(prefix='cyrevision-audit-') as temp:
            # Also inspect control scripts and metadata, not just data.tar.
            for flag, label in (('--fsys-tarfile', 'data'), ('--ctrl-tarfile', 'control')):
                output = Path(temp) / (label + '.tar')
                with output.open('wb') as stream:
                    result = subprocess.run(['dpkg-deb', flag, str(path)], stdout=stream, stderr=subprocess.PIPE)
                if result.returncode:
                    raise ValueError('Debian extraction failed')
                with output.open('rb') as stream:
                    audit.tar(stream)
    elif path.suffix == '.exe':
        if sys.platform != 'win32':
            raise ValueError('Installer auditing requires Windows')
        # Inspect outer PE as well as the decompressed installation payload.
        for issue in issues_for('installer.exe', path.read_bytes()):
            audit.failures.append('installer wrapper: ' + issue)
        with tempfile.TemporaryDirectory(prefix='cyrevision-audit-') as temp:
            run_tool([get_extractor(), '--silent', '--extract', '--output-dir', temp, path.resolve()])
            audit.tree(Path(temp))
    elif path.suffix == '.dmg':
        if sys.platform != 'darwin':
            raise ValueError('DMG auditing requires macOS')
        with tempfile.TemporaryDirectory(prefix='cyrevision-audit-') as temp:
            run_tool(['hdiutil', 'attach', '-readonly', '-nobrowse', '-mountpoint', temp, path.resolve()])
            try:
                run_tool(['codesign', '--verify', '--strict', str(Path(temp) / 'CyRevision.app')])
                audit.tree(Path(temp))
            finally:
                run_tool(['hdiutil', 'detach', temp])
    else:
        raise ValueError('Unsupported package format')
    audit.finish()
    return audit

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tree', type=Path)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    if bool(args.tree) == bool(args.package):
        parser.error('Choose exactly one of --tree or --package')
    try:
        if args.tree:
            audit = Audit()
            audit.tree(args.tree)
            audit.finish()
        else:
            audit = audit_package(args.package)
        if args.report:
            if not args.package:
                raise ValueError('Reports require a final package')
            report = {'schema': 1, 'package': args.package.name, 'sha256': file_sha(args.package),
                      'files_inspected': audit.files, 'unreal_variants': sorted(audit.unreal), 'result': 'passed',
                      'scope': 'Decompressed payload, binary strings and debug-symbol files; not an Unreal runtime test.'}
            args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        if not os.environ.get('CI'):
            print(f'Privacy audit passed: {audit.files} files, seven approved Unreal variants.')
        return 0
    except Exception as error:
        if os.environ.get('CI'):
            print('Package validation failed; publication stopped.', file=sys.stderr)
        elif isinstance(error, ValueError):
            print(str(error), file=sys.stderr)
        else:
            print(f'Privacy inspection failed ({type(error).__name__}); no package approved.', file=sys.stderr)
        return 1

if __name__ == '__main__':
    sys.exit(main())
