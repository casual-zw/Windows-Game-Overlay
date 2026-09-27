# Project development conventions

These instructions apply throughout this repository. Keep this file as the shared
home for development preferences and tool conventions. Build instructions belong
in `BUILD.md`; Windows runtime checks belong in `docs/WINDOWS-TEST.md`.

## Tool preference

- Prefer GitHub CLI (`gh`) for GitHub operations; Git CLI (`git`) is also allowed.
- Shell commands and file-editing tools remain appropriate for source edits, searches, builds, and tests.

## Change scope and collaboration

- Preserve unrelated local edits and exclude them from commits for the current task.
- Keep changes focused on the requested behavior and follow the existing C#/WPF style.
- Clearly report the branch or worktree used, whether a commit is local or remote,
  and whether a push actually succeeded. Verify remote state with `gh` before
  claiming changes are published.
- Do not infer a permanent branch or worktree policy from earlier work on `main`.
  Follow explicit user instructions about where to work.

## Validation

- This development Mac has no .NET SDK. Treat this as known: do not repeatedly
  probe for `dotnet`, search for an SDK, or attempt local .NET builds/tests after
  changes. Recheck only if the user says the SDK was installed or requests a recheck.
  Use source review and other available checks here; use Windows or CI for .NET
  validation when authorized. Mention skipped validation briefly without retrying it.
- Follow `BUILD.md` and `scripts/build.ps1` for build and test commands. The executable
  test harnesses run with `dotnet run`, not `dotnet test`.
- Run checks appropriate to the change. Report unavailable tooling and unrun checks
  plainly; source review or compilation does not establish Windows runtime behavior.
- For capture or overlay changes, update the Windows checklist as needed and cover
  click-through, focus, capture exclusion, target movement, DPI, and lifecycle behavior.
- Keep overlays out of OCR input and preserve game input. Handle unsupported capture
  features gracefully with an understandable fallback.

## Documentation and releases

- Update relevant documentation when user-visible behavior changes.
- Keep credentials and machine-specific configuration out of tracked files.
- Creating a commit does not imply creating a release. Build or upload release
  artifacts when requested, using the project's documented release workflow.

The preference for `gh` and instruction to skip local .NET attempts are explicit
user preferences. The remaining conventions capture existing project practices and can be refined as work continues.
