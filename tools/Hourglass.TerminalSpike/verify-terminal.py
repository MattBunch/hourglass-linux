#!/usr/bin/env python3
"""Bounded Linux PTY smoke check. Run after building the spike in Release."""
import fcntl
import json
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


def check(hz, fail, editor=False):
    master, slave = pty.openpty()
    before = termios.tcgetattr(slave)
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))
    dll = Path(__file__).parent / "bin/Release/net10.0/Hourglass.TerminalSpike.dll"
    with tempfile.TemporaryDirectory(prefix="hourglass-spike-") as directory:
        evidence = Path(directory) / "evidence.json"
        args = ["dotnet", str(dll), "--hz", str(hz), "--evidence", str(evidence)]
        if fail:
            args.append("--throw")
        if editor:
            args.append("--editor")
        process = subprocess.Popen(args, stdin=slave, stdout=slave, stderr=slave,
                                   start_new_session=True, env={**os.environ, "TERM": "xterm-256color"})
        output = bytearray()
        started = time.monotonic()
        # Separate Escape from subsequent keys so it cannot become their Alt prefix.
        actions = [(2, b" "), (2.5, b"\x1b"), (3, b"\x10"), (3.5, b"\x13"),
                   (4, b"\x12"), (4.5, b"\t"), (5, b"\x1b[Z"),
                   (5.5, b"\x1b\r"), (6, b"\r"), (7, b"q")]
        if editor:
            actions = [(2, b"qsren tea"), (3, b"\x11")]
        resized = False
        try:
            while process.poll() is None:
                elapsed = time.monotonic() - started
                if elapsed > 15:
                    raise AssertionError("Terminal spike exceeded its deadline")
                if not fail and actions and elapsed >= actions[0][0]:
                    os.write(master, actions.pop(0)[1])
                if not fail and not resized and elapsed >= 6.5:
                    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 12, 40, 0, 0))
                    os.kill(process.pid, signal.SIGWINCH)
                    resized = True
                if select.select([master], [], [], 0.05)[0]:
                    output.extend(os.read(master, 65536))
            drain_deadline = time.monotonic() + 1
            while time.monotonic() < drain_deadline and select.select([master], [], [], 0)[0]:
                chunk = os.read(master, 65536)
                if not chunk:
                    break
                output.extend(chunk)
            assert process.returncode == (70 if fail else 0), process.returncode
            assert termios.tcgetattr(slave) == before, "Terminal attributes changed"
            result = json.loads(evidence.read_text())
            assert result["updates"] > 0
            modes = {}
            for match in re.finditer(rb"\x1b\[\?([0-9;]+)([hl])", output):
                for mode in match[1].split(b";"):
                    modes[int(mode)] = match[2] == b"h"
            for mode in (1000, 1002, 1003, 1006, 1049):
                assert not modes.get(mode, False), ("Mode left enabled", mode)
            assert modes.get(25, True), "Cursor left hidden"
            if editor:
                assert result["editedText"] == "qsren tea", result
            elif not fail:
                for key in ("Space", "Esc", "Ctrl+P", "Ctrl+S", "Ctrl+R", "Tab", "Shift+Tab", "q"):
                    assert key in result["keys"], (key, result["keys"])
                assert len(result["sizes"]) >= 2, result["sizes"]
            else:
                assert result["failure"] == "Intentional DEV-18 spike failure."
            print(json.dumps({"hz": hz, "exception": fail, "keys": result["keys"],
                              "terminalRestored": True, "updates": result["updates"]}), flush=True)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
            os.close(master)
            os.close(slave)


for frequency in (4, 5, 10):
    check(frequency, False)
check(10, True)
check(5, False, editor=True)
