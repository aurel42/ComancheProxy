---
name: commit
description: >
  Release commit workflow: bump version, write changelog entry, clean temp
  files, and commit. Use when the user invokes /commit to ship changes.
user_invocable: true
---

# /commit — Release Commit Workflow

When invoked, execute every step below **in order**. Do NOT skip steps.

## Step 1 — Analyse changes

Run these in parallel:
- `git status` (never use `-uall`)
- `git diff --cached` and `git diff` to see staged + unstaged changes
- `git log --oneline -5` for recent commit style reference

Read the diff carefully. Identify:
- What was added, changed, or fixed.
- Which files were touched.

## Step 2 — Determine the new version

Read the current version from **two** canonical locations:
1. `ComancheProxy.csproj` — the `<Version>` element
2. `Program.cs` — the `logger.LogInfo("ComancheProxy v...")` line

The latest released version is whichever is **higher** between these two (they may be out of sync).

Bump the **patch** component by 1 (e.g. `0.1.4` → `0.1.5`).
If the user provided an explicit version as an argument (e.g. `/commit 0.2.0`), use that instead.

## Step 3 — Update version in source files

Edit both locations to the new version:
1. `ComancheProxy.csproj`: `<Version>NEW</Version>`
2. `Program.cs`: `logger.LogInfo("ComancheProxy vNEW");`

## Step 4 — Write CHANGELOG entry

Read `CHANGELOG.md`. Prepend a new section **immediately after** the `# Changelog` heading, before the first `## [...]` entry.

Format — use exactly this structure, filling in from the diff analysis:

```
## [NEW_VERSION] - YYYY-MM-DD

### Added
- (list genuinely new features/capabilities; omit section if none)

### Changed
- (list modifications to existing behaviour; omit section if none)

### Fixed
- (list bug fixes, leak fixes, perf fixes; omit section if none)
```

Rules:
- Date is today (`YYYY-MM-DD`).
- Each bullet is a single concise sentence (no trailing period).
- Omit empty subsections entirely (don't write `### Added` with no bullets).
- Keep the style consistent with existing entries in the file.

## Step 5 — Clean temporary files

Run `make clean` to remove `bin/` and `obj/` build artifacts.
Delete any `logs/*.log` files if present (but not the `logs/` directory).

## Step 6 — Stage and commit

Stage **only** the meaningful files. Never stage:
- `.env`, credentials, secrets
- `bin/`, `obj/`, `logs/`
- `.claude/settings.local.json`

Commit with this message format:

```
Release vNEW_VERSION — short summary of changes

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
```

The summary after the dash should be a comma-separated list of the key changes (max ~70 chars for the first line).

## Step 7 — Confirm

Show the user:
- The new version number
- The changelog entry that was written
- The full `git log --oneline -3` output
- Remind them to `git push` and `git tag vNEW_VERSION` when ready

Do **NOT** push or tag automatically.
