# AngryMouse

<p align="center">
  <img src="icon.png" alt="AngryMouse icon" width="100" height="100">
</p>

AngryMouse is a Windows utility that makes the pointer grow when you shake the mouse, similar to macOS “shake to locate”. This repository is a detached fork of the retired `Longi94/AngryMouse` project.

## Features

- Activate by shaking the mouse, a configurable keyboard shortcut, or both.
- Choose hold or toggle activation.
- Tune shake sensitivity, visible duration, animation speed, and cursor size.
- Use the current Windows cursor or a PNG cursor collection, with per-role hotspot adjustment.
- Optionally hide the normal cursor while the enlarged collection cursor is visible.
- Light and dark themes, start-with-Windows support, and minimize-to-tray startup.
- Import/export settings packages, optionally including custom cursor collections.
- Multi-monitor and per-monitor DPI support.

## Install

AngryMouse requires Windows with .NET Framework 4.7.2 or later.

1. Download `<version>.zip` from the [latest release](https://github.com/Jamir-boop/AngryMouse/releases/latest).
2. Extract the ZIP.
3. Run `AngryMouse.application` and complete the ClickOnce installation.

ClickOnce automatic update checking is disabled. To update, download the latest release package and run its `AngryMouse.application` again.

## Use

AngryMouse opens its settings window on a normal launch and continues running in the notification area. Configure an activation source, shake the mouse or press the configured shortcut, and use the tray menu to reopen settings or exit.

Custom collections are stored in `%APPDATA%\AngryMouse\CursorCollections`. A collection is a folder of PNG files; use the cursor-role editor to assign images and adjust hotspots. Settings ZIP imports are validated and custom collections are renamed when a name already exists.

## Build

Requirements:

- Visual Studio with the .NET desktop development workload
- .NET Framework 4.7.2 targeting pack
- NuGet package restore

From a Developer PowerShell prompt:

```powershell
nuget restore AngryMouse.sln
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' AngryMouse.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /m
```

The Debug executable is written to `AngryMouse\bin\Debug\AngryMouse.exe`.

Publishing uses the project’s `PublishUrl`/`PublishDir` and writes ClickOnce output to the repository-level `publish\` directory. Release archives are named `publish\<version>.zip` and contain:

```text
AngryMouse.application
AngryMouse.exe
Application Files/AngryMouse_X_Y_Z_0/...
```

## Troubleshooting

- If no enlarged cursor appears, confirm at least one activation source is enabled and reduce the shake speed/turn thresholds.
- If a custom collection is missing or empty, AngryMouse falls back to the bundled Adwaita collection.
- If the normal cursor remains hidden after an abnormal exit, reopening or exiting AngryMouse restores the Windows cursor scheme; Windows mouse settings can also reload it.
- Enable debug logging in Settings when diagnosing a problem. The log is `%APPDATA%\AngryMouse\debug.log` and is capped at 1 MiB.
- A settings-save warning means changes remain active only for the current session; check that `%APPDATA%` is writable before retrying.

## License

See [LICENSE](LICENSE).
