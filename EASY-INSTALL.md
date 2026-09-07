# Easy Installer fork

This fork keeps the original Dimensions Recompiled installer and adds a recommended one-screen setup path.

## What the easy installer does

1. Checks that Windows is 64-bit and the CPU exposes AVX2.
2. Searches common game/download folders for an already-extracted LEGO Dimensions Xbox 360 disc, Title Update 23, and optional DLC.
3. Accepts either an extracted disc folder **or the user's own Xbox 360 ISO**.
4. If an ISO is selected, downloads the official Windows x64 build of [XboxDev/extract-xiso](https://github.com/XboxDev/extract-xiso), extracts the image to a temporary folder, and validates the extracted `Default.xex` before installing anything.
5. Validates the disc title/media ID and validates Title Update 23 by the exact hash already used by the upstream installer. TU24 and other updates are rejected.
6. Detects optional DLC and installs it using the existing package/header handling.
7. Installs the recompilation, Toy Pad companion app, mods, configuration, updater, and desktop shortcut with recommended defaults when those components are present in the release payload.
8. Checks free disk space and installs outside Program Files by default.
9. Points automatic update checks at `RadiantDelux/DimensionsRecomp` for easy-installer installations.

The original multi-page setup remains available through **Advanced...** or by running:

```text
DimensionsRecompiled-Setup.exe --advanced
```

## What it does not download

The installer does **not** download LEGO Dimensions, Title Update 23, or DLC. Those files must come from the user. The release payload contains the public recompilation binaries and redistributable companion tools only.

## Building

The normal host can be checked with:

```powershell
dotnet build rexlego-installer/Setup.csproj -c Release
```

For a complete installer using the current public upstream release payload:

```powershell
./rexlego-installer/build-fork-installer.ps1
```

The result is:

```text
rexlego-installer/fork-dist/DimensionsRecompiled-Setup.exe
```

GitHub Actions also builds this automatically and publishes the current executable in the rolling `easy-installer` prerelease.
