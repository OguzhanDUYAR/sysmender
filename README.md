# SysMender

**Diagnose with clarity. Repair with confidence.**

*One home for two operating systems. Built for the people who keep them running.*

SysMender is a growing family of maintenance and diagnostic tools for **Windows** and **Linux**. The goal is straightforward: make system problems easier to understand, repairs easier to review, and day-to-day IT work less dependent on a drawer full of one-off scripts.

We believe a good repair tool should show its work. It should tell you what it found, what it proposes to change, what could go wrong, and how to check the result.

> **Project status:** Early development. The repository includes the existing Windows Maintenance Center **v4.4 source code** as a starting point. The Linux edition is a planned product and does **not** yet contain a working application. No cross-platform compatibility or Windows build result is claimed by this repository scaffold.

## Meet the two editions

| Edition | Purpose | Current state |
| --- | --- | --- |
| **[SysMender for Windows](apps/windows/README.md)** | Windows maintenance, system checks, guided repair workflows, and software administration | Existing v4.4 C# / PowerShell source imported; new packaging and CI validation pending |
| **[SysMender for Linux](apps/linux/README.md)** | Distro-aware diagnostics, package and service inspection, logs, networking, and recovery workflows | Architecture and product scope defined; implementation pending |

The editions will share design principles and, where it makes sense, common data contracts. They won't pretend that Windows and Linux are the same operating system.

## What we're building toward

- **Know your system.** Hardware, software, services, storage, networking, and useful health signals in one place.
- **Find the cause.** Actionable diagnostics with evidence, not a generic “everything is fixed” message.
- **Make safer changes.** Permission checks, previews, explicit approval, logs, and verification for system-altering actions.
- **Work like an IT team.** Repeatable checklists, exportable reports, and modular workflows that are easier to support and audit.
- **Respect each platform.** Native Windows tooling on Windows; capability-aware package, init, firewall, and filesystem adapters on Linux.

These are **design goals**, not a statement that every feature is available today. See the [roadmap](ROADMAP.md) and [compatibility notes](docs/COMPATIBILITY.md).

## Getting started

### Windows source

The current Windows source lives in [`apps/windows/`](apps/windows/). It targets Windows 10 (22H2) and Windows 11 and uses Windows Forms / .NET Framework and PowerShell.

From **Windows PowerShell** on a suitable Windows machine:

```powershell
cd apps/windows
.\Derle.ps1
```

For the existing verification harness, see the [Windows edition README](apps/windows/README.md). This repository has **not** verified a Windows build in the current environment. Review the source and security guidance before running maintenance actions, especially those requiring elevation.

### Linux edition

The Linux edition is **not yet installable**. Start with its [scope and design notes](apps/linux/README.md) or contribute to the [distribution adapters roadmap](ROADMAP.md). Do not trust download or installation instructions that claim to be an official SysMender Linux release before one is published here.

## Repository layout

```text
sysmender/
├── apps/
│   ├── windows/       # Imported Windows Maintenance Center v4.4 source
│   └── linux/         # Linux edition planning and future implementation
├── shared/            # Future cross-platform contracts (no runtime code yet)
├── docs/              # Architecture, safety, provenance, compatibility, releases
├── tools/             # Repository structure validation
├── .github/           # CI and contributor templates
├── README.md
├── ROADMAP.md
├── CONTRIBUTING.md
├── SECURITY.md
├── LICENSE
└── THIRD_PARTY_NOTICES.md
```

## The rules we build by

**Visibility before intervention.** Read-only checks first; changes only after their effects are explained.

**No magic performance claims.** Faster, safer, or healthier means we have evidence, not just a button label.

**Privilege is earned, not assumed.** The UI should run without administrator/root privileges whenever possible. Privileged work must be narrowly scoped and auditable.

**Open source deserves proper credit.** We review upstream code, respect its license, preserve notices, and track what was imported and why.

See our [safety model](docs/SAFETY_MODEL.md), [architecture](docs/ARCHITECTURE.md), and [source provenance](docs/SOURCE_PROVENANCE.md).

## Contributing

If you work with Windows support, Linux desktops, servers, or real-world repair tickets, your perspective is valuable. You can help by reporting reproducible problems, improving documentation, writing targeted tests, or building one small, well-verified tool at a time.

Read [CONTRIBUTING.md](CONTRIBUTING.md), the [Code of Conduct](CODE_OF_CONDUCT.md), and our [security disclosure policy](SECURITY.md) before opening a pull request. For setup and planned quality checks, see [developer notes](docs/DEVELOPMENT.md).

## Roadmap and releases

The [roadmap](ROADMAP.md) separates current source from planned capabilities. [Release notes](CHANGELOG.md) will distinguish changes shipped here from the historical Windows v4.4 baseline. **There is no official SysMender release attached to this scaffold.**

## License and acknowledgments

Original SysMender material in this repository is offered under the [MIT License](LICENSE), subject to authorship and provenance review before public distribution. Imported and third-party components retain their own notices and any applicable license obligations. The legacy Windows source includes a [WinUtil MIT notice](apps/windows/WinUtil-MIT.txt) and identifies adapted WinUtil functionality. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [licensing notes](docs/LICENSING.md).

---

*Systems are complicated enough. Looking after them shouldn't have to be.*
