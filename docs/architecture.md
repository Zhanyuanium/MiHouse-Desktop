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
miot-spec JSON under `%LOCALAPPDATA%\MiHouse\cache\spec`, keyed by SHA-256. The instance index is
treated as fresh for 24 hours, and stale data is used when the network fails.

`SnapshotStore` keeps 30-day structural snapshots of homes, devices and scenes under
`%LOCALAPPDATA%\MiHouse\cache\snapshots\<userId>`. Snapshots never restore property values.

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
