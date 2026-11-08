#!/usr/bin/env python3
"""Deterministic packaging rejection fixtures; no subprocesses or delays."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import sys
sys.dont_write_bytecode = True
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('packaging_validator', ROOT / 'scripts/validate-terminal-packaging.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='hourglass-package-fixture-')
        self.addCleanup(self.directory.cleanup)
        self.package = Path(self.directory.name)
        def write(name, text='fixture', executable=False):
            path = self.package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
            if executable: path.chmod(0o755)
        self.write = write
        write('manifest.json', json.dumps({'version': '0.2.0', 'runtime': 'linux-x64', 'sourceRevision': 'a' * 40}))
        for component, executable in [('cli', 'hourglass'), ('tui', 'hourglass-tui'), ('host', 'hourglass-host')]:
            root = 'lib/hourglass/' + component + '/'
            write(root + executable, executable=True)
            write(root + 'libcoreclr.so')
            write(root + executable + '.runtimeconfig.json', json.dumps({'runtimeOptions': {'includedFrameworks': []}}))
            for index in range(3): write(root + 'Assets/Sounds/' + str(index) + '.wav')
        for name in ['hourglass', 'hourglass-tui']: write('bin/' + name, executable=True)
        write('licenses/LICENSE.md')
        for name in ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']: write('licenses/Microsoft.NETCore.App.Runtime.linux-x64/' + name)
        write('licenses/Example/1.0/LICENSE.txt')
        write('licenses/dependencies.json', json.dumps({'runtime': 'linux-x64', 'packages': {'Example/1.0': {'sha256': hashlib.sha256(b'fixture').hexdigest(), 'suppliedNotices': []}}}))

    def validate(self): validator.validate(self.package, 'linux-x64')
    def test_complete_fixture(self): self.validate()
    def test_missing_sound(self):
        (self.package / 'lib/hourglass/tui/Assets/Sounds/0.wav').unlink()
        with self.assertRaises(AssertionError): self.validate()
    def test_toolkit_leak(self):
        self.write('lib/hourglass/host/Terminal.Gui.dll')
        with self.assertRaises(AssertionError): self.validate()
    def test_changed_notice(self):
        self.write('licenses/Example/1.0/LICENSE.txt', 'changed')
        with self.assertRaises(AssertionError): self.validate()
    def test_missing_runtime(self):
        (self.package / 'lib/hourglass/cli/libcoreclr.so').unlink()
        with self.assertRaises(AssertionError): self.validate()
    def test_wrong_architecture(self):
        with self.assertRaises(AssertionError): validator.validate(self.package, 'linux-arm64')


if __name__ == '__main__': unittest.main()
