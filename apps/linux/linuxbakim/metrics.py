"""Linux counters. Missing or reset counters never become invented zeroes."""
from __future__ import annotations

import re
import time
from pathlib import Path
from typing import Dict, Optional, Tuple

from .host import read_text


def cpu_counters(text: str) -> Optional[Tuple[int, int]]:
    for line in text.splitlines():
        fields = line.split()
        if fields and fields[0] == "cpu":
            try:
                values = [int(value) for value in fields[1:9]]
                if len(values) < 4 or min(values) < 0:
                    return None
                idle = values[3] + (values[4] if len(values) > 4 else 0)
                return sum(values), idle
            except ValueError:
                return None
    return None


def cpu_percent(old: Optional[tuple], new: Optional[tuple]) -> Optional[float]:
    if old is None or new is None:
        return None
    total, idle = new[0] - old[0], new[1] - old[1]
    return 100.0 * (total - idle) / total if total > 0 and 0 <= idle <= total else None


def memory_values(text: str) -> dict:
    fields = {}
    for line in text.splitlines():
        match = re.fullmatch(r"(MemTotal|MemAvailable|SwapTotal|SwapFree):\s+(\d+)\s+kB", line.strip())
        if match:
            fields[match[1]] = int(match[2]) * 1024
    total, available = fields.get("MemTotal"), fields.get("MemAvailable")
    swap, free = fields.get("SwapTotal"), fields.get("SwapFree")
    return {"memory_total": total, "memory_used": total - available if total and available is not None and available <= total else None,
            "swap_total": swap, "swap_used": swap - free if swap is not None and free is not None and free <= swap else None}


def net_counters(text: str) -> Dict[str, Tuple[int, int]]:
    result = {}
    for line in text.splitlines():
        device, sep, numbers = line.partition(":")
        parts = numbers.split()
        name = device.strip()
        if sep and name and name != "lo" and len(parts) >= 16:
            try:
                rx, tx = int(parts[0]), int(parts[8])
                if rx >= 0 and tx >= 0:
                    result[name] = (rx, tx)
            except ValueError:
                continue
    return result


def disk_counters(text: str, devices: set) -> Dict[str, Tuple[int, int]]:
    result = {}
    for line in text.splitlines():
        fields = line.split()
        if len(fields) > 9 and fields[2] in devices:
            try:
                read, written = int(fields[5]), int(fields[9])
                if read >= 0 and written >= 0:
                    result[fields[2]] = (read * 512, written * 512)
            except ValueError:
                continue
    return result


def rates(old: dict, new: dict, seconds: float) -> Optional[Tuple[float, float]]:
    if not old or old.keys() != new.keys() or not 0 < seconds <= 20:
        return None
    changes = [(new[key][0] - value[0], new[key][1] - value[1]) for key, value in old.items()]
    if any(min(delta) < 0 for delta in changes):
        return None
    return sum(item[0] for item in changes) / seconds, sum(item[1] for item in changes) / seconds


class Sampler:
    def __init__(self, proc: Path = Path("/proc"), sysfs: Path = Path("/sys")):
        self.proc, self.sysfs = proc, sysfs
        self.last = None

    def sample(self) -> dict:
        cpu = cpu_counters(read_text(self.proc / "stat") or "")
        net = net_counters(read_text(self.proc / "net/dev") or "")
        block = {item.name for item in (self.sysfs / "block").glob("*")
                 if re.fullmatch(r"(?:sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme\d+n\d+|mmcblk\d+)", item.name)}
        disk = disk_counters(read_text(self.proc / "diskstats") or "", block)
        now = time.monotonic()
        old = self.last
        self.last = now, cpu, net, disk
        result = memory_values(read_text(self.proc / "meminfo") or "")
        result.update(cpu_percent=cpu_percent(old[1], cpu) if old else None,
                      network_bytes_per_second=rates(old[2], net, now - old[0]) if old else None,
                      disk_bytes_per_second=rates(old[3], disk, now - old[0]) if old else None)
        load = (read_text(self.proc / "loadavg", 256) or "").split()
        uptime = (read_text(self.proc / "uptime", 256) or "").split()
        result["load"] = load[:3] or None
        try:
            result["uptime_seconds"] = float(uptime[0]) if uptime else None
        except ValueError:
            result["uptime_seconds"] = None
        return result

