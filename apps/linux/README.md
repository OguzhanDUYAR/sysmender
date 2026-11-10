# SysMender for Linux

**Distro-aware maintenance. No one-size-fits-all shell tricks.**

SysMender for Linux is the planned companion to the Windows edition. No implementation, installer, or supported-distribution certification is included yet.

## What we want this edition to do

Bring together read-only system inventory, log diagnostics, network and service checks, storage and package inspection, security posture, and guided troubleshooting under one practical interface.

A desktop UI and headless CLI should share one core. Each Linux environment will be detected by its capabilities rather than guessed from its distribution name.

## First platform targets

| Family | Primary integration under consideration |
| --- | --- |
| Debian / Ubuntu / Mint | APT and systemd-aware providers |
| Arch / CachyOS | Pacman and systemd-aware providers |
| Fedora / Rocky / AlmaLinux | RPM / DNF and systemd-aware providers |
| openSUSE | Zypper, Snapper, and systemd-aware providers |
| NixOS / SteamOS / immutable systems | Explicit, distribution-specific workflows |
| Alpine / Void / Gentoo | Specialized package and init adapters |

These are **development targets**, not tested compatibility guarantees. GUI behavior may differ under Wayland, X11, or headless operation.

## Planned architecture

1. **Capability probe:** Detect operating system, kernel, init, package ecosystem, filesystem, privileges, desktop, and installed management tools.
2. **Read-only adapters:** Inventory, networking, logs, services, and storage.
3. **Action engine:** Explain, preview, authorize, run, verify, and log each change.
4. **User interfaces:** Desktop GUI and scriptable CLI on the same domain layer.
5. **Compatibility suite:** Containers for low-risk unit/integration coverage; VMs or actual hardware for privileged and graphical workflows.

## How to contribute today

The best first contributions are a tested capability detector, an adapter interface, a distribution test fixture, or a documented operator workflow. See [`../../ROADMAP.md`](../../ROADMAP.md) and [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md).

**No general `curl | sh`, no automatic root shell, no unsupported repair commands.**
