#!/usr/bin/env python3
"""Static checks for a staged or extracted terminal archive."""
import hashlib
import json
import os
from pathlib import Path
import sys


def validate(package, runtime):
    manifest = json.loads((package / 'manifest.json').read_text())
    assert manifest['runtime'] == runtime and manifest['version'] and len(manifest['sourceRevision']) == 40
    assert not list(package.rglob('*.pdb')), 'Debug symbols remain'
    for name in ['hourglass', 'hourglass-tui']:
        assert os.access(package / 'bin' / name, os.X_OK), name
    for component, executable in [('cli', 'hourglass'), ('tui', 'hourglass-tui'), ('host', 'hourglass-host')]:
        payload = package / 'lib/hourglass' / component
        assert os.access(payload / executable, os.X_OK), executable
        assert (payload / 'libcoreclr.so').is_file(), 'Missing self-contained runtime'
        assert len(list((payload / 'Assets/Sounds').glob('*.wav'))) == 3, 'Missing sound assets'
        assert not list(payload.glob('Avalonia*')), 'Avalonia leaked into terminal archive'
        if component != 'tui':
            assert not list(payload.glob('Terminal.Gui*')), 'TUI dependency leaked into CLI/Host'
        config = json.loads((payload / (executable + '.runtimeconfig.json')).read_text())
        assert 'includedFrameworks' in config['runtimeOptions'], 'Framework-dependent publish'
    notices = package / 'licenses'
    assert (notices / 'LICENSE.md').is_file()
    for name in ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']:
        assert (notices / ('Microsoft.NETCore.App.Runtime.' + runtime) / name).is_file()
    audit = json.loads((notices / 'dependencies.json').read_text())
    assert audit['runtime'] == runtime and audit['packages']
    for identity, evidence in audit['packages'].items():
        text = notices / identity / 'LICENSE.txt'
        assert hashlib.sha256(text.read_bytes()).hexdigest() == evidence['sha256'], identity
        for supplied in evidence['suppliedNotices']:
            assert (notices / identity / 'package-notices' / supplied).is_file()
    print(f'Terminal {runtime} package contents, dependency isolation, runtime, sounds and notices passed.')


if __name__ == '__main__':
    validate(Path(sys.argv[1]), sys.argv[2])
