"""Exercise conflict editor draft/action transactions in a disposable Git repository."""
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]

def run():
    with tempfile.TemporaryDirectory(prefix='native-conflict-', dir=ROOT / 'artifacts') as temporary:
        root = Path(temporary)
        repository = root / 'repository'
        repository.mkdir()
        def git(*args, success=True):
            result = subprocess.run(['git', '-C', str(repository), *args], capture_output=True, text=True, encoding='utf-8')
            assert result.returncode == (0 if success else 1), result.stderr
            return result.stdout
        git('init', '-b', 'main')
        git('config', 'user.name', 'CyRevision Test')
        git('config', 'user.email', 'automation@users.noreply.github.com')
        file = repository / 'settings.ini'
        file.write_text('[Engine]\nMode=base\n', encoding='utf-8')
        git('add', '.'); git('commit', '-m', 'Base fixture')
        git('switch', '-c', 'incoming')
        file.write_text('[Engine]\nMode=incoming\n', encoding='utf-8')
        git('commit', '-am', 'Incoming fixture')
        git('switch', 'main')
        file.write_text('[Engine]\nMode=ours\n', encoding='utf-8')
        git('commit', '-am', 'Current fixture')
        git('merge', 'incoming', success=False)
        original = file.read_text(encoding='utf-8')
        process = subprocess.Popen([str(ROOT / 'artifacts/native-v2/CyRevision.Desktop.exe'), '--native-backend', '--profile-dir', str(root / 'profile')],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8', creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        messages = queue.Queue()
        def read():
            for line in process.stdout:
                messages.put(json.loads(line))
        threading.Thread(target=read, daemon=True).start()
        counter, project = 0, ''
        def request(command, expect_error=False, **values):
            nonlocal counter
            counter += 1
            process.stdin.write(json.dumps(dict(id=counter, command=command, projectId=project, **values)) + '\n'); process.stdin.flush()
            response = messages.get(timeout=30)
            assert response['id'] == counter, 'Mismatched engine response'
            assert bool(response.get('ok')) != expect_error, response.get('error', 'Unexpected accepted request')
            return response.get('result')
        def state():
            return request('state', workspace='changes', table='NativeGitConflicts')
        def ready():
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                result = state()
                if result.get('ready') and not result.get('busy'): return result
                time.sleep(.1)
            raise AssertionError('Engine initialization timed out')
        try:
            request('catalog'); ready()
            request('invoke', action='AddExistingRepositoryAsync', parameters={'path': str(repository)})
            project = ready()['projectId']
            request('gitConflict', operation='open')
            snapshot = ready()
            row = snapshot['table']['rows'][0]
            request('gitConflict', operation='select', key=row['key'])
            snapshot = ready()
            fields = snapshot['fields']
            key = fields['GitConflictEditorSelectedKey']['value']
            path = fields['GitConflictEditorPath']['value']
            draft = fields['GitConflictEditorResult']['value']
            proposed = draft.replace('Mode=ours', 'Mode=edited')
            request('gitConflict', expect_error=True, operation='resolve', key=key, path=path, draftResult='unapproved\n')
            assert ready()['fields']['GitConflictEditorResult']['value'] == draft
            assert file.read_text(encoding='utf-8') == original
            request('gitConflict', expect_error=True, operation='nextBlock', key='stale-key', path=path, draftResult=proposed)
            assert ready()['fields']['GitConflictEditorResult']['value'] == draft
            request('gitConflict', operation='nextBlock', key=key, path=path, draftResult=proposed)
            assert ready()['fields']['GitConflictEditorResult']['value'] == proposed
            assert file.read_text(encoding='utf-8') == original, 'A preview wrote the working file'
            request('gitConflict', operation='backup', enabled=False, retention='30')
            result = '[Engine]\nMode=reviewed\n'
            request('gitConflict', operation='resolve', key=key, path=path, draftResult=result, confirmed=True)
            assert file.read_text(encoding='utf-8') == result
            assert not git('diff', '--name-only', '--diff-filter=U').strip()
            assert git('show', ':settings.ini') == result
            request('shutdown'); process.wait(timeout=15); assert process.returncode == 0
        finally:
            if process.poll() is None:
                process.kill(); process.wait(timeout=15)
            process.stdin.close(); process.stdout.close()
    print('Conflict editor integration passed: stale selection rejection, draft/action transaction, confirmation, unchanged preview and explicit write/stage.')

if __name__ == '__main__':
    run()
