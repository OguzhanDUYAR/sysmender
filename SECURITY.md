# Security policy

SysMender may inspect sensitive system state and, in later versions, propose or perform privileged maintenance actions. We treat security reports with care and expect security claims to be backed by evidence.

## Supported versions

There are **no declared supported SysMender release versions yet**. The Windows v4.4 source is a historical baseline, not a promise of ongoing security support. The Linux edition has no released implementation.

## Reporting a vulnerability

**Do not open a public issue containing exploit details, secrets, private logs, or vulnerable endpoint information.**

Once this repository is published, maintainers should enable GitHub **Private vulnerability reporting** (Settings → Advanced Security → Private vulnerability reporting). Then use the **Security → Advisories → Report a vulnerability** flow.

Until private reporting is enabled, the maintainers must publish an appropriate private contact channel. No response time or private contact address is claimed by this scaffold.

A useful report includes affected revision, expected and actual behavior, scope and impact, minimum reproduction, and suggested mitigation. Redact all credentials and identifiers.

## Secure development baseline

- Read-only diagnostics by default.
- Narrowly scoped privilege escalation only when required.
- No unreviewed remote script execution or command-string interpolation.
- Preview, consent, logging, and result verification for changing operations.
- Dependency and imported-source provenance review.
- Sensitive data minimization and safe report export.
- Separate security fixes from speculative optimizations.

See [docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md).
