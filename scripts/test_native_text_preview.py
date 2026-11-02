"""Verify real text previews and capture the native UI using only temporary fixtures."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parent.parent

def run(kind, themes, split=False, comfortable=False, locale='en'):
    executable = ROOT / 'artifacts/native-v2/CyRevision.Desktop.exe'
    frontend = executable.with_name('CyRevision.Native.exe')
    output = ROOT / 'artifacts/ui-preview-checks'
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='text-preview-', dir=ROOT / 'artifacts') as temporary:
        root = Path(temporary)
        repository = root / 'Preview fixture'
        repository.mkdir()
        def git(*args):
            subprocess.run(['git', '-C', str(repository), *args], check=True, capture_output=True)
        git('init', '-b', 'main')
        git('config', 'user.name', 'CyRevision Fixture')
        git('config', 'user.email', 'automation@users.noreply.github.com')
        (repository / '.gitignore').write_text('/.cyrevision/\n', encoding='utf-8')
        if kind == 'ini':
            (repository / 'Config').mkdir()
            fixture = repository / 'Config/DefaultEngine.ini'
            fixture.write_text('[Engine]\nName=Preview fixture\nEnabled=False\nWorkerCount=4\n\n[Rendering]\nQuality=Medium\n', encoding='utf-8')
        git('add', '.')
        git('commit', '-m', 'Initial preview fixture')
        if kind == 'ini':
            fixture.write_text('[Engine]\nName=Preview fixture\nEnabled=True\nWorkerCount=8\nUseAsync=True\n\n[Rendering]\nQuality=High\n', encoding='utf-8')
            expected = '+Enabled=True'
        else:
            fixture = repository / 'large.json'
            fixture.write_text(json.dumps({'previewFixture': True, 'name': 'Large JSON file', 'items': [{'id': i, 'label': 'Example asset', 'enabled': True} for i in range(45000)]}, indent=2), encoding='utf-8')
            assert fixture.stat().st_size > 2 * 1024 * 1024
            expected = '+  "previewFixture": true'
        flags = getattr(subprocess, 'CREATE_NO_WINDOW', 0)
        process = subprocess.Popen([str(executable), '--native-backend', '--profile-dir', str(root / 'profile')], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8', creationflags=flags)
        responses = queue.Queue()
        def reader():
            for line in process.stdout:
                responses.put(json.loads(line))
        threading.Thread(target=reader, daemon=True).start()
        counter = 0
        def request(command, **args):
            nonlocal counter
            counter += 1
            process.stdin.write(json.dumps(dict(id=counter, command=command, **args)) + '\n')
            process.stdin.flush()
            result = responses.get(timeout=30)
            assert result['id'] == counter and result.get('ok'), result.get('error', 'Preview fixture request failed')
            return result.get('result')
        try:
            request('catalog')
            deadline = time.monotonic() + 30
            while not request('state', workspace='changes').get('ready'):
                assert time.monotonic() < deadline, 'Preview engine initialization timed out'
                time.sleep(.1)
            request('invoke', action='AddExistingRepositoryAsync', parameters={'path': str(repository)})
            deadline = time.monotonic() + 30
            while True:
                state = request('state', workspace='changes', fields=['DiffText', 'IsWorkingTreeDiffLoading'])
                if state.get('ready') and not state.get('busy') and expected in state['fields'].get('DiffText', {}).get('value', ''):
                    break
                assert time.monotonic() < deadline, 'The selected JSON/INI text never became visible'
                time.sleep(.1)
            diff = state['fields']['DiffText']['value']
            assert 'text preview limited to 2 MB' not in diff
            assert 'binary file' not in diff
            if kind == 'json':
                assert '[Preview truncated:' in diff and len(diff) < 2 * 1024 * 1024
            request('shutdown')
            process.wait(timeout=15)
            assert process.returncode == 0
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=15)
            process.stdin.close()
            process.stdout.close()
        for theme in themes:
            capture = output / f'{kind}-{theme}{"-split" if split else ""}{"-comfortable" if comfortable else ""}{"-fr" if locale == "fr" else ""}.bmp'
            arguments = [str(frontend), '--profile-dir', str(root / 'profile'), '--capture', str(capture), '--capture-workspace', 'changes', '--theme', theme]
            if split:
                arguments.append('--capture-side-by-side')
            if comfortable:
                arguments.append('--comfortable')
            arguments.extend(['--locale', locale])
            subprocess.run(arguments, check=True, timeout=35, creationflags=flags)
            assert capture.stat().st_size > 100000
            observed = json.loads(Path(str(capture) + '.workspace.json').read_text(encoding='utf-8'))
            assert observed['ready'] and observed['workspace'] == 'changes'
            assert observed['selectedDiffPath'] == fixture.relative_to(repository).as_posix()
            assert observed['diffCharacters'] > (1000 if kind == 'json' else 50), 'The capture did not contain the selected file preview'
    print(f'Native {kind} preview passed, with captures for: {", ".join(themes)}')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--kind', choices=['ini', 'json'], default='ini')
    parser.add_argument('--themes', nargs='+', default=['mid-grey', 'light-grey', 'amber-night', 'violet', 'navy'])
    parser.add_argument('--side-by-side', action='store_true')
    parser.add_argument('--comfortable', action='store_true')
    parser.add_argument('--locale', choices=['en', 'fr'], default='en')
    args = parser.parse_args()
    run(args.kind, args.themes, args.side_by_side, args.comfortable, args.locale)
