# Device icons and switch-channel classification

Research note, 2026-10-22. No production code was changed.

Question: (1) which icon source can MiHouse ship for device cards; (2) how does the
official Mi Home app let a wall-switch channel act as a light, and can a third-party
client reproduce it.

Evidence base:

- Mi Home Android 11.7.709 (`%TEMP%\opencode\mihome-re\mihome-11.7.709.apk`), JADX
  class/method reads plus bytecode scans of the plugin React Native SDK.
- Live cloud calls with the signed session already stored by MiHouse, against the
  **东里** home (`home_id=5001832188`). Read-only endpoints only.
- Local MiHouse snapshots and MIoT spec cache for the same home.

## 1. How Mi Home classifies a switch channel

Mi Home does **not** derive a light from the MIoT spec. Every `xiaomi.switch.w1/w2/w3`
channel exposes exactly one service, `urn:miot-spec-v2:service:switch`, with
`property:on`; there is no brightness, colour temperature, or `device:light` anywhere
in the channel spec.

Instead, each switch channel carries a **`subclass_id`**: an id into a server-side
"switch subclass" icon catalogue. The catalogue entry carries a `category_id`, and
`category_id == 12` means lighting.

This is a **naming/icon feature, not a control-semantics feature**. Mi Home renders a
lamp glyph for that channel and shows the user's own channel name; switching it still
writes `switch:on` on the parent device. No code in the APK aggregates `subclass_id`
into lighting scenes, brightness, or a light device.

### 1.1 Where the per-channel value lives

| Case | Where `subclass_id` is read | Write endpoint |
| --- | --- | --- |
| Single-gang switch, or a split `.sN` sub-device | the device object's own `subclass_id` (`/v2/home/device_list`, `/v2/home/device_list_page`) | `/v2/device/set_sub_class` |
| Unsplit multi-gang switch | parent device `member_ship["1".."N"].subclass_id` from `/v2/device/deviceinfo` with `get_sub_relation: true` | `/v2/device/update_membership` |
| Split channel as returned by `/home/sub_device_list` | **absent** — the endpoint returns no `subclass_id` and no `room_id` | (use the parent device) |

`member_ship` key `N` corresponds to channel `siid = N + 1`, i.e. split did `.s(N+1)`.
In 东里 the channel names match the member names one by one, so the mapping is exact.

### 1.2 The catalogue

`POST /v2/device/switch_subclass` with `data={}` returns 54 entries:

```
{ id, rank, chinese_name, english_name, plugin_type, state,
  category, category_id,
  light_icon, light_icon_off, dark_icon, dark_icon_off, proxy_category_icon }
```

`plugin_type` is one of `switch` / `socket` / `lightgroup`. Entries with
`plugin_type=switch` are the ones a wall switch can be assigned. Mi Home caches the
list in `<files>/<env>/DeviceCustomIcon.json`.

`category_id` is the only semantic field. The APK never enumerates the other values;
the plugin SDK only compares against `12`:

```js
var lightItems = subClassList.filter(s => s.plugin_type === 'switch' && s.category_id === 12);
```

Observed ids with `category_id = 12` (照明): 1 筒灯, 2 射灯, 3 灯带, 4 吊灯, 5 吸顶灯,
6 壁灯, 7 床头灯, 8 氛围灯, 9 灯. `category_id` values seen for non-lighting entries:
2 冰箱, 3 厨房电器, 4 影音/电脑, 5 摄像头, 7 环境电器, 9 其他, 11 洗衣机, 16 路由器,
20 晾衣/清洁, 21 厨卫, 23 窗帘/其他. These mappings are inferred from entry names, not
from an enum in the APK.

### 1.3 东里 as measured

`/v2/device/deviceinfo` (batch, `get_sub_relation:true`):

| Parent | Channel | Name | `subclass_id` | Catalogue meaning |
| --- | --- | --- | --- | --- |
| 2002071959 `xiaomi.switch.w2` | 1 (`siid 2`, `.s2`) | 客厅线条灯 | 2 | 射灯 (light) |
| | 2 (`siid 3`, `.s3`) | 客厅吸顶灯 | 5 | 吸顶灯 (light) |
| 2000420370 `xiaomi.switch.w3` | 1 (`.s2`) | 门厅吸顶灯 | 5 | 吸顶灯 (light) |
| | 2 (`.s3`) | 射灯 | 2 | 射灯 (light) |
| | 3 (`.s4`) | 射灯 | 2 | 射灯 (light) |
| 2000675338 `xiaomi.switch.w1` | 1 (self) | 书房灯 | 3 | 灯带 (light) |

So the user's expectation is already recorded in Xiaomi's own data: all six switch
channels in 东里 are marked as lighting. MiHouse ignores it because it classifies by
`spec_type` only.

Two devices in the same home have **no** `subclass_id` at all (`2023237085`
床头开关, and the w2/w3 parents themselves). Absence must fall back to `switch`.

### 1.4 What this means for MiHouse

Reproducing the official behaviour needs two read-only calls and no user input:

1. `POST /v2/device/switch_subclass {}` — fetch the catalogue once, cache it
   (`category_id`, `chinese_name`; icon URLs are not needed).
2. `POST /v2/device/deviceinfo` with
   `{"get_sub_relation": true, "dids": [ ...all switch parents... ]}` — one batched
   call per refresh, gives every channel's `subclass_id`.

Then classify `switch channel → catalogue[subclass_id].category_id == 12 → light`.
Ordered resolution: explicit local override > switch subclass > `spec_type` >
unknown. Degrade silently to `switch` when the catalogue or `member_ship` is missing,
offline, or older than the cache window.

Write endpoints (`set_sub_class`, `update_membership`) exist but should not be used
by a third-party client; classification can be read-only.

Rejected alternative: inferring from the device name (灯/台灯/light). The same home
contains 床头开关 (a switch) and 书房灯 (a switch), and names are user text — it would
misfire in both directions.

## 2. Where Mi Home gets its icons

- No icon field in `device_list`; the top-level keys contain `spec_type` and (sparse)
  `subclass_id` only.
- Default device icon = per-model URL from the packaged plugin config
  (`assets/device_config/plugin_config_<region>`): fields `icon_real`,
  `icon_bluetooth_pairing`. For example `xiaomi.switch.w1` →
  `https://cnbj1.fds.api.xiaomi.com/iotweb-product-center/21606.png`.
- Custom icon = the chosen catalogue entry's `proxy_category_icon` (plus
  `light_icon`/`dark_icon` variants), a CDN PNG.
- There is no local model→drawable or category→drawable table in the APK.

Consequence: the official icons are CDN-hosted bitmaps owned by Xiaomi, with no
redistribution grant. MiHouse must ship its own set; the catalogue is useful as a
*classification* signal, not as an asset source.

## 3. Icon source for MiHouse (GPL-3.0-or-later)

Material Design Icons (`@mdi/svg`, Apache-2.0) is the recommended base: 24×24 filled
single-path SVGs, one `PathIcon` per class, tints with `Foreground` so light/dark
themes need one asset. Coverage of the ~25 device classes is 24/25; only
**water purifier** has no dedicated glyph and must be drawn (24×24, same visual
weight). Optional extra: a multi-gang switch glyph and a gateway glyph.

Alternatives considered and rejected for coverage, not licensing:

- Papirus (GPL-3.0) — license is ideal for this project, but it is a Linux
  application-icon theme: of 163 icons under `48x48/devices`, none cover ceiling
  light, purifier, robot vacuum, water purifier or gateway, and its fixed palette
  cannot follow light/dark themes.
- Tabler (MIT) — 14/25; no ceiling light, purifier, humidifier, water heater, gateway.
- Material Symbols (Apache-2.0) — 19/25; needs four drawings and a variable font.

License obligations: ship `THIRD-PARTY-NOTICES.md`, the Apache-2.0 text under
`licenses/`, keep the original SVGs (also the GPL "corresponding source"), and mark
the icons as recoloured/resized. CC-BY-NC material is unusable here: it would add a
restriction that GPLv3 forbids downstream. CC-BY-4.0 needs attribution only.

## 4. Suggested shape of the implementation (not started)

- `SwitchSubclassCatalog`: fetch + cache `/v2/device/switch_subclass`; expose
  `id → (categoryId, name)`.
- `DeviceMembershipClient`: batched `/v2/device/deviceinfo?get_sub_relation`; expose
  `(parentDid, memberIndex) → subclassId`; refresh with the device list, cache per home.
- `DeviceClass.Resolve(MiDevice device, ...)`: switch channels map `ChannelSiid - 1`
  to a member index; standalone switches use their own `subclass_id`; then
  `category_id` decides `light`.
- Grouping and the group power switch keep working unchanged, because the underlying
  property is still `switch:on` on the parent.
- Keep the planned per-device override on top: local choice wins over the cloud value.

Open questions:

- Catalogue stability by region/account (54 entries for CN); needs a fallback that
  treats unknown ids as `switch`.
- Whether a channel's `subclass_id` can go stale after the user edits it in Mi Home;
  a cache TTL or refresh on device-list change is required.
- Whether to expose `chinese_name` (射灯/灯带) in the UI as the light's subtitle; that
  is the closest thing to a device subtype the cloud provides.
