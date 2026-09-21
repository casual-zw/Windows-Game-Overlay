# Build and run the Windows prototype

The prototype uses Chinese filler text. No API key, Luna setup, or AI spending is needed.

## 1. Set up the Windows PC

- Use Windows 11 x64.
- Install the **.NET SDK 10.0.401** (or a later 10.0.4xx patch) from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Install the SDK, not just the runtime. The required version is recorded in `global.json`.
- Copy the source project to the PC. Include `src`, `tests`, `scripts`, `docs`, `global.json`, and `Directory.Build.props`.
- Open PowerShell in the project folder—the folder containing this file.

Check the SDK:

```powershell
dotnet --version
```

The first build needs internet access to restore Microsoft's Windows reference packages. Visual Studio is optional.

## 2. Compile and test

```powershell
dotnet run --project tests/Overlay.Core.Tests/Overlay.Core.Tests.csproj -c Release
dotnet build src/Overlay.Windows/Overlay.Windows.csproj -c Release
```

The tests use a small executable harness: they print PASS/FAIL and return a nonzero exit code if anything fails. They are run with `dotnet run`, not `dotnet test`.

## 3. Run

```powershell
dotnet run --project src/Overlay.Windows/Overlay.Windows.csproj -c Release --no-build
```

Click **Test window**, return to the control window, and click **Start capture**. Switch to the test scene and press **Ctrl+Alt+O** to open the small in-game controls. Click **Select dialogue region**, then drag around the dialogue on the frozen game preview. Releasing the mouse applies the crop and returns to the game. Press **Esc** to cancel without changing the previous region. The controls also have a **Back to game** button; Escape or Ctrl+Alt+O dismisses them. The original preview crop remains available.

- **Ctrl+Alt+T:** show/hide the panel.
- **Ctrl+Alt+O:** open/close the in-game controls, including the selection button.
- **Ctrl+Alt+R:** optional shortcut directly to region selection.
- **Ctrl+Alt+E:** switch between editing its position/size and click-through reading mode.

Use [the Windows checklist](docs/WINDOWS-TEST.md) for input, capture, resize, and real-game checks. Compilation and portable tests do not prove native Windows behavior.

## 4. Create a release EXE and upload it to Google Drive

The release command creates a self-contained, single-file `GameOverlay.exe`, adds the
Windows test instructions, bundles both into a versioned ZIP, and copies that ZIP to a
Google Drive for desktop sync folder. Google Drive uploads it in the background.

On the Windows PC, configure the destination just once:

```powershell
Copy-Item scripts/release.local.psd1.example scripts/release.local.psd1
notepad scripts/release.local.psd1
```

Set `DriveFolder` to the folder that Google Drive for desktop synchronizes, for example
`G:\My Drive\GameOverlay Releases`. Create that folder in Drive or Explorer first.
The local config is ignored by Git, so a personal Drive path is never committed.

After that, each release is one command:

```powershell
./scripts/release.ps1
```

By default the version is a timestamp, so every release is a new file and prior builds
remain in Drive. To set a human-readable version:

```powershell
./scripts/release.ps1 -Version 0.1.0
```

The generated `.exe`, ZIP, checksum metadata, and a copy of the test instructions are
kept under `artifacts/releases/`. Use `-NoUpload` when you only want the local package,
or `-SkipTests` only for an already-validated rebuild.

## 5. Optional: create a portable multi-file folder

Only do this when you want a build that runs without a separate .NET installation:

```powershell
dotnet publish src/Overlay.Windows/Overlay.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/GameOverlay.exe`. Keep **all files in that folder** together. It is a portable, unsigned development build—not an installer. Publishing may download additional Windows runtime packages.

## 6. Optional: make a ZIP yourself

After publishing:

```powershell
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/GameOverlay-win-x64.zip -Force
```

Extract the entire ZIP on the destination PC before running `GameOverlay.exe`. Nothing is uploaded automatically.

## Script shortcuts

```powershell
./scripts/build.ps1           # Tests and compilation only
./scripts/build.ps1 -Publish  # Also creates the portable folder
./scripts/build.ps1 -Zip      # Also creates the folder and ZIP
./scripts/release.ps1         # Versioned single EXE + Drive upload
./scripts/sync-build.ps1      # Sync clean main + tested local release EXE and ZIP
```

`sync-build.ps1` deliberately stops if the checkout is not clean `main`, or if local
`main` is ahead of or diverged from `origin/main`. Resolve that Git state explicitly
rather than building a release that differs from the repository source of truth.

If PowerShell blocks scripts, use the individual `dotnet` commands above; changing execution policy is not necessary.

On macOS, portable tests and Windows cross-compilation are possible with the SDK, but the app itself must run on Windows.
