"""Curated diagnostics and guarded interactive APT dependency repair."""
from __future__ import annotations

import os
import re
import stat
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from .adapters import APT_REPAIR
from .catalog import CANCELLED, FAILED, OK, REVIEW, UNAVAILABLE, Result, run
from .reports import Audit
from .runner import clean


SCENARIOS = {
    "health": ("info", "resources", "space", "failed-services", "logs", "package-audit"),
    "slow": ("resources", "pressure", "processes", "space", "sensors", "boot-time"),
    "network": ("addresses", "routes", "routes-v6", "dns", "networkmanager", "sockets"),
    "storage": ("block", "space", "mounts", "fstab", "raid", "kernel-logs"),
    "services": ("services", "failed-services", "timers", "logs"),
    "packages": ("package-audit", "updates", "package-verify"),
    "security": ("hardening", "firewall", "secure-boot", "selinux", "apparmor"),
    "graphics": ("pci", "display", "nvidia", "opengl", "vulkan", "kernel-logs"),
    "audio": ("audio", "pci", "failed-services", "logs"),
    "boot": ("boot", "boot-time", "failed-services", "previous-boot", "fstab"),
}

REPAIR = "apt-dependencies"
APT_APPLY = tuple(arg for arg in APT_REPAIR[1:] if arg != "-s")
PACKAGE = re.compile(r"^(Inst|Conf)\s+([a-z0-9][a-z0-9+.-]*(?::[a-z0-9-]+)?)\b(?:\s|$)")
SUMMARY = re.compile(r"^(\d+) upgraded, (\d+) newly installed, (\d+) to remove and (\d+) not upgraded\.$", re.M)


def diagnose(scenario, context, stop=None):
    for tool in SCENARIOS[scenario]:
        if stop and stop.is_set():
            yield Result(tool, CANCELLED, context.text("Tanılama durduruldu.", "Diagnostics stopped."))
            break
        yield run(tool, context, stop)


@dataclass(frozen=True)
class Plan:
    actions: tuple
    detail: str


def plan_from_output(output: str) -> Plan:
    counts = SUMMARY.findall(output)
    if len(counts) != 1 or int(counts[0][2]) != 0:
        raise ValueError("Missing APT summary or removal proposed")
    actions = []
    for line in output.splitlines():
        if re.match(r"^(Remv|Purg)\b", line):
            raise ValueError("APT proposed package removal")
        if re.match(r"^(Inst|Conf)\b", line):
            if not PACKAGE.match(line):
                raise ValueError("Unrecognized APT action")
            actions.append(line.strip())
    installations = sum(line.startswith("Inst ") for line in actions)
    if installations != sum(int(counts[0][i]) for i in (0, 1)):
        raise ValueError("APT plan and summary disagree")
    return Plan(tuple(actions), output.strip())


def trusted_binary(path: str, expected_name: str) -> str:
    original = Path(path)
    if original.name != expected_name or original.parent not in {Path("/usr/bin"), Path("/bin")}:
        raise PermissionError("Unexpected privileged executable path")
    resolved = original.resolve(strict=True)
    if resolved != Path("/usr/bin") / expected_name and resolved != Path("/bin") / expected_name:
        raise PermissionError("Unexpected privileged executable target")
    for node in (resolved, *resolved.parents, original.parent, *original.parent.parents):
        attrs = node.stat()
        if attrs.st_uid != 0 or attrs.st_mode & (stat.S_IWGRP | stat.S_IWOTH):
            raise PermissionError("Privileged command path has unsafe permissions")
    if not resolved.is_file() or not os.access(str(resolved), os.X_OK):
        raise PermissionError("Privileged executable is not available")
    return str(resolved)


def native_apply(sudo, apt):
    environment = {"PATH": "/usr/sbin:/usr/bin:/sbin:/bin", "LANG": "C", "LC_ALL": "C",
                   "TERM": os.environ.get("TERM", "dumb")}
    process = subprocess.Popen((sudo, "--", apt, *APT_APPLY), cwd="/", env=environment, shell=False)
    while True:
        try:
            return process.wait()
        except KeyboardInterrupt:
            # The foreground terminal already signalled sudo. Wait for its native exit and verify.
            continue


def _preview(ctx, apt, stop=None):
    result = ctx.execute((apt, *APT_REPAIR[1:]), timeout=45, stop=stop)
    if result.reason == "cancelled":
        raise InterruptedError("APT simulation cancelled")
    if result.code != 0 or result.reason or result.truncated:
        raise ValueError(result.output.strip() or result.reason or "APT simulation failed")
    return plan_from_output(result.output)


def _verify(ctx, apt, stop=None):
    dpkg = ctx.find("dpkg")
    if not dpkg:
        return False, "dpkg unavailable"
    audit = ctx.execute((dpkg, "--audit"), timeout=45, stop=stop)
    check = ctx.execute((apt, "-s", "check"), timeout=45, stop=stop)
    if audit.reason == "cancelled" or check.reason == "cancelled":
        raise InterruptedError("APT verification cancelled")
    verified = (audit.code == check.code == 0 and not audit.reason and not check.reason
                and not audit.truncated and not check.truncated and not audit.output.strip())
    return verified, "\n".join(part.strip() for part in (audit.output, check.output, audit.reason, check.reason) if part.strip())


def repair(ctx, apply=False, *, stop=None, prompt=input, terminal=None, audit_factory=Audit,
           launch=native_apply, trust=trusted_binary, display=print) -> Result:
    host = ctx.host
    if not host.linux or host.package != "apt":
        return Result(REPAIR, UNAVAILABLE, ctx.text("Debian ailesi ve APT gerekiyor.", "This requires a Debian-family APT host."))
    if host.root or host.immutable or host.wsl or host.container or host.ssh:
        return Result(REPAIR, UNAVAILABLE, ctx.text("Onarım, yerel ve değiştirilebilir bir Linux oturumunda normal kullanıcıyla kullanılabilir.",
                                                   "Repair requires a local, mutable Linux session as an unprivileged user."))
    apt = ctx.find("apt-get")
    if not apt:
        return Result(REPAIR, UNAVAILABLE, "apt-get")
    audit = None
    try:
        apt = trust(apt, "apt-get")
        first = _preview(ctx, apt, stop)
        if not first.actions:
            verified, detail = _verify(ctx, apt, stop)
            heading = ctx.text("APT değişiklik önermedi.", "APT did not propose changes.")
            return Result(REPAIR, OK if verified else REVIEW, heading + "\n" + detail)
        if not apply:
            return Result(REPAIR, REVIEW, first.detail)
        if terminal is None:
            terminal = sys.stdin.isatty() and sys.stdout.isatty()
        sudo = ctx.find("sudo")
        if not terminal or not sudo:
            return Result(REPAIR, UNAVAILABLE, ctx.text("Etkileşimli terminal ve sudo gerekli.", "An interactive terminal and sudo are required."))
        sudo = trust(sudo, "sudo")
        display(clean(first.detail))
        token = ctx.text("ONAR", "REPAIR")
        question = ctx.text("Bu planı uygulamak için ONAR yazın: ", "Type REPAIR to apply this plan: ")
        if prompt(question) != token:
            return Result(REPAIR, CANCELLED, ctx.text("Onay verilmedi.", "Plan declined."))
        second = _preview(ctx, apt)
        if first.actions != second.actions:
            return Result(REPAIR, REVIEW, ctx.text("Paket planı değişti. Yeniden önizleyin.", "The package plan changed. Preview it again."))
        audit = audit_factory()
        audit.write("begin", tool=REPAIR, actions=first.actions)
        try:
            code = launch(sudo, apt)
        except (OSError, KeyboardInterrupt) as error:
            audit.write("finish", tool=REPAIR, status="incomplete", error=type(error).__name__)
            raise
        verified, detail = _verify(ctx, apt)
        status = OK if code == 0 and verified else REVIEW if code == 0 else FAILED
        audit.write("finish", tool=REPAIR, status=status, return_code=code, verified=verified)
        message = ctx.text("Paket veritabanı doğrulandı.", "Package database verified.") if status == OK else ctx.text(
            "Onarım başarılı olarak doğrulanamadı.", "Repair could not be verified as successful.")
        return Result(REPAIR, status, message + "\n" + detail, code)
    except (EOFError, KeyboardInterrupt, InterruptedError):
        return Result(REPAIR, CANCELLED, ctx.text("İşlem kesildi. Başlatıldıysa paket durumunu denetleyin.",
                                                 "Interrupted. Check the package state if repair had started."))
    except (OSError, ValueError) as error:
        return Result(REPAIR, FAILED, str(error))
    finally:
        if audit:
            audit.close()
