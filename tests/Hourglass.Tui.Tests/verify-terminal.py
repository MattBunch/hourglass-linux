#!/usr/bin/env python3
"""Bounded PTY smoke test for the built Hourglass TUI."""

import fcntl
import os
from pathlib import Path
import pty
import re
import select
import signal
import struct
import subprocess
import tempfile
import termios
import time


ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/Hourglass.Tui/bin/Release/net10.0/hourglass-tui.dll"
CLI = ROOT / "src/Hourglass.Cli/bin/Release/net10.0/hourglass"


def check(actions, size=(80, 24), expected=0, resize=None, fail=False, terminate_at=None, via_cli=False):
    master, slave = pty.openpty()
    before = termios.tcgetattr(slave)
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", size[1], size[0], 0, 0))
    output = bytearray()
    with tempfile.TemporaryDirectory(prefix="hourglass-tui-") as directory:
        env = {**os.environ, "TERM": "xterm-256color", "XDG_CONFIG_HOME": directory,
               "XDG_RUNTIME_DIR": directory}
        if fail:
            env["HOURGLASS_TUI_TEST_THROW"] = "1"
        command = [str(CLI), "tui"] if via_cli else ["dotnet", str(DLL)]
        process = subprocess.Popen(command, stdin=slave, stdout=slave, stderr=slave,
                                   start_new_session=True, env=env)
        started = time.monotonic()
        resized = False
        terminated = False
        try:
            while process.poll() is None:
                elapsed = time.monotonic() - started
                if elapsed > 18:
                    raise AssertionError("TUI exceeded PTY deadline: " + output[-600:].decode(errors="replace"))
                if actions and elapsed >= actions[0][0]:
                    os.write(master, actions.pop(0)[1])
                if resize and not resized and elapsed >= resize[0]:
                    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", resize[2], resize[1], 0, 0))
                    os.kill(process.pid, signal.SIGWINCH)
                    resized = True
                if terminate_at and not terminated and elapsed >= terminate_at:
                    os.kill(process.pid, signal.SIGTERM)
                    terminated = True
                if select.select([master], [], [], 0.05)[0]:
                    try:
                        output.extend(os.read(master, 65536))
                    except OSError:
                        break
            assert process.returncode == expected, (process.returncode, output[-800:].decode(errors="replace"))
            assert termios.tcgetattr(slave) == before, "Terminal attributes changed"
            modes = {}
            for match in re.finditer(rb"\x1b\[\?([0-9;]+)([hl])", output):
                for mode in match[1].split(b";"):
                    modes[int(mode)] = match[2] == b"h"
            for mode in (1000, 1002, 1003, 1006, 1049):
                assert not modes.get(mode, False), ("Mode left enabled", mode)
            assert modes.get(25, True), "Cursor left hidden"
            assert b"Hourglass" in output, "TUI never rendered"
            if fail:
                assert b"Intentional TUI test failure." in output
            return output
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
            os.close(master)
            os.close(slave)


check([(1, b"q")])
check([(1, b"q")], via_cli=True)
help_screen = check([(1, b"?"), (2, b"\x1b"), (2.5, b"q")])
assert b"Ctrl+P pause or resume" in help_screen, help_screen[-800:].decode(errors="replace")
tiny = check([(1, b"?"), (2, b"\x1b"), (2.5, b"q")], size=(30, 10))
assert b"Terminal too small" in tiny, tiny[-800:].decode(errors="replace")
check([(1, b"n"), (1.6, b"0 seconds"), (2.2, b"\t"), (2.8, b"Tea"),
       (3.4, b"\r"), (5, b"q")], resize=(4, 40, 12))
workflow = check([(1, b"n"), (1.4, b"25 minutes"), (1.8, b"\t"), (2.2, b"Focus"),
                  (2.6, b"\r"), (3.2, b" "), (3.8, b"\x10"), (4.4, b"r"),
                  (5, b"s"), (5.6, b"e"), (6.2, b"\x1b"), (6.8, b"n"),
                  (7.2, b"0 seconds"), (7.6, b"\t"), (8, b"Break"),
                  (8.4, b"\r"), (9, b"\t"), (9.5, b"\x1b[Z"),
                  (10, b"q"), (10.5, b"y")])
for text in (b"Focus", b"Break", b"Paused", b"Stopped"):
    assert text in workflow, (text, workflow[-800:].decode(errors="replace"))
editor = check([(1, b"n"), (1.5, b"qsren tea"), (2, b"\r"), (3, b"\x1b"),
                (3.5, b"q")])
assert b"Enter a valid current timer" in editor, editor[-800:].decode(errors="replace")
parity = check([(1, b"n"), (1.5, b"5 minutes"), (2, b"\t"), (2.5, b"Focus"),
                (3, b"\r"), (4, b"v"), (4.5, b"a"), (5, b"\r"),
                (5.5, b"A"), (6.5, b","), (7, b"\x1b[B"), (7.5, b"\r"),
                (8, b"\x1b"), (8.5, b"c"), (9, b"\r"), (9.5, b"\x1b"),
                (10, b"o"), (10.5, b"\x1b"), (11, b"q"), (11.5, b"y")])
for text in (b"Saved", b"Settings", b"Recent", b"Options"):
    assert text in parity, (text, parity[-800:].decode(errors="replace"))
check([], expected=130, terminate_at=1)
check([], expected=1, fail=True)
print("TUI PTY lifecycle, catalog/settings menus, resize, quit and terminal restoration passed.")
