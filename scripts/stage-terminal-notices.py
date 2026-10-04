#!/usr/bin/env python3
"""Stage pinned terminal dependency licenses from resolved NuGet assets."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PINNED = {
    'system.commandline': ('2.0.0', 'system.commandline'),
    'tmds.dbus.protocol': ('0.92.0', 'tmds.dbus.protocol'),
    'terminal.gui': ('2.5.0', 'terminal.gui'),
    'colorhelper': ('1.8.1', 'colorhelper'),
    'jetbrains.annotations': ('2026.2.0', 'jetbrains.annotations'),
    'markdig': ('1.3.2', 'markdig'),
    'onigwrap': ('1.0.11', 'onigwrap'),
    'system.io.abstractions': ('22.2.0', 'testableio'),
    'testableio.system.io.abstractions': ('22.2.0', 'testableio'),
    'testableio.system.io.abstractions.wrappers': ('22.2.0', 'testableio'),
    'testably.abstractions.filesystem.interface': ('10.3.0', 'testably'),
    'textmatesharp': ('2.0.4', 'textmatesharp'),
    'textmatesharp.grammars': ('2.0.4', 'textmatesharp'),
    'wcwidth': ('4.0.1', 'wcwidth'),
}


def stage(destination, runtime):
    sources_dir = ROOT / 'packaging/terminal/licenses'
    sources = json.loads((sources_dir / 'sources.json').read_text())
    notices = destination / 'licenses'
    notices.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / 'LICENSE.md', notices / 'LICENSE.md')
    package_root = Path(subprocess.check_output(['dotnet', 'nuget', 'locals', 'global-packages', '--list'], text=True).strip().split(': ', 1)[1])
    packages = {}
    for project in ['Hourglass.Cli', 'Hourglass.Tui', 'Hourglass.Host']:
        assets = json.loads((ROOT / 'src' / project / 'obj/project.assets.json').read_text())
        for identity, library in assets['libraries'].items():
            if library['type'] != 'package':
                continue
            name, version = identity.lower().split('/')
            if name.startswith('microsoft.extensions.'):
                expected = '10.0.7' if name == 'microsoft.extensions.logging.abstractions' else '10.0.11'
                source = 'dotnet'
            else:
                expected, source = PINNED[name]  # Unknown dependencies require a notice audit.
            if version != expected:
                raise ValueError(f'Unaudited dependency version: {identity}')
            target = notices / identity
            target.mkdir(parents=True, exist_ok=True)
            license_file = sources_dir / (source + '.txt')
            evidence = sources[source]
            if hashlib.sha256(license_file.read_bytes()).hexdigest() != evidence['sha256']:
                raise ValueError(f'Changed license evidence: {source}')
            shutil.copy2(license_file, target / 'LICENSE.txt')
            supplied = []
            for file in (package_root / library['path']).rglob('*'):
                if file.is_file() and ('license' in file.name.lower() or 'notice' in file.name.lower()) and file.suffix.lower() != '.nuspec':
                    relative = file.relative_to(package_root / library['path'])
                    copied = target / 'package-notices' / relative
                    copied.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(file, copied)
                    supplied.append(str(relative))
            packages[identity] = {**evidence, 'suppliedNotices': supplied}
    config = json.loads((destination / 'lib/hourglass/host/hourglass-host.runtimeconfig.json').read_text())
    framework = next(item for item in config['runtimeOptions']['includedFrameworks'] if item['name'] == 'Microsoft.NETCore.App')
    runtime_package = package_root / ('microsoft.netcore.app.runtime.' + runtime) / framework['version']
    runtime_notices = notices / ('Microsoft.NETCore.App.Runtime.' + runtime)
    runtime_notices.mkdir(parents=True, exist_ok=True)
    for name in ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']:
        shutil.copy2(runtime_package / name, runtime_notices / name)
    (notices / 'dependencies.json').write_text(json.dumps({'runtime': runtime, 'runtimeVersion': framework['version'], 'packages': packages}, indent=2) + '\n')


if __name__ == '__main__':
    stage(Path(sys.argv[1]), sys.argv[2])
