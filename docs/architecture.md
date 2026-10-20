# Architecture

MiHouse-Desktop is a Windows-native client for Xiaomi smart-home devices. It speaks the
same protocols as the Mi Home Android app, but renders its own UI from `miot-spec` metadata.

## Boundaries

```
MiHouse.App (WinUI 3)
        │  view models, views, composition root
        ▼
MiHouse.Core (net10.0, no UI dependencies)
   Auth      account login, token storage
   Cloud     signed HTTP client and Mi Home endpoints
   Spec      miot-spec fetch / parse / cache
   Lan       miIO over UDP
   Devices   device inventory, property/action orchestration, transport selection
   Storage   SQLite cache and migrations
```

`MiHouse.Core` must stay free of WinUI types so it can be unit tested and reused by a future
headless host.

## Decisions

| Decision | Rationale |
| --- | --- |
| .NET 10 + WinUI 3 (Windows App SDK) | Native BLE, credential APIs and MSIX packaging without a web runtime. |
| CommunityToolkit.Mvvm | Source generators replace hand-written `INotifyPropertyChanged`. |
| `Microsoft.Extensions.DependencyInjection` + `Logging` | Explicit composition, no `Generic Host` ceremony. |
| `Microsoft.Data.Sqlite` | Small local cache; EF Core is unnecessary for this schema. |
| Hand-written RC4 | Not in `System.Security.Cryptography`; small and covered by known-answer tests. |
| DPAPI for tokens | Tokens never leave the user profile in plaintext. |

## Scope

First release targets Wi-Fi devices only (lights, plugs, air conditioners, sensors).
BLE, Zigbee sub-devices, camera P2P and Mi Home plugin UIs are explicitly out of scope.
`IDeviceTransport` is the seam for adding them later.

## Threading

All network and storage work is asynchronous. UI updates flow through the dispatcher;
Core never touches UI threads.
