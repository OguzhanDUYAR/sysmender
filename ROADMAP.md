# Roadmap

**The promise:** a useful, careful IT tool is more valuable than a spectacular list of buttons that have never been tested.

Roadmap items describe intended work, **not shipped features**. Priorities may change when real operator feedback or compatibility evidence changes the cost of doing something safely.

## Foundation — P0

- [x] Establish a single repository for both editions.
- [x] Preserve the supplied Windows v4.4 source and its attribution notice.
- [x] Add contributor, security, provenance, and release documentation.
- [ ] Build the imported Windows source in a Windows CI environment and verify the test harness safely.
- [ ] Review Windows source for permissions, command execution, errors, and third-party provenance.
- [ ] Define shared task/result/risk/report contracts based on a real implementation need.
- [ ] Implement Linux capability detection and read-only inventory.
- [ ] Add Linux package, service, network, log, and storage adapter interfaces.

## Practical tools — P1

- [ ] Design and validate Windows repair workflows with preflight and post-action checks.
- [ ] Add Linux diagnostics for commonly supported distro families.
- [ ] Establish a consistent, accessible interface for each edition.
- [ ] Create redacted, exportable technical reports.
- [ ] Integrate system-specific authorization and auditable task execution.
- [ ] Build unit, integration, and VM-based regression test suites.

## IT workflows — P2

- [ ] Introduce repeatable support checklists and task history.
- [ ] Add optional backup/restore integrations with clear prerequisites.
- [ ] Expand distro and init-system adapters based on test coverage.
- [ ] Add appropriately authorized enterprise and remote-diagnostic integrations.
- [ ] Package and sign releases when the build and distribution processes are ready.

## Release gates

No public build should claim a capability without a documented implementation and a test result. Destructive or privileged features require separate review, clear operator consent, and a tested failure path. OS compatibility claims must identify exact tested versions and environments.
