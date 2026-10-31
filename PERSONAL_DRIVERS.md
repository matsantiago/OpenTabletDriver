# Personal driver build

This fork is the single maintained repository for Matheus Santiago's native ARM64 macOS tablet setup.

| Directory | Purpose | Original source | Imported commit |
|---|---|---|---|
| Driver at repository root | OpenTabletDriver, followed via `upstream` | https://github.com/OpenTabletDriver/OpenTabletDriver | `f3927ac1` (v0.6.8.1) |
| `plugins/PenPanScroll` | Scroll, Mission Control, middle click, zoom, acceleration and diagnostics | https://github.com/djoliverm/otd-penpanscroll | Customized fork `f42d4df` |
| `third-party/HIDSharpCore` | HID dependency with exclusive capture for Bosto BT-12HD on macOS | https://github.com/OpenTabletDriver/HIDSharpCore | Patched commit `1f8cad2` |

Both imports preserve their Git history. Their existing license and copyright notices remain in their own directories. The driver uses the in-tree HID dependency by default; `-p:UseLocalHidSharp=false` selects the upstream NuGet package for comparison. No sibling clones or submodules are needed.

Only the driver upstream is followed routinely. Plugins and HID changes are maintained directly in this repository; their original sources are provenance, not update channels. Existing standalone clones and the old plugin fork remain historical backups and are not needed for new builds.

The v0.6.8.1 upstream merge retains the customized PenPanScroll sources and the in-tree HID dependency, including exclusive capture for the Bosto BT-12HD. Rebuild the plugin against the updated driver assemblies before installing both.

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

The existing package script creates `dist/OpenTabletDriver.app`. The plugin remains a separate build/install artifact in the same repository; its app-data configuration is not stored in Git. Updating the plugin alone retains the app signature.

## Persistent signing on this Mac

For personal builds, configure a local signing identity once:

```sh
python3 scripts/setup-macos-signing.py
```

macOS may ask you to authorize trusting the certificate for code signing. The private key is imported into your default Keychain, accessible to `/usr/bin/codesign`; temporary key files are removed. The public certificate and its fingerprint are saved under `~/Library/Application Support/OpenTabletDriver/Signing/`, outside Git. This local certificate is for personal builds, not notarized distribution to other Macs.

The normal `--signed true` package command automatically reuses that identity. `OTD_CODESIGN_IDENTITY` can override it with an existing signing identity, including a Developer ID identity. A configured identity that is unavailable causes packaging to fail rather than falling back to ad-hoc signing. Machines without a configured identity retain upstream's ad-hoc signing behavior.

After switching from ad-hoc signing, re-add the app in macOS Input Monitoring and Accessibility once. Subsequent builds signed with the same certificate and identifiers should retain those grants. Keep the certificate and its private key: recreating the identity, losing the key or switching signing identities can require granting permissions again. The local certificate is valid for ten years.
