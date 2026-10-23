# MiHouse

[English](README.md) | [简体中文](README.zh-CN.md)

![MiHouse](docs/images/banner.png)

MiHouse is an unofficial, local-first Windows desktop client for Xiaomi Mi Home accounts, built with WinUI 3 and .NET 10. It speaks the account, cloud and miot-spec protocols used by the official Android app, which were reverse-engineered for this project. It is not affiliated with, endorsed by, or supported by Xiaomi.

The UI is generated from miot-spec metadata instead of the official plugins, so device support follows the published spec rather than the Mi Home plugin catalog. The list below is the implemented surface for 1.0, not a roadmap.

**Upgrading from 0.1.x.** Version 1.0 runs with package identity and keeps its data inside the package data folders. The session and appearance settings of an unpackaged 0.1.x run are not migrated, so sign in once and set your appearance again.

![MiHouse home dashboard](docs/images/home.png)

## Features

- **Sign-in.** QR code or Xiaomi account password. The session is protected with Windows DPAPI for the current user and restored on the next launch. Password sign-in rejects accounts that require two-factor authentication; use the QR flow for those.
- **Homes and rooms.** Owned and shared homes, room membership, and a home switcher in the title bar.
- **Device tiles.** Online state, category icon, power control, and sensor readings on room and category pages; the home dashboard summarises the same devices by class.
- **Category icons.** Every miot-spec class maps to a Material Design Icons glyph. Icons follow the app accent color, or render in a neutral shade when colored icons are turned off.
- **Appearance.** System, light or dark theme; the accent color comes from Windows or from a custom picker, and colored icons and motion can be switched off. Choices persist in `appearance.json` and apply without a restart.
- **Switch channels.** A wall-switch channel that Mi Home marks as a light (cloud `subclass_id`) joins the light class. A switch body whose first channel already has its own row is hidden from room pages, so that channel is not controlled twice.
- **Tri-state class switches.** Each device category shows on, off, or partial. Applying a state targets only controllable online members; offline and read-only devices are skipped.
- **Scenes.** Manual scenes for the selected home; disabled scenes are labelled and every run asks for confirmation.
- **Device details.** Spec-driven properties in normal and advanced sections, with boolean, enum, range and text editors. Edits are written immediately, then verified by a re-read; the optimistic value is marked unconfirmed until the device reports it, bounded to 12 seconds, after which the next successful read wins. Actions also require confirmation.
- **Passive polling.** Home and room cards refresh every 8 seconds; an open device refreshes every 6 seconds, backing off to 12 and 30 seconds after failures. Polling pauses when the window is inactive, and the open device is excluded from the card sweep.
- **LAN-first control.** When a private local IP and device token are available, property reads, writes and actions use miIO over UDP; failures fall back to the cloud and block LAN for that device for five minutes.
- **miot-spec catalog.** Exact device `spec_type` first, then the model index. The instance index is fresh for 24 hours and raw documents are cached on disk; stale data is served when the network fails.
- **Warm start.** Snapshots of homes, devices and scenes paint the structure before the cloud answers. Snapshots expire after 30 days and never restore values, only structure.
- **Diagnostics.** Crash reports contain only whitelisted fields, with no exception messages.

## Screenshots

| | |
|:--:|:--:|
| [![Home dashboard](docs/images/home.png)](docs/images/home.png) | [![Room page](docs/images/room.png)](docs/images/room.png) |
| **Home** | **Room page** |
| [![Device details](docs/images/device.png)](docs/images/device.png) | [![Scenes page](docs/images/scenes.png)](docs/images/scenes.png) |
| **Device details** | **Scenes page** |
| [![Settings page](docs/images/settings.png)](docs/images/settings.png) | [![Narrow layout](docs/images/narrow.png)](docs/images/narrow.png) |
| **Settings page** | **Narrow layout** |

## Requirements

- Windows 11 24H2 (build 26100) or later.
- The .NET 10 SDK to build.
- The `winapp` CLI, version 0.7.1 or later, for running with package identity and for packaging.
- To install the framework-dependent MSIX bundle: the .NET 10 Desktop Runtime and Windows App Runtime 2.5.1 or later, matching the target architecture (x64 or ARM64). `docs/build.md` covers certificate trust and installation.

## Build, run and test

```powershell
dotnet build MiHouse.slnx -c Release
dotnet test MiHouse.slnx -c Release
dotnet format MiHouse.slnx --verify-no-changes

winapp run src\MiHouse.App\MiHouse.App.csproj -c Release --detach
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --arch x64 --arch arm64 --generate-cert
```

`winapp run` builds the app, registers a development package identity and launches it; `--detach` returns to the shell immediately. `winapp package` publishes x64 and ARM64 slices and writes a signed `.msixbundle` plus a self-signed certificate to the current directory. The build is framework-dependent, and `--self-contained` is not used because in winapp it bundles only the Windows App SDK runtime, not .NET. See `docs/build.md` for the full packaging and certificate flow.

## Data and privacy

- **Session.** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\session.dat`, a JSON payload encrypted with DPAPI (`DataProtectionScope.CurrentUser`). Signing out deletes it; a corrupt file is treated as signed out.
- **Caches.** miot-spec documents live in `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\spec`, keyed by SHA-256. Warm-start snapshots live in `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\snapshots\<userId>`. All persisted data stays inside the package data folders, so uninstalling the package removes it.
- **Appearance.** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\appearance.json`: plain JSON holding the theme mode, accent source and color, colored-icon and motion switches.
- **Network.** Sign-in talks to `account.xiaomi.com` and `sts.api.io.mi.com`; signed API calls go to `api.io.mi.com` or `<region>.api.io.mi.com`; spec definitions come from `miot-spec.org`. LAN control sends miIO UDP packets directly to the device. No telemetry or analytics is implemented.
- **Capabilities.** The package declares only `internetClient` (cloud HTTPS) and `privateNetworkClientServer` (miIO UDP to LAN devices and local DNS). It holds no broad system-resource permission such as `runFullTrust`.
- **Crash logs.** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\logs\crash-*.log`, newest 20 kept. The format is a whitelist: timestamp, app version, source, exception type, inner-exception type names, a reflection-built stack, and `<not recorded>` in place of any message.

## Known limitations

- **Polling only.** The client uses the legacy Mi Home cloud session and does not subscribe to the SSE or MQTT push channels, so changes made in the Mi Home app appear on the next poll.
- **LAN path unverified.** The miIO transport exists and is unit-tested, but it has not been verified against real hardware; cloud control is the tested path.
- **Official plugins are not reusable.** Capabilities that exist only in Mi Home's React Native plugins have no equivalent here.
- **Generic fallback.** Devices outside the supported spec families render a generic property list; models without a miot-spec entry show an explicit "no definition" message.
- **Placeholders.** Favorites and activity are not implemented, and the account, family, notification, consumable and about entries in Settings are tagged 未实现.
- **Scope.** BLE devices, Zigbee sub-devices, camera P2P and vendor-specific plugin UIs are out of scope for this release.

## Architecture

```
src/MiHouse.App/       WinUI 3 shell, views and view models, DPAPI store, crash log
src/MiHouse.Core/      Auth, signed cloud client, spec catalog, miIO LAN, device control
tests/MiHouse.Core.Tests/  xUnit tests for the Core layer
```

`MiHouse.Core` has no WinUI dependency. Its cloud layer signs and encrypts requests; the spec layer resolves and caches miot-spec definitions; the device layer picks a transport and enforces split-channel addressing. The app layer composes these services with `Microsoft.Extensions.DependencyInjection` and keeps network work off the UI thread.

Caching is layered: in-memory caches, a hashed disk cache for raw spec JSON, and per-user structural snapshots for homes, devices and scenes. Snapshots are read only for structure, so stale values are never written back to a device.

In one line: sign in, load homes and the selected home's devices and scenes, render tiles and spec-driven details, then write edits immediately through LAN or cloud and confirm them with a read-back.

More detail: `docs/architecture.md`, `docs/protocol-notes.md`, `docs/build.md`.

## License

Released under the **GNU General Public License v3.0 or later** (GPL-3.0-or-later); the full text is in [`LICENSE`](LICENSE). Modified versions must be distributed under the same license with corresponding source. The project comes with no warranty. `MiHouse` and its Chinese name `米耗子` are not affiliated with Xiaomi; Xiaomi and Mi Home are trademarks of their respective owners.

Device category icons come from Material Design Icons (Apache-2.0, Pictogrammers). The license text and the list of glyphs ship with the package; see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
