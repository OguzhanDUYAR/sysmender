"""Bounded, cancellable execution of fixed argv arrays."""
from __future__ import annotations

import os
import re
import signal
import subprocess
import threading
import time
from dataclasses import dataclass
from typing import Optional, Sequence

MAX_OUTPUT = 196608
CONTROL = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]|[\x00-\x08\x0b-\x1f\x7f\u202a-\u202e\u2066-\u2069]")


def clean(text: str) -> str:
    return CONTROL.sub("", text.replace("\r\n", "\n").replace("\r", "\n"))


def environment() -> dict:
    keys = ("HOME", "USER", "LOGNAME", "DISPLAY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR",
            "DBUS_SESSION_BUS_ADDRESS", "XAUTHORITY", "HYPRLAND_INSTANCE_SIGNATURE", "SWAYSOCK",
            "TERM", "SystemRoot", "WINDIR")
    env = {key: os.environ[key] for key in keys if key in os.environ}
    env.update(LC_ALL="C", LANG="C", PATH="/usr/sbin:/usr/bin:/sbin:/bin:/run/current-system/sw/bin:/nix/var/nix/profiles/default/bin", SYSTEMD_PAGER="cat",
               PAGER="cat", SYSTEMD_COLORS="0")
    return env


@dataclass(frozen=True)
class CommandResult:
    code: Optional[int]
    output: str
    reason: str = ""
    truncated: bool = False


def _signal_group(proc: subprocess.Popen, force: bool) -> None:
    try:
        if os.name == "posix":
            os.killpg(proc.pid, signal.SIGKILL if force else signal.SIGTERM)
        elif proc.poll() is None:
            (proc.kill if force else proc.terminate)()
    except (OSError, ProcessLookupError):
        pass


def execute(argv: Sequence[str], timeout: float = 15.0, stop: Optional[threading.Event] = None,
            cap: int = MAX_OUTPUT) -> CommandResult:
    if (not argv or not os.path.isabs(argv[0]) or not 0 < timeout <= 180 or
            not 0 < cap <= MAX_OUTPUT or any(not isinstance(arg, str) or "\0" in arg for arg in argv)):
        raise ValueError("Invalid bounded command")
    if stop and stop.is_set():
        return CommandResult(None, "", "cancelled")
    try:
        proc = subprocess.Popen(list(argv), stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, shell=False, cwd=os.path.abspath(os.sep),
                                env=environment(), start_new_session=os.name == "posix")
    except OSError as error:
        return CommandResult(None, clean(str(error)), "start")
    output = bytearray()
    truncated = False
    lock = threading.Lock()

    def drain() -> None:
        nonlocal truncated
        try:
            while True:
                block = proc.stdout.read1(16384)
                if not block:
                    return
                with lock:
                    space = cap - len(output)
                    output.extend(block[:space])
                    truncated |= len(block) > space
        except (OSError, ValueError):
            pass
        finally:
            proc.stdout.close()

    reader = threading.Thread(target=drain, daemon=True)
    reader.start()
    end = time.monotonic() + timeout
    reason = ""
    while proc.poll() is None:
        if stop and stop.is_set():
            reason = "cancelled"
            break
        if time.monotonic() >= end:
            reason = "timeout"
            break
        time.sleep(0.045)
    if reason:
        _signal_group(proc, False)
        try:
            proc.wait(timeout=0.4)
        except subprocess.TimeoutExpired:
            _signal_group(proc, True)
            try:
                proc.wait(timeout=1)
            except subprocess.TimeoutExpired:
                pass
    reader.join(timeout=0.6)
    if reader.is_alive():
        _signal_group(proc, True)
        reader.join(timeout=0.3)
    with lock:
        data = bytes(output)
        limited = truncated or reader.is_alive()
    return CommandResult(proc.poll(), clean(data.decode("utf-8", errors="replace")), reason, limited)

