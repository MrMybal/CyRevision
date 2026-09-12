"""Regression coverage for package publication manifests."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('release_checksums', Path(__file__).with_name('verify-release-checksums.py'))
checksums = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checksums)

class PackageTests(unittest.TestCase):
    def create(self, root):
        for rid in checksums.PLATFORMS:
            lines = []
            for name in sorted(checksums.expected_packages('1.2.3', rid)):
                data = name.encode()
                (root / name).write_bytes(data)
                lines.append(hashlib.sha256(data).hexdigest() + '  ' + name)
            (root / f'SHA256SUMS-{rid}.txt').write_text('\n'.join(lines), encoding='utf-8')
    def test_valid(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.create(root)
            checksums.verify(root, '1.2.3')
    def test_rejects_mutations_and_extras(self):
        for mutation in ('changed', 'missing', 'extra', 'report', 'duplicate', 'traversal', 'missing_manifest'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                self.create(root)
                package = root / 'CyRevision-Setup-1.2.3-win-x64.exe'
                manifest = root / 'SHA256SUMS-win-x64.txt'
                if mutation == 'changed': package.write_bytes(b'changed')
                elif mutation == 'missing': package.unlink()
                elif mutation == 'extra': (root / 'unrequested.txt').write_text('extra')
                elif mutation == 'report': (root / 'package.privacy.json').write_text('{}')
                elif mutation == 'missing_manifest': manifest.unlink()
                elif mutation == 'duplicate': manifest.write_text(manifest.read_text() + '\n' + manifest.read_text().splitlines()[0])
                elif mutation == 'traversal': manifest.write_text('0' * 64 + '  ../outside')
                with self.assertRaises(ValueError): checksums.verify(root, '1.2.3')
