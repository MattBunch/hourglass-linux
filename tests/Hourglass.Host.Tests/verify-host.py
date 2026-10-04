#!/usr/bin/env python3
"""Bounded Linux process checks for host lifetime, election and crash recovery."""
import concurrent.futures
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
CLI = Path(os.environ.get("HOURGLASS_TEST_CLI", ROOT / "src/Hourglass.Cli/bin/Release/net10.0/hourglass"))
OPTIONS = ["--set", "notifications-enabled=false", "--set", "audio-alerts-enabled=false", "--set", "do-not-keep-computer-awake=true"]

def call(env, *args):
    result = subprocess.run([str(CLI), *args, "--json"], env=env, capture_output=True, timeout=15)
    assert result.returncode == 0, (args, result.returncode, result.stderr)
    return json.loads(result.stdout)["result"]

def pid(directory):
    text = (Path(directory) / "hourglass-linux/hourglass-linux.lock").read_text()
    return int(next(line[4:] for line in text.splitlines() if line.startswith("pid=")))

def alive(identifier):
    status = Path(f"/proc/{identifier}/stat")
    return status.exists() and status.read_text().split()[2] != "Z"

def wait_dead(identifier):
    deadline = time.monotonic() + 10
    while alive(identifier):
        assert time.monotonic() < deadline, "Host did not stop when idle"
        time.sleep(0.05)

with tempfile.TemporaryDirectory(prefix="hourglass-host-") as directory:
    env = {**os.environ, "XDG_CONFIG_HOME": directory, "XDG_RUNTIME_DIR": directory}
    hosts = set()
    try:
        assert not any(item["available"] for item in call(env, "doctor")["checks"] if item["name"] == "runtime-connection")
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
            results = list(executor.map(lambda index: call(env, "start", "25m", "--detach", "--title", f"Detached {index}", *OPTIONS), range(4)))
        identifier = pid(directory)
        hosts.add(identifier)
        assert alive(identifier)
        assert os.getsid(identifier) == identifier, "Host did not detach its Linux session"
        for descriptor in (0, 1, 2):
            assert os.readlink(f"/proc/{identifier}/fd/{descriptor}") == "/dev/null"
        ids = {result["sessions"][0]["sessionId"] for result in results}
        sessions = call(env, "list")["sessions"]
        assert {item["sessionId"] for item in sessions} == ids
        assert all(item["lifetime"] == "detached" for item in sessions)
        waiter = subprocess.Popen([str(CLI), "start", "25m", "--json", *OPTIONS], env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            deadline = time.monotonic() + 10
            while len(call(env, "list")["sessions"]) != 5:
                assert time.monotonic() < deadline
                time.sleep(0.05)
            waiter.send_signal(signal.SIGTERM)
            waiter.communicate(timeout=10)
            assert waiter.returncode == 130
            assert {item["sessionId"] for item in call(env, "list")["sessions"]} == ids
        finally:
            if waiter.poll() is None:
                waiter.kill()
                waiter.communicate()
        os.kill(identifier, signal.SIGKILL)
        wait_dead(identifier)
        recovered = call(env, "list")["sessions"]
        recovered_pid = pid(directory)
        hosts.add(recovered_pid)
        assert recovered_pid != identifier
        assert {item["sessionId"] for item in recovered} == ids
        selected = next(iter(ids))
        assert call(env, "pause", selected)["sessions"][0]["state"] == "paused"
        assert call(env, "resume", selected)["sessions"][0]["state"] == "running"
        for session_id in ids:
            call(env, "stop", session_id)
            call(env, "dismiss", session_id)
        wait_dead(recovered_pid)
        assert not any(item["available"] for item in call(env, "doctor")["checks"] if item["name"] == "runtime-connection")
    finally:
        for identifier in hosts:
            if alive(identifier):
                os.kill(identifier, signal.SIGTERM)
                wait_dead(identifier)

print("Host election, detached lifetime, descriptor isolation, foreground cancellation, crash recovery and idle shutdown passed.")
