# floatsoda-apps Agent Instructions

> Adapted from the `pippirium/agent_instructions` preset `projects/floatsoda/AGENTS.md` (the fork-boundary rules) and the repository-global `AGENTS.md`, adjusted for this application repository. `agent_instructions` is **not** automatically applied to this checkout, and there is no automatic synchronization — this file is the instruction actually read here. The app design and operational boundary live in [PLAN.md](PLAN.md); the repository root of `engine/` carries the framework's own instructions.

## Scope and existing conventions

- This repository holds SteamVR overlay **applications** (`apps/ScreenView`, `apps/WristAgent`, `tools/WristAgentBridge`). The framework is pinned as the `engine/` git submodule of the `pippirium/FloatSoda` fork.
- **Framework changes belong in the fork, not here.** In this repository, update only the submodule pin. Do not edit `engine/` sources in place.
- Read [PLAN.md](PLAN.md) for the repository's positioning and its prohibited operations, and `engine/AGENTS.md` plus `engine/CONTRIBUTING.md` for framework conventions when working through the submodule.
- Keep user-facing reports, reviews, PR descriptions, and commit messages in **Japanese**. Leave code identifiers, type names, file paths, and commands verbatim.

## GitHub write boundary

- **All writes target `pippirium/floatsoda-apps` and `pippirium/FloatSoda` only.** The upstream `sumx21t-3310/FloatSoda` may be read for reference; never push, open a PR, create an Issue, or otherwise write to it without separate explicit authorization.
- Work on task-specific branches. Before creating a PR, confirm the base repository is exactly the intended `pippirium/...` repository and the base branch is `main`. Do not trust GitHub's default comparison target.
- Open changes as **Draft PRs** for human review. Do not merge, publish, or deploy without authorization.
- Branch names describe *what* changes, not *who* (or which tool) did it. No `codex/`, `claude/`, `cursor/` prefixes; use lowercase ASCII slugs.
- **No upstream synchronization by default, in either direction**, for the `FloatSoda` fork. Importing upstream changes into the fork, or sending fork changes upstream, requires prior, operation-specific, explicit user approval — judge by the **result**, not the command name (`Sync fork`, `gh repo sync`, `git pull/merge/rebase upstream/*`, `cherry-pick`, patch application, sync bots and schedules are all covered). Reading upstream source or `git fetch upstream` purely for inspection with no integration is allowed. Even after approval, never sync directly into the fork's `main`: use a dedicated branch and a Draft PR.
- Issues are disabled on the `pippirium/FloatSoda` fork (observed 2026-10-25). The fork exists as a personal customization area that will not be taken upstream, so **an Issue is not created for work here**; state this in the PR description instead. Never create an Issue upstream to satisfy that rule.

## Verification

- The PR preflight for this repository is the Release-equivalent of the framework's documented sequence, run against the solution that includes the pinned engine:

  ```sh
  dotnet build FloatSoda.Apps.slnx --configuration Release
  dotnet test engine/tests/FloatSoda.Rendering.Test --configuration Release --no-build
  dotnet test engine/tests/FloatSoda.Test --configuration Release --no-build
  ```

- SteamVR-dependent behavior (overlay display, controller gestures, HMD visibility) is verified **only when SteamVR and the hardware are actually available**. Report what was not run and why; never claim device verification from headless builds.
- For documentation-only edits, check links and paths, and verify the exact PR base instead of building.

## Git workflow and attribution

- Task branch → commit → Draft PR. Never commit directly to the base branch.
- Credit the actual AI agent with a `Co-authored-by` trailer and in the PR body, using a documented attribution address; do not invent identities.
