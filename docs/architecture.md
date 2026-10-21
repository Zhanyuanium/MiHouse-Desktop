# Architecture

MiHouse-Desktop is a Windows-native client for Xiaomi smart-home devices. It uses the account
and cloud protocols of the Mi Home Android app and renders its own UI from `miot-spec` metadata.

## Boundaries

```
MiHouse.App (WinUI 3)
        │  views, view models, DPAPI credential store, composition root
        ▼
MiHouse.Core (net10.0, no UI dependencies)
   Auth      account login and the credential-store contract
   Cloud     signed HTTP client and Mi Home endpoints
   Spec      miot-spec fetch / parse / in-memory cache
   Lan       miIO over UDP (`ILanTransport`, `MiIoLanTransport`)
   Devices   inventory, property/action orchestration, snapshots, transport selection
```

`MiHouse.Core` must stay free of WinUI types so it can be unit tested and reused by a future
headless host. The DPAPI implementation of `ICredentialStore` lives in `MiHouse.App`.

## Caching and storage

There is no database. `SpecCatalog` keeps parsed specs in memory, and `SpecDiskCache` writes raw
miot-spec JSON under `<LocalCache>\spec`, keyed by SHA-256. The instance index is treated as fresh
for 24 hours, and stale data is used when the network fails.

`SnapshotStore` keeps 30-day structural snapshots of homes, devices and scenes under
`<LocalCache>\snapshots\<userId>`. Snapshots never restore property values.

All persisted state lives inside the MSIX package data folders, so uninstalling the package
removes it:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\
    LocalState\        session.dat, device-id, logs\crash-*.log
    LocalCache\        spec\, snapshots\<userId>\
```

`MiHouse.App.Storage.AppStorage` resolves those two directories once from
`ApplicationData.Current.LocalFolder` and `LocalCacheFolder`. `MiHouse.Core` never resolves paths
itself: every component that persists something takes an explicit directory, so a component
cannot fall back to a location outside the package. Only an unpackaged development run (no
package identity, so `ApplicationData.Current` throws) falls back to
`%LOCALAPPDATA%\MiHouse-Unpackaged`.

## Trust and capabilities

`Package.appxmanifest` declares `uap10:TrustLevel="appContainer"` with
`uap10:RuntimeBehavior="packagedClassicApp"` and the literal entry point
`Windows.PartialTrustApplication`; the `$targetentrypoint$` token expands to
`Windows.FullTrustApplication`, which the validator rejects for this configuration. The capability
list is exactly two entries: `internetClient` for cloud HTTPS, and `privateNetworkClientServer` for
miIO UDP to LAN devices and local DNS. The package does not declare `runFullTrust`.

LAN traffic is outbound UDP only; the app opens no listening socket, so no inbound firewall rule is
involved.

## Auth

QR login is the default flow: `StartQrLoginAsync` returns a ticket, and `PollQrLoginAsync`
long-polls it. Password login posts to `serviceLoginAuth2`. If the response contains a
`notificationUrl`, the account requires two-factor authentication and `MiAuthException` is
thrown; those accounts must use QR login. A successful login yields a `MiHomeSession`, which
`ICredentialStore` persists (DPAPI in the app).

## Decisions

| Decision | Rationale |
| --- | --- |
| .NET 10 + WinUI 3 (Windows App SDK) | Native Windows UI, credential APIs and MSIX packaging without a web runtime. |
| CommunityToolkit.Mvvm | Source generators replace hand-written `INotifyPropertyChanged`. |
| `Microsoft.Extensions.DependencyInjection` + `Logging` | Explicit composition, no `Generic Host` ceremony. |
| Hand-written RC4 | Not in `System.Security.Cryptography`; small and covered by known-answer tests. |
| DPAPI for tokens | Tokens never leave the user profile in plaintext. |
| MSIX `appContainer` trust (no `runFullTrust`) | The package is a `packagedClassicApp` with `uap10:TrustLevel="appContainer"`, so Windows shows only the two network capabilities it really uses; `runFullTrust` (rendered as "uses all system resources") is dropped. |
| Package data folders for persistence | `AppStorage` resolves `LocalFolder` and `LocalCacheFolder`; nothing is written outside the package, so uninstall removes all app data. |
| DPAPI in the AppContainer | `ProtectedData` with `DataProtectionScope.CurrentUser` was verified to round-trip inside the AppContainer; no WinRT credential API is needed. |

## Scope

First release targets Wi-Fi devices only (lights, plugs, air conditioners, sensors).
BLE, Zigbee sub-devices, camera P2P and Mi Home plugin UIs are explicitly out of scope.
`DeviceController` selects `ILanTransport` when a private IP and token are available and falls
back to `IMiHomeCloudClient`; there is no generic transport abstraction.

## Threading

Network requests are asynchronous. The spec catalog's in-memory index and disk cache, and
`SnapshotStore`'s home/device/scene snapshots, are synchronous: they read or write small local
files inline and return before the caller continues. UI updates flow through the dispatcher;
Core never touches UI threads.
