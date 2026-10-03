#!/usr/bin/env python3
"""Bounded X11 GUI lifetime smoke; uses isolated application data."""
import ctypes as C
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "src/Hourglass.Cli/bin/Release/net10.0/hourglass"
GUI = ROOT / "src/Hourglass.Linux.Avalonia/bin/Release/net10.0/hourglass-linux"
OPTIONS = ["--set", "notifications-enabled=false", "--set", "audio-alerts-enabled=false", "--set", "do-not-keep-computer-awake=true"]
if not os.environ.get("DISPLAY"):
    raise SystemExit("Not run: GUI lifetime smoke requires an X11 display.")
x = C.CDLL("libX11.so.6")
x.XOpenDisplay.argtypes = [C.c_char_p]; x.XOpenDisplay.restype = C.c_void_p
x.XDefaultRootWindow.argtypes = [C.c_void_p]; x.XDefaultRootWindow.restype = C.c_ulong
x.XInternAtom.argtypes = [C.c_void_p, C.c_char_p, C.c_int]; x.XInternAtom.restype = C.c_ulong
x.XQueryTree.argtypes = [C.c_void_p, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.POINTER(C.c_ulong)), C.POINTER(C.c_uint)]
x.XGetWindowProperty.argtypes = [C.c_void_p, C.c_ulong, C.c_ulong, C.c_long, C.c_long, C.c_int, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_int), C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.c_void_p)]
x.XFree.argtypes = [C.c_void_p]
x.XFlush.argtypes = [C.c_void_p]
x.XCloseDisplay.argtypes = [C.c_void_p]
class MessageData(C.Union):
    _fields_ = [("l", C.c_long * 5)]
class Message(C.Structure):
    _fields_ = [("type", C.c_int), ("serial", C.c_ulong), ("send_event", C.c_int), ("display", C.c_void_p), ("window", C.c_ulong), ("message_type", C.c_ulong), ("format", C.c_int), ("data", MessageData)]
class Event(C.Union):
    _fields_ = [("message", Message), ("padding", C.c_long * 24)]
x.XSendEvent.argtypes = [C.c_void_p, C.c_ulong, C.c_int, C.c_long, C.POINTER(Event)]
display = x.XOpenDisplay(None)
assert display, "Could not connect to X11 display"
root = x.XDefaultRootWindow(display)
pid_atom = x.XInternAtom(display, b"_NET_WM_PID", 0)
protocol_atom = x.XInternAtom(display, b"WM_PROTOCOLS", 0)
close_atom = x.XInternAtom(display, b"WM_DELETE_WINDOW", 0)

def windows(pid):
    found = []
    pending = [root]
    while pending:
        window = pending.pop()
        actual = C.c_ulong(); format = C.c_int(); count = C.c_ulong(); after = C.c_ulong(); data = C.c_void_p()
        if x.XGetWindowProperty(display, window, pid_atom, 0, 1, 0, 0, C.byref(actual), C.byref(format), C.byref(count), C.byref(after), C.byref(data)) == 0 and data.value:
            value = C.cast(data, C.POINTER(C.c_ulong))[0] if count.value and format.value == 32 else 0
            x.XFree(data)
            if value == pid: found.append(window)
        r = C.c_ulong(); parent = C.c_ulong(); children = C.POINTER(C.c_ulong)(); length = C.c_uint()
        if x.XQueryTree(display, window, C.byref(r), C.byref(parent), C.byref(children), C.byref(length)):
            pending.extend(children[index] for index in range(length.value))
            if children: x.XFree(children)
    return found

def close_windows(pid):
    for window in windows(pid):
        event = Event()
        event.message = Message(33, 0, 1, display, window, protocol_atom, 32, MessageData())
        event.message.data.l[0] = close_atom
        x.XSendEvent(display, window, 0, 0, C.byref(event))
    x.XFlush(display)

def wait(predicate, description):
    deadline = time.monotonic() + 15
    while not predicate():
        assert time.monotonic() < deadline, description
        time.sleep(0.1)

def call(env, *args):
    result = subprocess.run([str(CLI), *args, "--json"], env=env, capture_output=True, timeout=15)
    assert result.returncode == 0, (args, result.returncode, result.stderr)
    return json.loads(result.stdout)["result"]

with tempfile.TemporaryDirectory(prefix="hourglass-gui-lifetime-") as directory:
    env = {**os.environ, "XDG_CONFIG_HOME": directory, "XDG_RUNTIME_DIR": directory}
    for key, value in (("notifications-enabled", "false"), ("audio-alerts-enabled", "false"), ("do-not-keep-computer-awake", "true"), ("prompt-on-exit", "false")):
        call(env, "config", "set", key, value)
    processes = []
    def launch(*args):
        log = open(Path(directory) / f"gui-{len(processes)}.log", "wb")
        process = subprocess.Popen([str(GUI), *args], env=env, stdout=log, stderr=log)
        log.close()
        processes.append(process)
        return process
    try:
        primary = launch("-t", "GUI authority", "25m")
        wait(lambda: bool(windows(primary.pid)), "GUI did not open")
        sessions = call(env, "list")["sessions"]
        assert len(sessions) == 1
        identifier = sessions[0]["sessionId"]
        call(env, "detach", identifier)
        close_windows(primary.pid)
        wait(lambda: not windows(primary.pid), "Detached GUI view did not close")
        assert primary.poll() is None, "GUI authority exited with a detached timer"
        assert [item["sessionId"] for item in call(env, "list")["sessions"]] == [identifier]
        activate = launch()
        assert activate.wait(timeout=10) == 0
        wait(lambda: bool(windows(primary.pid)), "GUI authority could not reopen its detached view")
        assert [item["sessionId"] for item in call(env, "list")["sessions"]] == [identifier]
        close_windows(primary.pid)
        wait(lambda: not windows(primary.pid), "Reopened detached view did not close")
        call(env, "stop", identifier); call(env, "dismiss", identifier)
        assert primary.wait(timeout=15) == 0

        detached = call(env, "start", "25m", "--detach", *OPTIONS)["sessions"][0]["sessionId"]
        attached = launch("-t", "Recover GUI", "25m")
        wait(lambda: bool(windows(attached.pid)), "GUI could not attach to host")
        sessions = call(env, "list")["sessions"]
        ordinary = next(item["sessionId"] for item in sessions if item["sessionId"] != detached)
        attached.kill(); attached.wait(timeout=10)
        wait(lambda: all(item["sessionId"] != ordinary for item in call(env, "list")["sessions"]), "GUI disconnect did not defer recovery")
        extra = call(env, "start", "25m", "--detach", *OPTIONS)["sessions"][0]["sessionId"]
        reopened = launch()
        wait(lambda: bool(windows(reopened.pid)), "GUI recovery did not open")
        wait(lambda: ordinary in [item["sessionId"] for item in call(env, "list")["sessions"]], "GUI recovery lost its ID")
        wait(lambda: len(windows(reopened.pid)) >= 3, "GUI did not finish opening recovered views")
        close_windows(reopened.pid)
        assert reopened.wait(timeout=15) == 0
        remaining = {item["sessionId"] for item in call(env, "list")["sessions"]}
        assert remaining == {detached, extra}, remaining
        for identifier in remaining:
            call(env, "stop", identifier); call(env, "dismiss", identifier)
    finally:
        for process in processes:
            if process.poll() is None: process.terminate(); process.wait(timeout=10)
        # Stop the isolated host before its application directory is removed.
        lock = Path(directory) / "hourglass-linux/hourglass-linux.lock"
        if lock.exists():
            identifier = int(next(line[4:] for line in lock.read_text().splitlines() if line.startswith("pid=")))
            try: os.kill(identifier, signal.SIGTERM)
            except ProcessLookupError: pass
x.XCloseDisplay(display)
print("GUI-owned detached lifetime, reopen without replacement engines, host attachment, crash recovery and ordinary close passed.")
