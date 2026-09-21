---
name: windows-game-overlay-build
description: Sync the private Windows-Game-Overlay repository from origin/main and produce a tested Windows release EXE. Use for requests to update, build, rebuild, package, or locate the runnable GameOverlay artifact; do not use for feature development or source edits.
---

# Build Windows Game Overlay

Run the repository's deterministic wrapper instead of rediscovering the build commands:

```powershell
./scripts/sync-build.ps1
```

The wrapper requires a clean `main`, fast-forwards from `origin/main`, verifies the pinned .NET 10.0.4xx SDK, runs the core tests, and creates a self-contained single-file EXE and ZIP under `artifacts/releases/`.

If Git authentication fails, stop and ask the user to authenticate this PC with Git Credential Manager. Never request or print a token.

Do not bypass dirty-worktree, non-`main`, diverged-history, SDK, test, or build failures. Use `-SkipSync` only when validating an intentional local change to the wrapper. Use `-SkipTests` only when the user explicitly asks for an unvalidated rebuild.

On success, report clickable absolute paths to the EXE and ZIP plus the SHA-256 printed by the script. Do not launch the overlay unless the user asks, because it opens windows and requests capture access.

