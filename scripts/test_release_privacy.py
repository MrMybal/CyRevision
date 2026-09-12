import importlib.util
import io
import json
from pathlib import Path
import tempfile
from unittest.mock import patch
import unittest
import zipfile

def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parent / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

audit = load('audit', 'audit-release-privacy.py')
receipts = load('receipts', 'verify-release-audits.py')

class ReleasePrivacyTests(unittest.TestCase):
    def test_user_paths_ascii_and_wide_both_alignments(self):
        value = 'Z:' + '/Users/' + 'synthetic/Desktop/source.cpp'
        for encoding in ('utf-8', 'utf-16-le', 'utf-16-be'):
            for prefix in (b'', b'x'):
                with self.subTest(encoding=encoding, prefix=prefix):
                    self.assertTrue(audit.issues_for('module.dll', prefix + value.encode(encoding)))

    def test_runtime_files_and_symbols(self):
        for path in ('config.xml', 'state.db', 'profile/key.pem', 'project.pdb', '.cyrevision/state.json', 'backups.zip'):
            self.assertTrue(audit.issues_for(path, b'harmless'))

    def test_secret_and_mailbox_rules(self):
        for data in (('ghp_' + 'a' * 36).encode(), ('tester' + '@' + 'gmail.com').encode(),
                     ('PrivateKey = ' + 'a' * 43 + '=').encode()):
            self.assertTrue(audit.issues_for('module.dll', data))

    def test_relative_source_and_noreply_allowed(self):
        self.assertEqual([], audit.issues_for('module.dll', b'/_/src/main.cs noreply@github.com'))

    def test_archive_traversal_refused(self):
        for name in ('../escape', '/absolute', 'Z:' + '/outside', 'folder/../../outside'):
            with self.assertRaises(ValueError):
                audit.safe_name(name)

    def test_duplicate_member_refused(self):
        scanner = audit.Audit()
        scanner.consume('same.txt', b'one')
        with self.assertRaises(ValueError):
            scanner.consume('same.txt', b'two')

    def test_missing_or_changed_unreal_refused(self):
        scanner = audit.Audit()
        scanner.consume('Variants/UE5.2/Win64/UnrealEditor-CyRevisionEditor.dll', b'changed')
        with self.assertRaises(ValueError):
            scanner.finish()

    def test_checked_unreal_variants(self):
        scanner = audit.Audit()
        for version in range(2, 9):
            name = f'plugins/CyRevisionUnreal/Variants/UE5.{version}/Win64/CyRevisionUnreal/Binaries/Win64/UnrealEditor-CyRevisionEditor.dll'
            scanner.consume(name, (audit.ROOT / name).read_bytes())
        scanner.finish()

    def test_zip_contents_scanned(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('secret.txt', ('ghp_' + 'b' * 36).encode())
        stream.seek(0)
        scanner = audit.Audit()
        scanner.zip(stream)
        self.assertTrue(scanner.failures)

    def test_missing_receipts_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                receipts.verify(Path(folder), '0.1.25')

    def test_vendor_filename_does_not_exempt_modified_bytes(self):
        data = ('/home/' + 'synthetic/project/source.pdb').encode()
        self.assertIn('home directory', audit.issues_for('MicroCom.Runtime.dll', data))

    def test_receipts_bound_to_exact_final_bytes(self):
        version = '0.1.25'
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            names = [f'CyRevision-{version}-{rid}-portable.{ext}' for rid, ext in (
                ('win-x64','zip'),('linux-x64','tar.gz'),('linux-arm64','tar.gz'),('osx-x64','zip'),('osx-arm64','zip'))]
            names += [f'CyRevision-{version}-{rid}.{ext}' for rid, ext in (
                ('linux-x64','deb'),('linux-arm64','deb'),('osx-x64','dmg'),('osx-arm64','dmg'))]
            names += [f'CyRevision-Setup-{version}-win-x64.exe']
            for name in names:
                (root/name).write_bytes(b'synthetic package')
                receipt = {'schema':1, 'package':name, 'sha256':audit.sha(b'synthetic package'),
                           'files_inspected':100, 'unreal_variants':[f'UE5.{v}' for v in range(2,9)], 'result':'passed'}
                (root/(name+'.privacy.json')).write_text(json.dumps(receipt), encoding='utf-8')
            receipts.verify(root, version)
            (root/names[0]).write_bytes(b'changed after audit')
            with self.assertRaises(ValueError):
                receipts.verify(root, version)

    def test_failed_extractor_never_approves_package(self):
        with patch.object(audit.subprocess, 'run') as process:
            process.return_value.returncode = 1
            with self.assertRaises(ValueError):
                audit.run_tool(['synthetic-extractor'])

if __name__ == '__main__':
    unittest.main()
