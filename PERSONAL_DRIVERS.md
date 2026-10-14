# Personal driver build

This fork is the single maintained repository for Matheus Santiago's native ARM64 macOS tablet setup.

| Directory | Purpose | Original source | Imported commit |
|---|---|---|---|
| Driver at repository root | OpenTabletDriver, followed via `upstream` | https://github.com/OpenTabletDriver/OpenTabletDriver | `35b2e84e` |
| `plugins/PenPanScroll` | Scroll, Mission Control, middle click, zoom, acceleration and diagnostics | https://github.com/djoliverm/otd-penpanscroll | Customized fork `f42d4df` |
| `third-party/HIDSharpCore` | HID dependency with exclusive capture for Bosto BT-12HD on macOS | https://github.com/OpenTabletDriver/HIDSharpCore | Patched commit `1f8cad2` |

Both imports preserve their Git history. Their existing license and copyright notices remain in their own directories. The driver uses the in-tree HID dependency by default; `-p:UseLocalHidSharp=false` selects the upstream NuGet package for comparison. No sibling clones or submodules are needed.

Only the driver upstream is followed routinely. Plugins and HID changes are maintained directly in this repository; their original sources are provenance, not update channels. Existing standalone clones and the old plugin fork remain historical backups and are not needed for new builds.

## Build and test

Requires native ARM64 .NET 10 SDK. Run from the repository root:

```sh
dotnet build OpenTabletDriver.Desktop/OpenTabletDriver.Desktop.csproj -c Release
dotnet run --project plugins/PenPanScroll/tests -c Release
```

The plugin automatically references the matching driver assemblies built above. Its DLL is `plugins/PenPanScroll/src/bin/Release/net10.0/PenPanScroll.dll`; install it with its `metadata.json` in `~/Library/Application Support/OpenTabletDriver/Plugins/PenPanScroll/` while the app is closed.

To package the native driver, use GNU Bash and GNU coreutils on PATH:

```sh
bash ./eng/bash/package.sh --signed true --runtime osx-arm64 --package macos
```

The existing package script creates `dist/OpenTabletDriver.app`. The plugin remains a separate build/install artifact in the same repository; its app-data configuration is not stored in Git. Re-signing/replacing the driver may require re-adding macOS Input Monitoring and Device Control and Data Access permissions. Updating the plugin alone retains the app signature.
