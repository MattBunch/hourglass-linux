#!/usr/bin/env python3
"""Bounded process smoke checks for the built CLI control protocol."""

import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
CLI = Path(os.environ.get("HOURGLASS_TEST_CLI", ROOT / "src/Hourglass.Cli/bin/Release/net10.0/hourglass"))
OPTIONS = ["--set", "notifications-enabled=false", "--set", "audio-alerts-enabled=false",
           "--set", "do-not-keep-computer-awake=true"]


def wait_ready(owner, directory):
    deadline = time.monotonic() + 10
    endpoint = Path(directory) / "hourglass-linux/hourglass-control.sock"
    while not endpoint.exists():
        assert owner.poll() is None, owner.communicate()
        assert time.monotonic() < deadline, "Control endpoint did not become ready"
        time.sleep(0.05)


def call(env, *arguments):
    result = subprocess.run([str(CLI), *arguments, "--json"], env=env,
                            capture_output=True, timeout=15)
    assert result.returncode == 0, (arguments, result.returncode, result.stderr)
    return json.loads(result.stdout)["result"]


with tempfile.TemporaryDirectory(prefix="hourglass-control-") as directory:
    env = {**os.environ, "XDG_CONFIG_HOME": directory, "XDG_RUNTIME_DIR": directory}
    owner = subprocess.Popen([str(CLI), "start", "25m", "--title", "Owner", "--json", *OPTIONS],
                             env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    waiter = None
    try:
        wait_ready(owner, directory)
        deadline = time.monotonic() + 10
        while not (sessions := call(env, "list")["sessions"]):
            assert owner.poll() is None, owner.communicate()
            assert time.monotonic() < deadline, "Foreground session did not become ready"
            time.sleep(0.05)
        session = sessions[0]
        identifier = session["sessionId"]
        assert call(env, "pause", identifier)["sessions"][0]["state"] == "paused"
        assert call(env, "resume", identifier)["sessions"][0]["state"] == "running"
        checks = call(env, "doctor")["checks"]
        assert any(check["name"] == "runtime-connection" and check["available"] for check in checks)
        # The command waits longer than a single five-second protocol request deadline.
        waiter = subprocess.Popen([str(CLI), "start", "7s", "--json", *OPTIONS],
                                  env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        output, error = waiter.communicate(timeout=15)
        assert waiter.returncode == 0, (waiter.returncode, error)
        assert json.loads(output)["result"]["outcome"] == "expired"
        assert [item["sessionId"] for item in call(env, "list")["sessions"]] == [identifier]
        assert call(env, "stop", identifier)["sessions"][0]["state"] == "stopped"
        output, error = owner.communicate(timeout=10)
        assert owner.returncode == 0, (owner.returncode, error)
        assert json.loads(output)["result"]["outcome"] == "stopped"
    finally:
        for process in (waiter, owner):
            if process is not None and process.poll() is None:
                process.send_signal(signal.SIGTERM)
                process.communicate(timeout=10)

print("CLI shared lifecycle, negotiated diagnostics, long foreground wait and durable owner shutdown passed.")
