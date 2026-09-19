---
id: 001
status: ready
priority: p3
created: 2026-09-18
updated: 2026-09-18
---

# Publish v1.0.1 Release

## Goal

Tag and publish the v1.0.1 release of OpenSuperWhisper-Windows so the
one-line installer ships the green recording cursor feature.

## Context

Commit 830db62 (green recording cursor) is merged to main, tested, and
deployed to the live local install on 2026-09-18. The public release is
deliberately held for Alex's go — pushing the tag updates the
`irm .../get-opensuperwhisper.ps1` install path for everyone.

Mechanism (from the v1.0.0 release): push tag `v1.0.1` and the release
workflow (.github/workflows/release.yml) runs tests\Test.ps1, builds with
`setup-windows.ps1 -Engine cuda -SkipGpuCheck`, zips dist, and creates the
GitHub release. Pushes need the `alpacarodeo` gh account active (da3-gif
gets 403). Watch the workflow run to green and verify the release asset
digest before pointing anyone at it.

## Acceptance Criteria

- [ ] Tag `v1.0.1` pushed; release workflow completed green
- [ ] GitHub release asset (807 MB-scale CUDA zip) attached with checksum
- [ ] One-line install command verified against the new release
- [ ] Live local install still on the same or newer build

## Dependencies

- None (feature commit 830db62 already on main)

## Notes

Only remaining user-visible change to announce: green system-wide recording
cursor during dictation. Backup of the previous live exe:
`OpenSuperWhisper.Windows.exe.bak-20260918` beside the install.

## Completion

Leave empty until completed.
