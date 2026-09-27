# Packaging PicRestore for Windows

Run from the repository root on Windows 10 (1809+) or 11 with the .NET 8 SDK:

```powershell
build\package.cmd
```

or, with options:

```powershell
powershell -ExecutionPolicy Bypass -File build\package.ps1 [-Arch x64|arm64] [-Mode SelfContained|FrameworkDependent] [-SkipTests] [-NoAiModel] [-NoSmokeTest]
```

## What it produces (in `artifacts\`)

| File | What it is |
| --- | --- |
| `PicRestore-Setup-<version>-<arch>.exe` | Installer. Per-user by default (no admin prompt), Start menu shortcut, optional desktop shortcut, uninstaller in Settings > Apps. |
| `PicRestore-<version>-win-<arch>-portable.zip` | The same app as a zip. Unzip anywhere and run `PicRestore.App.exe`. |
| `publish\PicRestore-<version>-win-<arch>\` | The unzipped app folder. |

The installer needs Inno Setup 6 on the build machine (`winget install --id JRSoftware.InnoSetup -e`).
Without it, the script still makes the portable build and says so.

## Modes

- **SelfContained** (default): .NET 8 and the Windows App SDK are inside the app folder. The target PC needs nothing else.
- **FrameworkDependent**: .NET is inside the app folder; Setup.exe runs Microsoft's Windows App Runtime installer, which does nothing if the runtime is already there. The portable zip then needs the Windows App Runtime installed separately. Use this mode if the SelfContained build ever fails the smoke test.

## AI model

The LaMa repair model (92 MB, Apache-2.0, OpenCV Zoo) goes in the app's `Models` folder, so the
installed app works offline from the first run. The script reuses the copy in
`%LOCALAPPDATA%\PicRestore\Models` if the app has already downloaded it, otherwise downloads it once
into `artifacts\cache`, and always checks its SHA-256. `-NoAiModel` leaves it out; the app then
downloads it on its first restoration.

## Version

The version comes from `<Version>` in `src/PicRestore.App/PicRestore.App.csproj`. Bump it before
building a new release so the installer upgrades an existing install in place.

## Uninstall

Settings > Apps > PicRestore > Uninstall. Training pairs, the trained model and logs in
`%LOCALAPPDATA%\PicRestore` are kept, so a reinstall or upgrade doesn't lose them. Delete that folder
by hand to remove everything.

## Signing

The installer and exe are unsigned, so Windows SmartScreen shows "Windows protected your PC" on
first run (More info > Run anyway). Signing with a code-signing certificate removes that.
