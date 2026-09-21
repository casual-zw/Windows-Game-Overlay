# Milestone 1 — Windows foundation

## Goal

Prove that we can capture a game window and display a readable Chinese overlay while preserving normal game input. Use fixed Chinese sample text; OCR and Luna integration belong to later milestones.

## Work packages

1. **Environment and reproducible build.** Pin the .NET SDK; scaffold the Windows app and portable test project; provide local build instructions, opt-in packaging scripts, and a Windows build/test CI workflow.
2. **Window capture.** List visible windows, select a target, use Windows Graphics Capture, show a bounded-rate preview, and stop cleanly when the target closes or capture fails. Suspend frame copying when minimized or inactive.
3. **Region selection.** Drag a rectangle in the letterboxed window preview. Display its actual cropped pixels and coordinates. Store proportional coordinates; explain reselection when the game changes its layout.
4. **Chinese overlay.** Fixed Chinese sample paragraph, readable font, size/opacity controls, move/resize edit mode, click-through reading mode, show/hide hotkey, focus handling, and capture exclusion.
5. **Validation and handoff.** Test coordinate boundaries and visibility policy automatically. Use a built-in controllable scene for input/capture checks. Build from source on the Windows PC and validate a real game through the manual checklist. Packaging is optional and deferred while code correctness is the priority.

## Implementation choices

C#/.NET 10 and WPF; direct Win32/D3D11 interop with Windows Graphics Capture. No third-party capture wrapper, model SDK, backend, installer, or administrator privileges. WPF keeps the small control UI and Chinese panel in one executable. Separate portable core logic from the Windows integration.

The control window shows the complete captured frame for selection plus the cropped region. Capture does not substitute desktop screenshots when a selected game cannot be captured. Filler text is explicitly labeled so it cannot be mistaken for a translation.

Reading mode is non-activating and click-through. Editing is deliberate and intercepts input so the panel can be moved and resized. The panel is visible over the selected foreground game, and during editing with the control/overlay window active; it hides over unrelated apps and when the game minimizes. Its anchor follows target movement and resize. Hotkeys: Ctrl+Alt+T toggles visibility, Ctrl+Alt+E toggles editing.

## Tests and exit gate

Automated: reverse/out-of-bounds drags, last-pixel crops, very small selections, invalid coordinates/dimensions, randomized crop containment, letterboxing, minimized/closed targets, manual hiding, edit mode, and alt-tab visibility policy.

Windows manual: follow `WINDOWS-TEST.md`. Demonstrate the built-in scene and at least one real game in windowed or borderless mode. Verify Chinese rendering, capture isolation, game input, move/resize, focus, minimize/restore, show/hide, target close, and application shutdown. Record display scaling and whether the session is local or remote. Mixed-DPI behavior requires a physical multi-monitor test when available.

**M1 is not complete merely because it compiles.** The native Windows checks and real-game exit gate must pass. A remote session alone does not establish local GPU/display behavior.

## Dependencies and limits

The user has a Windows PC; a direct remote connection is not yet configured. First handoff uses source code and `BUILD.md`; ZIP creation and upload are not part of the current work. API credentials and translation budget are not needed until M3. No remote desktop services, firewall rules, or credentials are changed by this milestone.

Deferred: OCR, real translation, model comparison, game context, saved preferences, automatic game identification, configurable hotkeys, HDR/exclusive fullscreen, signed installation, and auto-update.
