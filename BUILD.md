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

Click **Test window**, return to the control window, click **Start capture**, then drag a rectangle around the dialogue preview. Switch back to the test scene to see the Chinese overlay.

- **Ctrl+Alt+T:** show/hide the panel.
- **Ctrl+Alt+E:** switch between editing its position/size and click-through reading mode.

Use [the Windows checklist](docs/WINDOWS-TEST.md) for input, capture, resize, and real-game checks. Compilation and portable tests do not prove native Windows behavior.

## 4. Optional: create a portable folder

Only do this when you want a build that runs without a separate .NET installation:

```powershell
dotnet publish src/Overlay.Windows/Overlay.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/GameOverlay.exe`. Keep **all files in that folder** together. It is a portable, unsigned development build—not an installer. Publishing may download additional Windows runtime packages.

## 5. Optional: make a ZIP yourself

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
```

If PowerShell blocks scripts, use the individual `dotnet` commands above; changing execution policy is not necessary.

On macOS, portable tests and Windows cross-compilation are possible with the SDK, but the app itself must run on Windows.
