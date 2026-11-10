# Contributing to SysMender

Thanks for considering a contribution. SysMender is being built for people who have to trust a tool while a computer, an employee, or a whole department is waiting for a fix.

Small, verifiable improvements are more valuable here than a long menu of untested buttons.

## Good first contributions

- Reproduce and document a Windows v4.4 issue.
- Improve an error message or operator-facing explanation.
- Add a small, read-only diagnostic with a test.
- Contribute Linux capability probes and distribution fixtures.
- Improve the risk review, audit trail, or rollback design.
- Correct an attribution, compatibility note, or inaccurate documentation claim.

## Before you start

1. Check existing issues and the [roadmap](ROADMAP.md).
2. For major changes, open a design issue first. State the user problem and proposed evidence of success.
3. Review [SECURITY.md](SECURITY.md) and [docs/SAFETY_MODEL.md](docs/SAFETY_MODEL.md).
4. Build and test only in an environment where you are authorized to make changes. Never test destructive repair paths on a production host.

## Pull-request expectations

- Keep changes focused and describe **what changed**, **why**, and **how you checked it**.
- Name the affected OS versions, distributions, architectures, and privilege levels.
- Add or update tests, especially for failure cases and denied permissions.
- Distinguish `tested`, `partially tested`, and `not tested` in your PR.
- For mutating actions, include preflight, user consent, a clear result, and verification. Describe rollback where feasible.
- No speculative benchmarks, invented compatibility claims, or success messages based solely on a button click.
- Do not include logs, dumps, machine names, addresses, access tokens, personal data, or secrets from real systems.
- Add a source/provenance record for borrowed code. Never copy code solely because a repository is public.

## Style and architecture

Windows-specific work belongs under `apps/windows/`; Linux-specific work under `apps/linux/`. Put concepts in `shared/` only after real consumers exist on both platforms.

Prefer explicit command arguments and structured API output. Avoid string-concatenated shell commands, hidden downloads, blanket privilege elevation, and bundled unknown executables.

Use English for new contributor-facing docs and code comments unless the material is directly related to a localized UI. Keep language files translatable.

## Development and validation

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). GitHub Actions may build the imported Windows source and validate repository structure; they are not substitutes for hardware, privileged operations, or full graphical tests.

## Review and collaboration

Please be constructive, specific, and generous with context. Maintainers may decline a feature when it increases risk or complexity more than it helps an operator. That is a design choice, not a judgment of the contributor.

By contributing under the repository's proposed MIT terms, you confirm that you have the right to submit the material and that third-party licensing has been properly disclosed. See [docs/LICENSING.md](docs/LICENSING.md).
