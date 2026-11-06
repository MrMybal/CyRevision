"""Check real CyDoc help IPC under an isolated profile, without launching a browser."""
import argparse
import json
import queue
import subprocess
import tempfile
import threading
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
FLAGS = getattr(subprocess, 'CREATE_NO_WINDOW', 0)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--engine', type=Path, default=ROOT/'artifacts/native-v2-cydoc-update/CyRevision.Desktop.exe')
    args = parser.parse_args()
    engine = args.engine.resolve()
    manifest = json.loads((engine.parent/'Documentation/DocData/manifest.json').read_text(encoding='utf-8'))
    routes = json.loads((engine.parent/'Documentation/DocData/routes.json').read_text(encoding='utf-8'))
    guides = [page['id'] for page in manifest['pages'] if page['locale'] == 'en']
    assert len(guides) == 86 and len(manifest['pages']) == 172
    assert len({page['id'] for page in manifest['pages']}) == 86
    (ROOT/'artifacts').mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='cydoc-profile-', dir=ROOT/'artifacts') as temporary:
        process = subprocess.Popen([str(engine), '--native-backend', '--profile-dir', temporary],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding='utf-8', creationflags=FLAGS)
        responses = queue.Queue()
        def read():
            for line in process.stdout:
                responses.put(json.loads(line))
        threading.Thread(target=read, daemon=True).start()
        threading.Thread(target=lambda: list(process.stderr), daemon=True).start()
        counter = 0
        def request(command, expect_ok=True, **values):
            nonlocal counter
            counter += 1
            process.stdin.write(json.dumps(dict(id=counter, command=command, **values))+'\n')
            process.stdin.flush()
            while True:
                response = responses.get(timeout=40)
                if response.get('id') == counter:
                    assert response['ok'] == expect_ok, response
                    return response.get('result')
        def check_url(result, locale, page):
            url = result['url']
            parsed = urlsplit(url)
            assert parsed.scheme == 'file' and not parsed.netloc
            local = unquote(parsed.path)
            if len(local) > 2 and local[0] == '/' and local[2] == ':':
                local = local[1:]
            assert Path(local).resolve() == (engine.parent/'Documentation/Documentation.html').resolve()
            assert parsed.fragment.startswith('/'+locale+'/'+page)
        try:
            for locale in ('en', 'fr'):
                request('app.language', language=locale)
                state = request('state', workspace='help')
                help_state = state['documentation']
                assert help_state['language'] == locale and help_state['available']
                assert help_state['reader'] == 'CyDoc' and help_state['total'] == 86
                assert help_state['modules'] == ['image', 'gallery']
                for identifier in guides:
                    check_url(request('docs.open', topicId=identifier), locale, identifier)
                for workspace, identifier in routes.items():
                    check_url(request('docs.open', topicId=workspace), locale, identifier)
                check_url(request('docs.open', topicId='doc:git-history#file-history-including-lfs',
                    mediaId='history-v2'), locale, 'git-history/file-history-including-lfs?media=history-v2')
                request('docs.open', topicId='missing-page', expect_ok=False)
                request('docs.open', topicId='getting-started', anchor='missing-section', expect_ok=False)
            request('app.language', language='en')
            check_url(request('docs.open', topicId='reference-server-api'), 'en', 'server-api')
            print('PASS: 172 localized page links, all context routes, anchors/media, fallback and error handling via real IPC; no browser launched.')
        finally:
            try:
                request('shutdown')
            finally:
                process.stdin.close()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    main()
