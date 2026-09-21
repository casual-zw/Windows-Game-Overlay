# Windows foundation — first test

This prototype displays **fixed Chinese sample text**. It does not translate anything yet. It makes no AI requests and does not save screen images.

## Start

1. Follow `BUILD.md` to compile the source on a Windows 11 x64 PC. A release ZIP is not required.
2. Run `dotnet run --project src/Overlay.Windows/Overlay.Windows.csproj -c Release --no-build` from the project root. If you made a self-contained publish folder instead, run its `GameOverlay.exe`. No API key or administrator permission is needed.
3. Click **Test window**, then return to the control window. The test scene should be selected in the list.
4. Click **Start capture**. Wait for the scene preview and drag a rectangle around its English dialogue. The right-hand crop should match your selection.
5. Switch to the test scene. A Chinese panel should appear. A Windows capture border may also appear.

## Basic checks

- [ ] Preview is live: click **Next line / typewriter** in the test scene.
- [ ] After the typewriter stops, the preview includes the final characters even when nothing else changes.
- [ ] Chinese characters are readable rather than empty squares. The panel says it is sample text.
- [ ] Press **Ctrl+Alt+E** to edit. Drag the panel header and resize its edges. Place it over the click-through test button.
- [ ] Press **Ctrl+Alt+E** again to finish. Click through the Chinese panel onto the scene's button. Its counter must increase without the panel stealing focus.
- [ ] **Ctrl+Alt+T** hides and restores the panel. If the control window reports a hotkey conflict, use its buttons instead.
- [ ] Move/resize the scene. The panel follows; the selected crop scales. Reselect if dialogue layout changes.
- [ ] Alt-tab to another application: the panel hides. Return to the scene: it returns if enabled.
- [ ] Minimize/restore the scene: the panel hides/restores and capture recovers.
- [ ] While editing, return to the control window and confirm the panel does not appear inside the captured scene preview.
- [ ] Close the scene: capture stops and the panel disappears. Open another scene and start again.
- [ ] Stop capture and exit the app: no panel is left behind.
- [ ] Launch again and close immediately without starting capture: exit cleanly without an error.

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
