#!/usr/bin/env python3
"""Execute an extracted terminal release on its native architecture."""
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
package = Path(sys.argv[1]).resolve()
runtime = sys.argv[2]
expected = {'linux-x64': 'x86_64', 'linux-arm64': 'aarch64'}[runtime]
assert platform.machine() == expected, 'Package execution requires its native architecture'
manifest = json.loads((package / 'manifest.json').read_text())
assert manifest['runtime'] == runtime
version = manifest['version']
cli = package / 'bin/hourglass'
tui = package / 'bin/hourglass-tui'
host = package / 'lib/hourglass/host/hourglass-host'
# Ignore developer .NET installations; published apphosts must remain self-contained.
with tempfile.TemporaryDirectory(prefix='hourglass-package-') as data:
    env = {**os.environ, 'DOTNET_ROOT': '/missing-dotnet-runtime', 'DOTNET_ROOT_X64': '/missing-dotnet-runtime',
           'DOTNET_ROOT_ARM64': '/missing-dotnet-runtime', 'XDG_CONFIG_HOME': data, 'XDG_RUNTIME_DIR': data,
           'HOURGLASS_TEST_CLI': str(cli), 'HOURGLASS_TEST_TUI': str(tui)}
    for executable, args in [(cli, ['version', '--plain']), (tui, ['--version']), (host, ['--version'])]:
        result = subprocess.run([str(executable), *args], env=env, cwd=data, capture_output=True, text=True, timeout=15)
        assert result.returncode == 0 and result.stdout.strip() == version, (executable, result.stdout, result.stderr)
    for executable in [cli, tui, host]:
        result = subprocess.run([str(executable), '--help'], env=env, cwd=data, capture_output=True, timeout=15)
        assert result.returncode == 0 and result.stdout
    result = subprocess.run([str(cli), 'start', 'invalid expression', '--json'], env=env, cwd=data, capture_output=True, timeout=15)
    assert result.returncode == 2 and not result.stdout and json.loads(result.stderr)['error']
    for script in ['tests/Hourglass.Cli.Tests/verify-control.py', 'tests/Hourglass.Host.Tests/verify-host.py',
                   'tests/Hourglass.Tui.Tests/verify-terminal.py']:
        subprocess.run([sys.executable, str(ROOT / script)], env=env, cwd=data, check=True, timeout=240)
print(f'Extracted {runtime} launchers, versions, CLI/Host lifetimes and PTY workflows passed.')
