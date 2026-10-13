# Pen Pan/Scroll for OpenTabletDriver

Wacom-style pen button-pan/scroll for [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) on **macOS**.

Hold a pen button, touch the nib, and drag: the pointer stays exactly where it was and the screen scrolls — both axes, natural direction (drag up = scroll down). Lifting the nib pauses without jumping; release the button for normal pointing.

Fork for Matheus Santiago: supports **Absolute and Relative Mode** on the native ARM64 OpenTabletDriver 0.6.7 development build targeting .NET 10. Original plugin developed by Oliver Iborra-MacDonald; original hardware test used a Wacom PTK-470.

In Relative Mode, the filter keeps a virtual coordinate offset. Releasing the scroll button rebases that offset, so the input accumulated during scrolling never becomes a cursor jump. Proximity return also rebases the next sample.

Optional contact clicks: set **Contact click button** to a different pen button and choose **Right** or **Middle**. Holding that side button alone never clicks. Touching the nib replaces the left click with the chosen click; lifting releases it. Releasing the side button while touching releases the click and suppresses left click until the nib lifts. Scroll has priority if both buttons are held. Zero disables contact-click substitution.

## Control + Command zoom

Enable **Control + Command zoom**: hold Control and Command, touch the nib and drag vertically. Sends pixel scroll with Command for application zoom, as documented for FigJam on Mac. Horizontal movement is ignored. Control alone continues normal scroll with cleared flags. Shift selects Mission gestures and Option selects middle click. Lift before changing chords; one contact stroke cannot switch between zoom and scroll.

The desktop application must support Command-scroll zoom. Automated tests check routing and modifier flags; physical testing in the target app is still needed.

## Control + Option middle click

Enable **Control + Option middle click**: hold Control and Option, then touch the nib to hold the middle mouse button. Moving the nib drags normally; lifting releases. Hover alone does not click. This chord takes priority over Control scroll and Mission gestures. Releasing Option while touching releases middle click and suppresses left click until lift; it cannot become scroll in the same stroke. Reads left/right Option from physical key state.

## Keyboard gesture mapping

Set **Scroll trigger** to **Control**: Control + nib drag scrolls with the cursor anchored. Enable **Control + Shift Mission gestures**: Control + Shift + nib drag opens/dismisses Mission Control or switches Spaces. The two chords are mutually exclusive. Hover never scrolls. Lift the nib before switching modes; changing Shift while touching cannot fire the other action.

Reads left/right modifier keys from the physical HID key state instead of flags on synthetic events. Keyboard-triggered scroll posts pixel scroll events with modifier flags cleared, avoiding application zoom or Shift axis switching. The original **Pen button** trigger remains available.

## Relative Pointer Acceleration

Enable the separate **Relative Pointer Acceleration** filter in the same plugin package. It runs after OTD transforms relative motion and scales only pointer deltas; pen scrolling and Mission Control gestures retain their own sensitivity/distance. Absolute Mode is passed through.

| Control | Default | Meaning |
|---|---|---|
| Acceleration starts at | 20 mm/s | Below this physical speed, retain base Relative Mode sensitivity |
| Maximum gain at | 150 mm/s | Speed at which the velocity curve reaches maximum gain |
| Maximum gain | 2× | Upper sensitivity multiplier; 1 disables acceleration |
| Speed smoothing | 20 ms | Smooth speed estimate without buffering pointer samples; 0 is instantaneous |
| Travel before acceleration | 3 mm | Require net travel beyond this distance; ramp distance gain from 3 to 6 mm; 0 uses velocity alone |
| Motion window | 100 ms | Recent displacement window; back-and-forth movement cancels rather than adding path length |

Both speed and distance gates must engage to produce acceleration. Short fast writing strokes stay at base sensitivity when net travel stays below 3 mm. Larger writing strokes can still accelerate; increase Travel before acceleration to suit your handwriting. Lift/proximity loss and long report gaps reset the speed estimate. Base sensitivity remains configured in Relative Mode.

## Control + nib gestures

Enable **Control + Shift Mission gestures** to replace Control-click with navigation:

- Hold Control + Shift, touch and drag up: Mission Control (`Control-Up`).
- Drag right: switch to the Space on the left (`Control-Left`).
- Drag left: switch to the Space on the right (`Control-Right`).

The cursor stays anchored, clicks and scroll are suppressed, and each contact stroke triggers at most one action after **Mission gesture distance** (default 5 mm). Lift and touch again for another action. Drag down to dismiss Mission Control with Escape. Releasing either modifier while touching suppresses the nib click until lift and resumes relative pointing without a jump.

Uses macOS keyboard shortcuts, which must be enabled under System Settings > Keyboard > Keyboard Shortcuts > Mission Control. This triggers the system transition after the distance threshold; it does not drive an interactive trackpad-style animation. Control alone selects scrolling; pen-button gestures retain their own bindings.

## Platform support

| Platform | Status |
|---|---|
| macOS | Supported — pixel-precise scrolling via OTD's macOS pointer backend |
| Windows | Inactive by design — the filter logs "Inactive" and passes all input through. OTD's Windows pointer backend (`WindowsVirtualMouse`) exposes no scroll API, so Windows support would be a separate implementation (see Forking below) |
| Linux | Inactive by design — the Linux scroll backend was not verified for this plugin, and the code gates to macOS anyway, so it passes input through. PRs welcome (see Forking below) |

The plugin never blocks or alters input on unsupported platforms: worst case, nothing happens.

## Forking for Windows / Linux

MIT-licensed — fork freely, no permission needed (keep the copyright notice). What's portable and what isn't:

- **Portable as-is:** the filter pipeline (`IPositionedPipelineElement` at `PreTransform`), tablet geometry + Absolute/Relative Mode, and all gesture math (anchor, pressure-zeroing, fractional accumulation). ~90% of the code.
- **Not portable:** the scroll sink. The filter casts OTD's live pointer to `IMouseScrollHandler`. macOS implements it with pixel precision; Windows does not implement it at all (verified in OTD source); Linux is unverified and the current code's `OperatingSystem.IsMacOS()` gate blocks it regardless.
- **What a port needs:** remove/extend the OS gate, then emit scroll through the target OS — on Windows that's `SendInput` `MOUSEEVENTF_WHEEL`/`HWHEEL` in `WHEEL_DELTA` (120) units rather than pixels, so sensitivity needs re-scaling; on Linux it means checking the current backend's name/path and emitting the equivalent wheel events. Either way it needs a real tablet + OTD 0.6.7 on that OS to verify feel — no Mac-side test can prove it.

## How it works (short version)

The filter sits in OTD's input pipeline before coordinate transformation. While the chosen pen button is held and the nib is touching:

- the pointer **anchors** (position frozen, pressure zeroed — no accidental clicks or drags),
- pen travel is converted to scroll pixels (`pixels = mm × sensitivity`), with sub-pixel motion accumulated so slow drags stay smooth,
- both scroll axes queue on OTD's own macOS virtual mouse and flush through its normal output cycle — no second, conflicting pointer.

Direction is tuned to macOS natural scrolling: upward pen motion scrolls the page down. Each axis is independently reversible and can be disabled (sensitivity 0).

## Install

1. OpenTabletDriver **0.6.7** on macOS, in **Absolute or Relative Mode**.
2. Either:
   - **Plugins tab** of OpenTabletDriver → drag and drop `PenPanScroll-1.1.0.zip` from the [Releases](https://github.com/djoliverm/otd-penpanscroll/releases), **or**
   - close OTD, create `~/Library/Application Support/OpenTabletDriver/Plugins/PenPanScroll/` and put `PenPanScroll.dll` inside it (any folder name works; version lives in the plugin metadata, not the folder).
3. Restart OTD → **Filters** → enable **Pen Pan/Scroll** → set your scroll button → **Save**.
4. Optionally drop a `metadata.json` next to the DLL (see `metadata.json` in this repo) so the Plugin Manager shows owner/license/description.

> Keep the filter first in the filter list if you have other filters that alter position, pressure, or buttons.

## Use

1. Point at the scrollable content.
2. Hold pen button 1 (default) and touch the nib to the tablet.
3. Drag up to scroll down, sideways to scroll horizontally.
4. Lift the nib to reposition; touch down for another stroke.
5. Release the button to resume ordinary pointing. If the nib is still down, lift it before clicking again.

Other pen buttons keep their existing bindings unless selected as the contact-click button. There is no momentum; the app must support normal mouse-wheel/pixel scrolling.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| Scroll button | 1 | One-based pen button that triggers panning (see OTD's debugger to identify). Its normal binding is suppressed while panning |
| Horizontal / Vertical sensitivity | 10 px/mm | Scroll pixels per mm of pen travel. 0 disables that axis. 0–1000 |
| Reverse horizontal / vertical | off | Invert that axis |
| Contact click button | 0 | Pen button selecting a contact click; 0 disables; must differ from Scroll button |
| Contact click type | Right | Replace nib left click with Right or Middle while chosen button is held |

## Build & test

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and matching OpenTabletDriver assemblies from the .NET 10 ARM64 build. No NuGet packages are downloaded (references come from the installed app).

```sh
dotnet build src/PenPanScroll.csproj -c Release -p:OtdDirectory=/path/to/OTD/assemblies
dotnet run --project tests -c Release -p:OtdDirectory=/path/to/OTD/assemblies
```

For a self-contained single-file app, use the source build directory `OpenTabletDriver.Desktop/bin/Release/net10.0` containing the matching assemblies. Pass `-p:OtdDirectory=/absolute/path/to/OpenTabletDriver.app/Contents/MacOS`. The compiled plugin is `src/bin/Release/net10.0/PenPanScroll.dll`. For direct installation, package that DLL together with the root `metadata.json`; keep both files at the archive's top level.

The 35 tests drive the real OTD report, binding, absolute-output, and relative-output classes with a recording pointer: direction, anchoring, fractional accumulation, lift/re-contact, both button-release orders, right-click preservation, proximity loss, disposal, and fail-open behavior. They do not substitute for physical tablet testing.

## Releasing a new version

1. Bump `<Version>` in `src/PenPanScroll.csproj` and `PluginVersion` in `metadata.json` (four-part, e.g. `1.1.0.0`).
2. `dotnet build src -c Release`, then package the DLL into a zip named after the release (DLL at the archive's top level) and upload it to a GitHub Release.
3. If the plugin is in the OTD marketplace, update `DownloadUrl`/`SHA256`/`PluginVersion` in `marketplace/` and open the PR.

## Sharing via the OTD marketplace

`marketplace/` contains the metadata JSON for the [OpenTabletDriver Plugin Repository](https://github.com/OpenTabletDriver/Plugin-Repository) (optional — the repo + Releases work standalone). File path in the marketplace: `Repository/{DriverVersion}/{Owner}/{Repo}/PenPanScroll.json`.

## License

[MIT](LICENSE) — do whatever you want, no warranty. OpenTabletDriver itself is LGPL-3.0; this plugin only references its public API.
