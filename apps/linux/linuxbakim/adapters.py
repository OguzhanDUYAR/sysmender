"""Explicit distribution and init adapters. There is no guess-and-execute fallback."""
from dataclasses import dataclass
from typing import Dict, Tuple

Command = Tuple[str, ...]


@dataclass(frozen=True)
class Packages:
    installed: Command
    audit: Command = ()
    updates: Command = ()
    verify: Command = ()


RPM_LIST = ("rpm", "-qa", "--qf", "%{NAME}\t%{VERSION}-%{RELEASE}\t%{ARCH}\n")
RPM_VERIFY = ("rpm", "-Va", "--noscripts")
APT_SIMULATE = ("apt-get", "-s", "--no-remove", "-o", "APT::Get::Assume-Yes=false",
                "-o", "APT::Get::Force-Yes=false", "-o", "APT::Get::AllowUnauthenticated=false",
                "-o", "APT::Get::Allow-Downgrades=false", "-o", "APT::Get::Allow-Change-Held-Packages=false",
                "-o", "Acquire::AllowInsecureRepositories=false", "-o", "Acquire::AllowDowngradeToInsecureRepositories=false")
APT_REPAIR = (*APT_SIMULATE, "-f", "install")
PACKAGES: Dict[str, Packages] = {
    "apt": Packages(("dpkg-query", "-W", r"-f=${binary:Package}\t${Version}\t${db:Status-Status}\n"),
                    ("dpkg", "--audit"), (*APT_SIMULATE, "upgrade"), ("dpkg", "--verify")),
    "pacman": Packages(("pacman", "-Q"), ("pacman", "-Dk"), ("pacman", "-Qu"), ("pacman", "-Qk")),
    "dnf": Packages(RPM_LIST, ("dnf", "-C", "check"), ("dnf", "-C", "check-update"), RPM_VERIFY),
    "rpm": Packages(RPM_LIST, verify=RPM_VERIFY),
    "zypper": Packages(RPM_LIST, updates=("zypper", "--no-refresh", "--non-interactive", "list-updates"), verify=RPM_VERIFY),
    "apk": Packages(("apk", "info", "-v"), updates=("apk", "version", "-l", "<")),
    "portage": Packages(("qlist", "-I")),
    "xbps": Packages(("xbps-query", "-l")),
    "nix": Packages(("nix-env", "-q", "--installed")),
    "ostree": Packages(("rpm-ostree", "status", "--json")),
}

SERVICES: Dict[str, Command] = {
    "systemd": ("systemctl", "--no-pager", "--plain", "list-units", "--type=service", "--all"),
    "openrc": ("rc-status", "--all"),
}

