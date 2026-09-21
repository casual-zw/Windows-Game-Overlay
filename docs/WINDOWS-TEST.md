# Windows foundation — first test

This prototype displays **fixed Chinese sample text**. It does not translate anything yet. It makes no AI requests and does not save screen images.

## Start

1. Follow `BUILD.md` to compile the source on a Windows 11 x64 PC. A release ZIP is not required.
2. Run `dotnet run --project src/Overlay.Windows/Overlay.Windows.csproj -c Release --no-build` from the project root. If you made a self-contained publish folder instead, run its `GameOverlay.exe`. No API key or administrator permission is needed.
3. Click **Test window**, then return to the control window. The test scene should be selected in the list.
4. Click **Start capture**, wait for a preview, then switch to the test scene. Press **Ctrl+Alt+O**, click **Select dialogue region**, and drag around its English dialogue on the frozen preview.
5. Release the mouse: selection closes, focus returns to the scene, and the Chinese panel appears. A Windows capture border may also appear. To change the crop later, reopen the in-game controls without returning to the desktop control window.

## Basic checks

- [ ] Set background opacity to **0%**: in reading mode the fill and outline disappear, while Chinese text remains visible over the game. At **100%**, the background is solid.
- [ ] At 0%, enter edit mode: the blue outline remains available for positioning/resizing. Return to reading mode: the outline disappears again. Repeat after hiding/showing the overlay.
- [ ] Put the panel over the test scene's English text and buttons, not just its plain background. Compare **0%, 50%, 100%**: underlying details are clear, tinted but visible, then hidden. Chinese text stays fully opaque at all three settings. Repeat after entering/leaving edit mode several times; no solid gray rectangle should appear.
- [ ] In edit mode, resize from all four edges and corners, then drag the header. Repeat at 0% background opacity and at 100%/150% display scaling (including a monitor left of the primary monitor, if available). Return to reading mode and verify click-through at the edges as well as the center.

- [ ] Ctrl+Alt+O opens a small clickable control panel over the game, before or after a region is selected.
- [ ] **Select dialogue region** dismisses the controls, opens selection, and returns to the game after a successful drag.
- [ ] **Back to game**, Esc, and Ctrl+Alt+O dismiss the controls and restore game input; repeat opening/closing several times.
- [ ] Alt-tab dismisses the controls without stealing focus. Minimize/close the game or stop capture: no controls remain floating above other apps.
- [ ] The controls never appear in the captured preview. Chinese output resumes click-through behavior after closing controls.
- [ ] If Ctrl+Alt+O conflicts, the desktop window reports it and its **In-game controls** button works.

- [ ] Ctrl+Alt+R selects/reselects dialogue directly over the game; input returns to the game after release.
- [ ] Esc cancels selection and keeps the prior region. A tiny click/drag stays in selection mode with a retry hint.
- [ ] Alt-tab during selection dismisses it without stealing focus from the other app.
- [ ] Moving/resizing/minimizing/closing the target during selection cancels it; no stale region is applied.
- [ ] Repeat selection on displays with different scaling and negative screen coordinates; the crop matches the chosen area.
- [ ] If Ctrl+Alt+R conflicts with another app, the control window reports it and **Select on game** still works.

- [ ] Preview is live: click **Next line / typewriter** in the test scene.
- [ ] After the typewriter stops, the preview includes the final characters even when nothing else changes.
- [ ] Chinese characters are readable rather than empty squares. The panel says it is sample text.
- [ ] Press **Ctrl+Alt+E** to edit. Drag the panel header and resize its edges. Place it over the click-through test button.
- [ ] Press **Ctrl+Alt+E** again to finish. Click through the Chinese panel onto the scene's button. Its counter must increase without the panel stealing focus.
- [ ] Confirm the gray panel header says **阅读模式 · 点击穿透**. Blue **编辑模式** intentionally accepts clicks; Ctrl+Alt+E leaves edit mode.
- [ ] **Ctrl+Alt+T** hides and restores the panel. If the control window reports a hotkey conflict, use its buttons instead.
- [ ] Move/resize the scene. The panel follows; the selected crop scales. Reselect if dialogue layout changes.
- [ ] Alt-tab to another application: the panel hides. Return to the scene: it returns if enabled.
- [ ] Minimize/restore the scene: the panel hides/restores and capture recovers.
- [ ] While editing, return to the control window and confirm the panel does not appear inside the captured scene preview.
- [ ] Close the scene: capture stops and the panel disappears. Open another scene and start again.
- [ ] Stop capture and exit the app: no panel is left behind.
- [ ] Launch again and close immediately without starting capture: exit cleanly without an error.

## Click-through regression (separate process)

The built-in scene shares the overlay's UI thread. Also test a separate process,
which is how a real game receives input. With the main app running, open another
PowerShell in the project root and launch:

```powershell
dotnet run --project src/Overlay.Windows/Overlay.Windows.csproj -c Release --no-build -- --test-scene
```

Refresh the main app's window list, select the standalone **Overlay test scene**,
start capture, and select its dialogue. Put the panel over the scene's click-counter
button in edit mode, then return to reading mode. Verify that the counter increases
when clicking through the panel's text, background, and edges. Repeat after
show/hide, alt-tab, resize, and several edit/reading transitions. In edit mode,
dragging/resizing the panel should not activate the button behind it.

If reading-mode clicks still fail, record the control window's input status, any
Windows error, whether this was the built-in scene/standalone scene/real game,
and whether either application was run as administrator.

## Real-game check

1. Launch an available game in **windowed or borderless** mode.
2. Refresh the list, select the actual game-rendering window (not a launcher), and start capture.
3. Select its dialogue region, then repeat the input, visibility, move/resize, and minimize checks above.
4. Try 100% and 150% Windows display scaling. If you have multiple displays, move the game between displays and check panel placement and crop alignment.
5. Spend at least 10 minutes with capture running. Note flicker, stutter, increasing memory, or input problems. A static scene need not generate five new frames per second.

HDR, exclusive fullscreen, protected capture, and remote GPU sessions are exploratory. A black preview is a failure to record; this prototype does not bypass protections or silently capture the desktop instead. Test a normal local Windows session as well as remote access when possible.

## Report back

Share:

- Source revision (or build time if there is no commit yet) and whether you ran from source or a publish folder.
- Windows version, GPU, monitor resolution, display scaling, and local/remote session type.
- Game and windowed/borderless mode.
- The first failed checkbox, expected versus actual behavior, and exact status/error message.
- A screenshot or short recording only if useful and safe to share.

Record each check as **pass / fail / not tested**. Successful compilation is not a Windows runtime test.

## Milestone 2: local OCR

1. Build using BUILD.md; keep the generated `models/v5` folder beside the app.
   No API key is needed. Disconnect from the internet after building if desired.
2. Capture the built-in test scene. Crop its English dialogue in the main preview.
   Without pressing Read again, verify Recognized English appears in the main
   window. Record the first-read total and OCR timings.
3. Repeat using Ctrl+Alt+R / Select dialogue region on the game. Verify automatic
   reading after release; Escape must cancel without starting another read.
4. Change dialogue, wait for the preview to update, and press Ctrl+Alt+G or Read
   again (available in both control windows). Verify the existing region is reused.
   Record warm timings across ten paragraphs; check accuracy and game responsiveness.
5. Crop a blank area: expect a no-readable-text message, not the previous dialogue.
6. Rapidly choose different crops / press Read again, including during the first
   model load. Only the newest crop may produce a result; UI must remain responsive.
7. Stop capture, switch target, or close the target during a read. Previous text
   must clear and must not reappear. Exit during a read without crashing.
8. With the app closed, temporarily rename `models/v5`, launch and read: expect an
   actionable error. Restore the folder and retry; do not expect a download.
9. Confirm the Chinese overlay still contains filler and clicks pass through.
   Repeat the existing minimize, alt-tab, resize, DPI, and hotkey-conflict checks.

The milestone is pending Windows validation. Record game/window mode, CPU, crop
pixel size, cold/warm timings, recognition errors, and observed game impact. A
clean build or Mac OCR smoke test cannot establish Windows capture correctness.

## Milestone 3 — real translation and continuous detection

The overlay now starts empty rather than with filler. Follow the
[Milestone 3 acceptance checklist](MILESTONE-3.md#windows-acceptance-checklist-user-validation)
for key entry/storage, Luna connection, Auto read, cancellation, and request limits.
Earlier filler-text checks apply only to the historical milestone 1/2 builds.
