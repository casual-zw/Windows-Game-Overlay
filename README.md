# Windows Game Overlay

Milestone 3 prototype: local English OCR, GPT-5.6 Luna translation through the Responses API, and a configurable target selector initially offering Simplified Chinese. Manual reading and continuous detection are supported. Translation and Auto read start disabled.

Open **Translation settings**, paste your own API key, and Apply. Optionally test the connection (one small paid request). Enable translation, select a game region, then use **Read again / Ctrl+Alt+G** or **Auto read**. Credentials stay in memory unless you select **Remember on this Windows account**; saved keys use user-scoped Windows DPAPI outside the repository. No key is included in builds.

See [Milestone 3](docs/MILESTONE-3.md) for behavior, limits, and validation. The [Product Spec](https://docs.google.com/document/d/1K6fwV27IizWI8eOFBRgUhrPH0rRftg33pmEKkFCIusw/edit) remains the product source of truth. See [Windows validation](docs/WINDOWS-TEST.md) for the native capture checks.

## Run on Windows

Start with [BUILD.md](BUILD.md) for the short compile, run, and optional packaging instructions. No ZIP or uploaded release has been produced. The prototype targets Windows 11 x64.

For a source build, install the [.NET SDK 10.0.401](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) or a later patch in the 10.0.4xx SDK feature band, then:

```powershell
./scripts/build.ps1
dotnet run --project src/Overlay.Windows/Overlay.Windows.csproj -c Release
```

For a repeatable Windows release, configure a Google Drive for desktop sync folder once
and then run `./scripts/release.ps1`. It produces a versioned, self-contained single
EXE with a required `models` folder and uploads a ZIP to that Drive folder. See [BUILD.md](BUILD.md#4-create-a-release-exe-and-upload-it-to-google-drive).
Optionally create a self-contained folder with `./scripts/build.ps1 -Publish`, or a folder and ZIP with `./scripts/build.ps1 -Zip`. Neither command uploads anything.

To fast-forward a clean `main` checkout to `origin/main`, run the tests, and create a
local single-file EXE plus ZIP in one step, use `./scripts/sync-build.ps1`.

## Architecture

- `src/Overlay.Core`: normalized crop coordinates, preview letterboxing, and overlay visibility policy. No Windows dependencies.
- `src/Overlay.Ocr`: CPU-only PP-OCRv5 mobile Latin models through RapidOcrNet 4.2.0; models load once per session.
- `src/Overlay.Translation`: Luna HTTP adapter, bounded translation queue, session cache, request accounting, language catalog, and text stabilization.
- `tests/Overlay.Translation.Tests`: deterministic fake-transport and sequencing tests; no paid calls.
- `tests/Overlay.Ocr.Tests`: real-model paragraph, blank-image, and cancellation smoke tests.
- `src/Overlay.Windows`: WPF UI, window enumeration, Windows Graphics Capture, D3D11 interop, native overlay styles, and hotkeys.
- `tests/Overlay.Core.Tests`: executable assertion harness; returns nonzero on failure, with no third-party test dependencies.
- `.github/workflows/windows.yml`: Windows build/test workflow, ready when this repository has a GitHub remote. It does not package or upload artifacts. No workflow run is implied by this file's presence.

```sh
dotnet run --project tests/Overlay.Core.Tests/Overlay.Core.Tests.csproj -c Release
dotnet build src/Overlay.Windows/Overlay.Windows.csproj -c Release
```

On macOS/Linux, the SDK can cross-compile the Windows project (`EnableWindowsTargeting=true`); it cannot execute WPF or prove native capture and input behavior. The SDK downloads the Windows reference packs during restore.

The preview reads at most five frames per second and only retains the latest frame. GPU-to-CPU copying is deliberately simple for this proof of concept and needs performance measurement on Windows. The selected region is stored as a fraction of the captured frame, not screen pixels. The overlay follows the target window using physical screen coordinates and a per-monitor-DPI-aware manifest.

## Current boundaries

Windowed/borderless games only; ordinary SDR displays are the initial test target. HDR, exclusive fullscreen, protected windows, multi-instance operation, and remote-display/GPU compatibility are not established. Settings are session-only except optionally remembered API credentials. Fixed hotkeys have conflict detection and button fallbacks. A resize that changes the game's layout may require region reselection.

Windows Graphics Capture may display a system capture border. Capture failure or a black game preview is an unsupported-configuration result to investigate, not an instruction to bypass a game's protections.

## API references

- [Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
- [Capture a specific HWND](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
- [Free-threaded frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)
- [Capture exclusion](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)
- [Layered windows and click-through behavior](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
