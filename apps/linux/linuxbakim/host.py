"""Capability detection without sourcing configuration or probing the network."""
from __future__ import annotations

import os
import platform
import re
import shlex
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Dict, Optional, Tuple

SYSTEM_PATHS = ("/usr/bin", "/usr/sbin", "/bin", "/sbin",
                "/run/current-system/sw/bin", "/nix/var/nix/profiles/default/bin")
FAMILIES = {
    "debian": {"debian", "ubuntu", "linuxmint", "pop", "zorin", "kali", "raspbian"},
    "arch": {"arch", "cachyos", "endeavouros", "manjaro", "garuda", "steamos"},
    "rpm": {"fedora", "rhel", "rocky", "almalinux", "centos", "ol"},
    "suse": {"opensuse", "opensuse-tumbleweed", "opensuse-leap", "opensuse-slowroll", "suse", "sles"},
    "alpine": {"alpine"},
    "gentoo": {"gentoo"},
    "void": {"void"},
    "nix": {"nixos"},
}
PACKAGE_COMMANDS = {
    "apt": ("apt-get", "dpkg-query", "dpkg"), "pacman": ("pacman",),
    "dnf": ("rpm", "dnf"), "rpm": ("rpm",), "zypper": ("rpm", "zypper"),
    "apk": ("apk",), "portage": ("emerge",), "xbps": ("xbps-query",),
    "nix": ("nix-env",), "ostree": ("rpm-ostree",),
}


def read_text(path: Path, limit: int = 131072) -> Optional[str]:
    try:
        with path.open("rb") as stream:
            return stream.read(limit).decode("utf-8", errors="replace")
    except OSError:
        return None


def parse_os_release(source: str) -> Dict[str, str]:
    allowed = {"ID", "ID_LIKE", "VERSION_ID", "PRETTY_NAME", "VARIANT_ID"}
    values: Dict[str, str] = {}
    for line in source.splitlines():
        key, sep, value = line.partition("=")
        if not sep or key not in allowed or len(value) > 1024:
            continue
        try:
            words = shlex.split(value, comments=False, posix=True)
        except ValueError:
            continue
        if len(words) == 1:
            values[key] = words[0]
    return values


def distro_family(identifier: str, like: Tuple[str, ...]) -> str:
    for hint in (identifier, *like):
        for family, names in FAMILIES.items():
            if hint in names:
                return family
    return "unknown"


def find_tool(name: str, paths: Tuple[str, ...] = SYSTEM_PATHS) -> Optional[str]:
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9._+-]*", name):
        return None
    for base in paths:
        candidate = Path(base) / name
        if candidate.is_file() and os.access(str(candidate), os.X_OK):
            return str(candidate)
    return None


@dataclass(frozen=True)
class Host:
    linux: bool
    distro_id: str
    pretty_name: str
    version: str
    id_like: Tuple[str, ...]
    family: str
    architecture: str
    kernel: str
    libc: str
    init: str
    package: str
    desktop: str
    session: str
    wsl: bool
    container: bool
    ssh: bool
    immutable: bool
    root: bool

    def as_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def detect(cls) -> "Host":
        linux = sys.platform.startswith("linux")
        release = parse_os_release((read_text(Path("/etc/os-release")) or
                                    read_text(Path("/usr/lib/os-release")) or "") if linux else "")
        identifier = release.get("ID", "unknown").lower()
        like = tuple(release.get("ID_LIKE", "").lower().split())
        family = distro_family(identifier, like)
        variant = release.get("VARIANT_ID", "").lower()
        kernel = platform.release()
        pid1 = (read_text(Path("/proc/1/comm"), 128) or "").strip() if linux else ""
        systemd = linux and Path("/run/systemd/system").is_dir() and pid1 == "systemd"
        openrc = linux and Path("/run/openrc").is_dir() and find_tool("rc-status") is not None
        runit = linux and (pid1 in {"runit", "runit-init"} or Path("/run/runit").is_dir())
        init = "systemd" if systemd else "openrc" if openrc else "runit" if runit else pid1 or "unknown"
        ostree = linux and Path("/run/ostree-booted").exists()
        immutable = linux and (ostree or identifier in {"nixos", "steamos"} or
                              variant in {"silverblue", "kinoite", "sericea", "onyx", "atomic", "microos", "aeon", "kalpa"})
        cgroup = read_text(Path("/proc/1/cgroup"), 4096) or "" if linux else ""
        container = linux and (any(Path(path).exists() for path in ("/.dockerenv", "/run/.containerenv", "/run/systemd/container"))
                               or any(word in cgroup for word in ("docker", "kubepods", "containerd", "libpod", "/lxc")))
        if linux and not container and pid1 not in {"systemd", "init", "runit", "runit-init", "openrc-init", "s6-svscan"}:
            container = True  # an unidentified PID namespace is never a mutation target
        managers = {"debian": ("apt",), "arch": ("pacman",), "rpm": ("dnf", "rpm"),
                    "suse": ("zypper", "rpm"), "alpine": ("apk",), "gentoo": ("portage",),
                    "void": ("xbps",), "nix": ("nix",)}
        candidates = ("ostree",) if ostree else managers.get(family, ())
        package = next((manager for manager in candidates
                        if all(find_tool(tool) for tool in PACKAGE_COMMANDS[manager])), "unknown") if linux else "unknown"
        libc_name, libc_version = platform.libc_ver() if linux else ("", "")
        if linux and not libc_name and any(Path("/lib").glob("ld-musl-*.so.1")):
            libc_name = "musl"
        return cls(linux, identifier, release.get("PRETTY_NAME", platform.system()), release.get("VERSION_ID", ""),
                   like, family, platform.machine(), kernel, " ".join(filter(None, (libc_name, libc_version))) or "unknown",
                   init, package, os.environ.get("XDG_CURRENT_DESKTOP", ""), os.environ.get("XDG_SESSION_TYPE", ""),
                   linux and ("microsoft" in kernel.lower() or bool(os.environ.get("WSL_INTEROP"))),
                   bool(container), bool(os.environ.get("SSH_CONNECTION") or os.environ.get("SSH_TTY")), bool(immutable),
                   linux and getattr(os, "geteuid", lambda: -1)() == 0)

