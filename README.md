# Pole Tool / 装柱図ツール (AutoCAD 2027, .NET 10)

Builds Japanese high-voltage service poles (構内第一柱・引込柱) with equipment, cables, labels,
dimensions and a BOM (物品表) – from a detailed drawing down to a simple sketch – plus a 3D model.

## Install
1. Close AutoCAD. Run `install.bat` (builds and installs an autoload bundle), or `build.bat` + `NETLOAD bin\Release\net10.0-windows\PoleTool.dll`.
2. In AutoCAD: `POLETOOL` (window) · `POLEEDIT` (reload a placed pole into the window). Ribbon tab "Pole Tool".

## Window
- **Configuration**: PAS+VCT first pole (detailed), PAS only, simple layout, pole only. **Detail**: 簡易 / 標準 / 詳細.
- **Pole / 柱**: JIS-style presets (12M-19CM-5.0kN …, steel pipe poles) or custom length, top Ø, taper 1/75, load.
  Embedment automatic (L/6 up to 15 m, 2.5 m to 16 m, +0.3 m for heavy load) or manual.
- **Equipment / 機器**: tick items on/off, add from the library, duplicate, remove. Each item: name JP/EN,
  spec, remarks, height (from GL or down from the pole top), direction around the pole
  (0 front, 90 line side, 180 back, 270 left), quantity, host arm/box, template parameters.
  Cables (jumpers, HV cable, control/meter cables, earth wires, guy wire) are routed automatically
  between the chosen equipment.
- **Drawing / 図面**: front, side, isometric views; labels as names or ①② balloons in JP or EN;
  dimension chain + GL; BOM table; wipeouts for hidden lines; scale, text height, layer prefix; one block or loose entities.
- **Library / ライブラリ**: register your own equipment (shape template + sizes) and optionally your own blocks
  for the front/side/3D views (base point = centre of the equipment). Saved to `%APPDATA%\PoleTool\library.json`.
- Preview: Front / Side / 3D (drag to orbit) / Sheet. Wheel zoom, drag pan, double-click fit.
- **Insert 2D drawing** asks for the point of the pole axis at GL. **Insert 3D model** builds solids.

Layers: `PT_POLE, PT_EQUIP, PT_CABLE, PT_EARTH, PT_GUY, PT_GROUND, PT_TEXT, PT_DIM, PT_BOM, PT_CENTER`, wipeouts on `01_WIPEOUT`.

## Notes
Equipment sizes are typical values for drawings, not maker data. Check embedment and pole strength by calculation.

## Developer
`Core/` has no AutoCAD/WPF dependency: `dotnet run --project tests/CoreTests.csproj -- out` runs the checks and writes
sheet JSON; `python tests/render.py out/pas_vct_jp.json sheet.png` renders it.
