"""Release source selection must not include unrelated local branch history."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

CHECK = Path(__file__).with_name('check-repository-privacy.py').resolve()


class SourceRevisionTests(unittest.TestCase):
    def test_restore_and_publish_use_matching_self_contained_mode(self):
        scripts = Path(__file__).parent
        for name in ('build-release.ps1', 'build-linux-release.sh', 'build-macos-release.sh'):
            lines = (scripts / name).read_text(encoding='utf-8').splitlines()
            restores = [line for line in lines if 'restore' in line and '--runtime' in line]
            self.assertEqual(3, len(restores), name)
            self.assertTrue(all('/p:SelfContained=true' in line for line in restores), name)
            if name.endswith('.sh'):
                for block in '\n'.join(lines).split('dotnet publish ')[1:]:
                    self.assertNotIn('--no-restore', block.split('/p:DebugSymbols=false')[0], name)
            else:
                for block in '\n'.join(lines).split("'publish',")[1:]:
                    self.assertNotIn("'--no-restore'", block.split("'/p:DebugSymbols=false'")[0], name)

    def test_release_ref_checks_its_ancestry_without_other_branches(self):
        with tempfile.TemporaryDirectory(prefix='release-refs-') as folder:
            root = Path(folder)
            def git(*args):
                return subprocess.run(['git', '-C', folder, *args], check=True,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            def check(*args):
                return subprocess.run([sys.executable, str(CHECK), *args], cwd=folder,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE).returncode
            git('init', '-b', 'main')
            git('config', 'user.name', 'Release Fixture')
            git('config', 'user.email', 'fixture@users.noreply.github.com')
            git('config', 'commit.gpgsign', 'false')
            git('config', 'core.hooksPath', '')
            (root / 'sample.txt').write_text('example', encoding='utf-8')
            git('add', 'sample.txt')
            git('commit', '-m', 'Initial fixture')
            git('checkout', '-b', 'unrelated')
            git('-c', 'user.email=fixture@example.test', 'commit', '--allow-empty', '-m', 'Other branch fixture')
            git('checkout', 'main')
            self.assertEqual(0, check('--ref', 'HEAD'))
            self.assertNotEqual(0, check())
            git('-c', 'user.email=fixture@example.test', 'tag', '-a', 'fixture-tag', '-m', 'Tag fixture')
            self.assertNotEqual(0, check('--ref', 'refs/tags/fixture-tag'))
            self.assertNotEqual(0, check('--ref', 'missing-ref'))


if __name__ == '__main__':
    unittest.main()
