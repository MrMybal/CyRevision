"""Verify the vendored CyUI snapshot and the native dependency boundary (no network)."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def run(build=None):
    lock = json.loads((ROOT / 'native/cyui.lock.json').read_text(encoding='utf-8'))
    vendor = ROOT / 'native/vendor/CyUI'
    expected = lock['files']
    actual = {path.relative_to(vendor).as_posix() for path in vendor.rglob('*') if path.is_file()}
    assert actual == set(expected), 'CyUI snapshot files do not match the lock'
    for relative, digest in expected.items():
        assert hashlib.sha256((vendor / relative).read_bytes()).hexdigest() == digest, relative
    application = ROOT / 'native/CyRevision.Native'
    for path in list(application.glob('*.cpp')) + list(application.glob('*.h')):
        text = path.read_text(encoding='utf-8-sig')
        assert 'cyui/CyUI.h' not in text, f'Umbrella include in {path.name}'
    assert not (application / 'TextDocument.cpp').exists(), 'The superseded lexer/diff model is still present'
    assert 'CyUI::CyUI' not in (application / 'CMakeLists.txt').read_text(encoding='utf-8')
    if build:
        cache = {}
        for line in (build / 'CMakeCache.txt').read_text(encoding='utf-8').splitlines():
            if line and not line.startswith(('#', '//')) and '=' in line and ':' in line.split('=', 1)[0]:
                key, value = line.split('=', 1)
                cache[key.split(':', 1)[0]] = value
        assert cache['CYUI_IMGUI_TARGET'] == 'cyrevision_imgui'
        assert set(cache['CYUI_MODULES'].split(';')) == {'Code', 'Diff', 'CodeEditor', 'Inspector', 'DocumentViews', 'Images'}
        for key in ('CYUI_BUILD_AUDIO', 'CYUI_BUILD_DEMO', 'CYUI_BUILD_WEB_CEF'):
            assert cache[key] == 'OFF', key
        assert cache['CYUI_BUILD_GALLERY'] == 'ON', 'The existing live theme editor was removed'
        projects = {path.stem for path in build.rglob('*.vcxproj')}
        assert len([name for name in projects if 'imgui' in name.lower()]) == 1
        assert not projects.intersection({'cyui_audio', 'cyui_demo', 'cyui_webcef', 'cyui_web', 'cyui_nodes', 'cyui_plots', 'cyui_viewport'})
    print(f'CyUI snapshot and module boundary passed: {lock["commit"]}, {len(expected)} unchanged upstream files.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build-dir', type=Path)
    args = parser.parse_args()
    run(args.build_dir.resolve() if args.build_dir else None)
