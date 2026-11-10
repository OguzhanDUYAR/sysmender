# Changelog

All notable changes made under the SysMender name will be recorded here. Dates refer to actual repository changes, not imported upstream history.

The format is inspired by [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). No semantic version or public release is declared for this new repository yet.

## [Unreleased]

### Added

- Initial monorepo structure for Windows and Linux editions.
- Imported the supplied Windows Bakım Merkezi v4.4 source baseline without modifying its files.
- Added project documentation, contribution templates, licensing guidance, and repository validation workflow.

### Planned

- Independently verify the Windows baseline on supported Windows versions.
- Implement the Linux edition and its capability-aware adapters.
- Introduce repeatable compatibility and security checks before releases.

## Historical baseline (not a SysMender release)

The imported Windows source identifies itself as **Windows Bakım Merkezi 4.4**. Refer to [`apps/windows/KULLANIM.md`](apps/windows/KULLANIM.md) for the historical feature descriptions. No SysMender release artifact is created by importing it.
