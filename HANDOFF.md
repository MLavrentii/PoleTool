# Pole Tool – handoff (2026-11-19)
Full spec / architecture: Claude project doc `claude/PoleTool_HANDOFF.md` (same content as below, kept in sync).

## State
- Core/ (geometry, data, catalogue ≈45 items JP/EN, scene builder, sheet composer): compiled and tested on Linux,
  71/71 checks pass; rendered samples in docs/.
- UI/ (code-built WPF window, preview with pan/zoom/orbit, JP/EN) and Cad/ (2D entities, wipeouts, dims, Table,
  3D solids, job stored in an extension-dictionary Xrecord) + Commands.cs: written, NOT compiled yet
  (no Windows/AutoCAD SDK in the build sandbox). No syntax errors (Roslyn parse check).

## Next steps
1. Close AutoCAD → build.bat → send the full error list if any → install.bat → POLETOOL.
2. Test: configurations, detail levels, language switch, Insert 2D (block + loose), Insert 3D, POLEEDIT, library entry with a user block.
3. Phase 2 ideas: label collision avoidance, 責任分界点 marker, cubicle/transformer link (様式5の5), more equipment, verify dimensions with maker data.

## Conventions
mm, Z up, GL = 0, pole axis = origin, X = incoming-line direction (right in front view). Item direction 0 = front, 90 = line side, 180 = back, 270 = left.
Heights: H from GL, or from pole top downward (Ref = Top) so items follow the pole length.

## Update 2026-11-19 (2)
- Fixed: Drawing name clash (WPF) in PreviewControl.cs / PoleToolWindow.cs.
- Added: "Equipment names" JP / EN / both (top bar), drawing labels JP / EN / JP+EN, per-item colour (ACI),
  lightning protection category (避雷器 LA, 避雷針, 接地線) – listed but off, drag equipment in Front/Side preview
  (height within limits, side by crossing the pole, guy anchor distance, underground ducts depth/side), Esc / Ctrl+Z.
- Still to do: per-item label/BOM/dimension choices, text level switch, notes, boundary marker.
Full details: Claude project doc claude/PoleTool_HANDOFF.md

## Update 2026-11-19 (3)
- Fixed: in 3D views from above, boxes in front of the pole were hidden. Cause: depth sorting per whole solid (pole centre is high up). Projector now slices long solids/cables that run toward the viewer (Slices(), Mesh.Virtual cut faces) so each slice is sorted on its own. Front/side 2D output unchanged. Test "3D view from above" added.

## Update 2026-11-19 (4) – visibility audit
- New check: `dotnet run --project tests/CoreTests.csproj -- vis [limit]` compares the painter drawing with an exact z-buffer render for 72 view directions x 3 configurations (all items on) and reports items wrongly hidden (solids by pixels, cables by sample points).
- First run found 17 problems: crossarms (L-angle prisms not sliced), base plate / guy anchor (wide boxes), earth wires, control cable and arrester leads (cables sorted by a single depth).
- Fixes: boxes sliced along all three axes (BoxP.Cut bit mask), prisms sliced (PrismP.CapB/CapT), cables cut into short pieces keyed on their nearer end + 20 mm, arrester leads routed around the arm edge. Result: VIS OK even at 15% limit; normal tests pass.
- Known: HV cable inside riser/duct is drawn (red) through the pipe wall – intentional for 2D, slightly odd in 3D.

## Update 2026-11-19 (5)
- install.bat rewritten in the BoltTool style: checks (AutoCAD closed, dotnet, AutoCAD dlls, source files), deletes the old built DLL and does a full --no-incremental rebuild, stages PoleTool.bundle.installing, keeps PoleTool.bundle.previous as backup, verifies the DLL byte for byte, rolls back on failure, warns about other bundle copies. uninstall.bat also removes backup/staging folders.

## Update 2026-11-19 (6)
- 3D preview: Shift+drag turns only around the vertical pole axis (elevation kept); azimuth/elevation shown while orbiting and stored in job.ViewAz/ViewEl (also used for the isometric view of the sheet).
- New button "Insert this 3D view as 2D" (command PT_INSERTVIEW): Sheet.ViewOnly -> Core/HiddenLines.Clean = hidden parts of lines cut against later wipeout polygons (Cyrus-Beck, grid index), masks dropped, collinear duplicates merged (MergeSegments), specks removed. Typical 3D view: ~10000 -> ~5500 lines, 0 wipeouts.
- Drawing tab option "Remove hidden lines + overkill (no wipeouts)" (job.CleanLines) applies the same cleanup to every view of the sheet.
- Not compiled on Windows yet.

## Update 2026-11-19 (7)
- Core/Links.cs: switching an item off also switches off what is mounted on it (At / AtDef host) and its followers (vct -> arm_vct, meter_cable, meter_box, conduit_meter; pas -> ctrl_cable, sog_box, pas_rbox, conduit_ctrl; guy -> guy parts; lrod -> earth_lp). EquipItem.OffBy remembers it; switching the item on again restores them. Window shows "Also switched off/on: ..." in the status line.
- Without VCT the jumpers run PAS -> cable heads directly (verified by test). They cross in front of the pole because the PAS load bushings face -X; the head arm can be dragged higher.

## Update 2026-11-19 (8)
- Moving elements now needs Ctrl+drag (plain drag = pan / orbit) and works in Front, Side, 3D and Sheet views. Drawing.Views (ViewRegion) tells which view the cursor is in. While dragging: Shift = height only, Alt = 1 mm / 1 deg steps; Ctrl+Shift at the start = turn around the pole (15 deg steps). In 3D the height change is divided by view.Up.Z.
- Top bar: "Drawing text" 日本語 / English / 日本語+English (= job.LabelsJp / LabelsBoth, same as Drawing tab) separate from "List names" (window list only) and UI language.
- Named configurations: top bar Save as... / Load / Delete; user configs saved as %APPDATA%\PoleTool\configs\<name>.polejob.json (whole job incl. pole + drawing settings), shown with a star in the configuration list. Core: Configs.UserDir / SaveUser / LoadUser / DeleteUser / UserNames; PoleJob.ConfigName.

## Update 2026-11-19 (9)
- Shift+drag turns the view around the pole axis in ALL previews: 3D (elevation kept) and Front / Side / Sheet (job.ElevRot; front = View3(ElevRot, 0), side = View3(ElevRot-90, 0); snaps to 15 deg multiples within 2 deg; "0°" button resets). The inserted 2D sheet uses the turned views.
- Drag side logic generalized to any horizontal view (screen right/left/facing computed from View3.Right/Toward).

## Update 2026-11-19 (10)
- Bottom bar now has three insert buttons: "Insert sheet" (PT_INSERT2D, Drawing-tab settings), "Insert current view as 2D" (PT_INSERTVIEW with the preview direction: front/side incl. turn, or 3D az/el; on the Sheet tab = insert sheet; clean lines + GL + caption), "Insert current view as 3D model" (PT_INSERT3D, then the AutoCAD view direction is set to the preview direction + ZOOM E).
- Block name / layer prefix / wipeout layer / one-block options moved into a collapsed "Insert options (advanced)" expander on the Drawing tab.

## Update 2026-11-19 (11) – nothing on top of each other around the pole
- Core/Around.cs: footprints (height range + angular sector on the pole surface) per part; kinds Pipe / Box / Wire / Bolt. Conflicts: everything except Box-Wire (cables pass behind boxes on brackets), Wire-Bolt and Bolt-Bolt. Gaps: 30 mm (pipes/boxes), 20 mm wire-other, 15 mm wire-wire. Ignored: arms, bands, insulators, heads, LA, VCT on its arm, pipe bands, connectors, jumpers, HV cable inside riser. Joined pairs (meant to touch) e.g. ctrl cable-PAS/SOG box, conduit-its box.
- Drag: when the side changes (crossing, Ctrl+Shift turn, cable turn) the item goes to the nearest free angle; it stops beside an obstacle and jumps over once pulled past the obstacle middle. Height moves are checked too.
- Control cable, meter cable and earth wires can now be dragged (turn only); they keep their own side after that (P fixedSide).
- Arrange button (Equipment tab) + automatic Arrange when a configuration is created; status line warns "Overlap around the pole: A <-> B".
- Default sides changed: telecom conduit 180, meter box 270, meter conduit 255, earth A 215, earth D 150.

## Update 2026-11-19 (12)
- Name labels are a DLeader entity (anchor, landing, end, text); inserted as AutoCAD MLEADER (dot arrow _DotSmall if present, underline attachment, dogleg toward the text) when job.MLeaders (Drawing tab, default on), fallback = polyline + dot + DBText. Preview draws texts down to 1.2 px (labels were invisible at sheet zoom).
- BOM table columns adapt to the longest text in each column (TextWidth x text height).
- Excel material list (bottom bar button, Core/Materials.cs + Core/Xlsx.cs, no external library): sheets Summary / BOM / Fixing materials (arm bands + M16 bolts per arm, M12 per arm tie, pole bands + bolts per PAS/box, pipe bands per pipe by pitch, normal bends, entrance cap, cable ties every 0.6 m on the pole, earth protection pipe VE16 2.5 m, lugs, guy band + thimbles; with basis text and totals per item+spec) / Cables & pipes (drawn length x1.1 + ends; conduit standard lengths + couplings). Sample: docs/sample_materials.xlsx.
- Cable route at boxes (ctrl / meter cable, earth wires; P "pass"): 0 behind boxes (default), 1 in front of boxes (detour round the front face), 2 beside boxes (treated as obstacle by Around / Arrange).

## Update 2026-11-19 (13)
- New items: pulse_box パルス変換器収納箱 (box template, OPK20-45A, 200x130x300 typical, side 225 -> arranged), pulse_cable パルス信号線 (new template link_cable: meter box -> pulse box -> down its conduit, dashed), conduit_pulse 電線管（パルス信号線） G22 (AtDef pulse_box). In the PAS+VCT configuration; switched off with the VCT / meter box (Links), included in BOM, Excel (fixings, lengths), Around (wire, joined to its boxes/conduits).

## Update 2026-11-19 (14)
- Separate label text height: job.LabelPaper (paper mm, default 3.5; 0 = same as TextPaper), job.LabelH = LabelPaper x Scale. Used for leaders and balloons (preview + AutoCAD MLEADER). Drawing tab field + A-/A+ buttons (0.5 mm steps) in the preview bar.
