# Protocol notes

Findings from static analysis of the official Mi Home APK, used as the specification for
this client. Everything here is an observation from the shipped artifact unless marked
otherwise.

## Artifact

| Field | Value |
| --- | --- |
| Package | `com.xiaomi.smarthome` |
| Version | 11.7.709 (versionCode 110717091) |
| Source | `app.mi.com/details?id=com.xiaomi.smarthome` → `fga1.market.xiaomi.com/.../com.xiaomi.smarthome.apk` |
| Size | 244,510,121 bytes |
| SHA-256 | `73335d3c17dff3e73097db7e2f86a373b7eb2c67611dc82749883ab2da3f0b6e` |
| SDK | minSdk 24, targetSdk 35 |
| Contents | 14 dex, 55,458 classes, 12,642 resources, arm64-v8a only |
| Analyzer | REA 6.5.0 with JADX headless 0.7.1 |

Resources are renamed to `r/`; a large part of the code is R8-obfuscated, but package
names, interfaces and reflection-reachable classes remain readable.

## Control chain

1. **Cards.** `assets/cardControl/configDes.json` maps device models to card layouts.
   Device panels themselves arrive as RN bundles (`assets/plugin_rn_sdk/sdk.bundle`,
   Metro output, not Hermes).
2. **Plugin API.** The JS SDK exposes `Device.callMethod(method, args)`, which invokes the
   native module `MIOTDevice.callMethod(did, method, args, extra, callback)`.
   `callMethodFromLocal` and `callMethodFromCloud` map to `callMethodForceWay` with
   `1` and `2`; the plain `callMethod` lets the host choose.
3. **Host.** `com.xiaomi.smarthome.framework.plugin.rn.nativemodule.MIOTDeviceModuleCore.callMethod`
   forwards to the host API implementation. The routing itself lives in obfuscated code.
4. **Spec bridge.** `MIOTSpecModule` exposes `getProperty(did, siid, piid, cb)` and
   `setProperty(did, siid, piid, value, ...)` to the same JS layer.

## Local network (miIO over UDP)

`liblocalcontrol.so` contains the payload format it emits:

```json
{"from":"4","id":<n>,"method":"set_properties","params":[{"did":"<did>","siid":2,"piid":1,"value":true}]}
```

It also emits `miIO.get_subdev_list` for gateway children and uses plain UDP sockets plus
AES. `libmiio.so` is the JNI crypto shim; exported symbols:

```
Java_com_xiaomi_miio_JNIBridge_encrypt      Java_com_xiaomi_miio_JNIBridge_decrypt
Java_com_xiaomi_miio_JNIBridge_hencrypt     Java_com_xiaomi_miio_JNIBridge_hdecrypt
Java_com_xiaomi_miio_JNIBridge_bledecrypt   Java_com_xiaomi_miio_JNIBridge_smencrypt
Java_com_xiaomi_miio_JNIBridge_smencryptpk
```

`LocalDeviceApi.rpcAsync(did, payload, callback)` resolves the device IP, logs `localApi`
and sends asynchronously. `LocalServiceManager` wraps this as `controlDevice`,
`getDeviceProperty`, `excuteLocalAutomAction` and `isCentralConnected`.

The UDP framing (magic `0x2131`, 32-byte header, MD5 checksum, AES-128-CBC with
`key = MD5(token)`, `iv = MD5(key + token)`) comes from the public miIO specification and
is **not** re-verified byte-by-byte here.

Discovery uses mDNS type `_miio._udp`.

## Cloud

Two generations of endpoints are present:

| Purpose | Endpoint |
| --- | --- |
| Legacy RPC | `POST /home/rpc/{did}` (confirmed: `DeviceApi.rpcAsyncRemote`, `Crypto.RC4`) |
| Device inventory | `/home/device_list`, `/v2/home/home_device_list`, `/v2/home/local_device_list` |
| Properties | `/app/v2/miotspec/prop/get`, `/prop/set`, `/action`, `/event/get` |
| Push topic | `/v2/miotspec/prop_changed` |
| Plugin metadata | `/app/v2/plugin/get_protocol` |
| BLE bindkey | `/user/get_bindkey`, `/v2/user/get_bindkey`, `/v2/device/blt_get_beaconkey` |

Signing follows the well-known `ssecurity` scheme: `signedNonce` from nonce + ssecurity,
RC4 over parameters, SHA-1 signature, cookies `serviceToken` / `yetAnotherServiceToken`.
Headers include `ENCRYPT-RC4` and `X-XIAOMI-PROTOCAL-FLAG-CLI` (typo is in the app).

Login: `account.xiaomi.com/pass/serviceLoginAuth2`, or QR long-polling.

## Push and events

- SSE: `text/event-stream` handled by `SmartHomeRc4ChunkApi.createSSEFlow` / `SSEStreamApi`.
- MQTT: Eclipse Paho client against `mqtt.io.mi.com`, topics such as `down.mqtt/...`.
- Vendor push channels (Mi Push, Huawei, OPPO, vivo, JPush) exist for notification delivery
  only and are irrelevant to a desktop client.

## Central hub

`com.xiaomi.miot.centralclientsdk.CentralClient` performs mDNS discovery, registers APIs and
broadcasts, and exchanges messages over an "OT" uplink/downlink. `libcentralclient.so`
contains `central_client_mdns_on_discovered`, `central_client_ot_down_link` and a TLS stack.
Pairing uses `miIO.get_central_link_passcode`.

## BLE

`libmiio.so` decrypts BLE frames with a per-device bindkey fetched from the cloud.
`sdk.bundle` defines the generic BLE operation codes:

| Opcode | Operation |
| --- | --- |
| 0 / 1 | `set_properties` / response |
| 2 / 3 | `get_properties` / response |
| 4 | `properties_changed` |
| 5 / 6 | `action` / response |
| 7 | `event_occured` |

Read-only parsing of MiBeacon advertisements is well understood publicly; active control is
device-dependent and out of scope for the first release.

## Open questions

- Actual UDP frame layout and handshake behaviour on current firmware.
- Whether `callMethod` prefers LAN or cloud per device class, and how failures fall back.
- Token rotation rules after re-pairing.
