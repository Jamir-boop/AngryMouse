---
name: angrymouse-build-release
description: Build, verify, package, commit, push, and publish AngryMouse releases. Use when working in C:\Users\superuser\Documents\AngryMouse and the user asks how AngryMouse was built/tested, asks to build/test AngryMouse, asks to create a release zip from the ClickOnce publish folder, or asks to commit/push/upload AngryMouse release assets.
---

# AngryMouse Build Release

## Overview

Use this workflow for AngryMouse build verification and release packaging. Keep source edits separate from release actions: commit, push, tag, and GitHub release upload only when the user explicitly asks.

## Tools

- Terminal commands in this repo should be prefixed with `rtk` when available.
- Use solution cwd: `C:\Users\superuser\Documents\AngryMouse`.
- Use MSBuild binary: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`.
- Use publish root: `C:\Users\superuser\Documents\AngryMouse\AngryMouse\publish`.
- Use GitHub release API with existing `git credential fill` token when `gh` is unavailable. Do not print tokens.

## Build And Test

Run Debug build:

```powershell
rtk 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' AngryMouse.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /m /nologo /v:minimal
```

Treat build as passed only when exit code is `0` and output contains:

```text
AngryMouse -> C:\Users\superuser\Documents\AngryMouse\AngryMouse\bin\Debug\AngryMouse.exe
```

Run whitespace diff check before commit:

```powershell
rtk git diff --check
```

Use manual-test language precisely. Build verifies compile/package integrity only; it does not prove UI behavior. If manual UI verification was not run, say so.

## Release Zip

Create zip only after publish output exists for target version. Expected version folder pattern:

```text
C:\Users\superuser\Documents\AngryMouse\AngryMouse\publish\Application Files\AngryMouse_X_Y_Z_0
```

Zip layout must match prior releases:

```text
AngryMouse.application
AngryMouse.exe
Application Files/AngryMouse_X_Y_Z_0/...
```

For `<version>`, zip path is:

```text
C:\Users\superuser\Documents\AngryMouse\AngryMouse\publish\AngryMouse-<version>.zip
```

Verify the zip by opening it and checking first entries include `AngryMouse.application`, `AngryMouse.exe`, and the correct `Application Files/AngryMouse_X_Y_Z_0/` files.

## Commit Push Release

Before commit:

```powershell
rtk git status --short --branch
rtk git diff --stat
```

Use conventional commits, for example:

```text
fix(cursor): disable hiding in active mode
```

Push `master` after commit only when asked:

```powershell
rtk git push origin master
```

For a versioned release, create and push tag matching app version, then create/update GitHub release asset:

```powershell
rtk git tag <version>
rtk git push origin <version>
```

When replacing release zip, delete existing `AngryMouse-*.zip` asset on that release before uploading the new zip. Verify `/releases/latest` returns the expected tag and asset URL.
