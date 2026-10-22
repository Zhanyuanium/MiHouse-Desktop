# Third-Party Notices

MiHouse includes the following third-party material. Each entry records the
material, its license, and any changes MiHouse made to it.

---

## Material Design Icons (MDI)

- **Project:** Material Design Icons
- **Source:** https://github.com/Templarian/MaterialDesign (distributed as `@mdi/svg`)
- **Copyright:** Austin Andrews (Templarian) and the Pictogrammers contributors
- **License:** Apache License 2.0
- **License text:** [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt)

### Usage in MiHouse

39 category icons are used (37 distinct MDI glyphs; motion/occupancy and vacuum/robot-vacuum share one each) as device-category icons on the home page, device tiles,
and the device detail header. Each glyph is a 24×24 filled single-path SVG.

**Changes made:**

- The source `fill="currentColor"` is dropped and the glyph is tinted at runtime
  with the Windows 11 theme foreground/accent brush, so a single asset serves
  both light and dark themes.
- The 24×24 source geometry is scaled at render time by `PathIcon` to the
  20 px / 24 px / 40 px / 48 px display sizes used across the UI.

**Where the material lives:**

- Original SVGs, unmodified: [`assets/icons/mdi/`](assets/icons/mdi/)
- Path data embedded for rendering:
  [`src/MiHouse.App/Styles/DeviceIcons.xaml`](src/MiHouse.App/Styles/DeviceIcons.xaml)

### Glyphs used

| MDI glyph | Device category |
| --- | --- |
| `ceiling-light` | light |
| `air-conditioner` | air-conditioner |
| `light-switch` | switch |
| `radar` | sensor |
| `curtains` | curtain |
| `air-purifier` | air-purifier |
| `air-humidifier` | humidifier |
| `cooker` | cooker |
| `home-automation` | gateway |
| `cctv` | camera |
| `water-filter` | water-purifier |
| `radiator` | heater |
| `fan` | fan |
| `robot-vacuum` | vacuum, robot-vacuum |
| `kettle-steam` | health-pot |
| `ventilation` | hood |
| `water-heater` | water-heater |
| `induction` | induction-cooker |
| `motion-sensor` | motion-sensor, occupancy-sensor |
| `speaker` | speaker |
| `toaster-oven` | air-fryer |
| `thermometer` | temperature-humidity-sensor |
| `toothbrush-electric` | toothbrush |
| `rice` | rice-cooker |
| `microwave` | microwave |
| `kettle` | kettle |
| `air-dehumidifier` | dehumidifier |
| `power-plug` | plug |
| `power-socket` | power-strip |
| `lock` | door-lock |
| `magnet` | door-sensor |
| `gesture-tap-button` | button |
| `washing-machine` | washer |
| `fridge` | refrigerator |
| `router-wireless` | router |
| `television` | tv |
| `devices` | other (fallback) |
