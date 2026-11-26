using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoleToolApp.Core
{
    public enum PoleKind { Concrete, Steel }
    public enum Detail { Sketch = 0, Standard = 1, Detailed = 2 }
    public enum HRef { GL, Top }
    public enum LabelMode { None, Names, Balloons }

    /// <summary>Pole body. All lengths in mm.</summary>
    public sealed class PoleSpec
    {
        public PoleKind Kind { get; set; } = PoleKind.Concrete;
        public double Length { get; set; } = 12000;
        public double TopDia { get; set; } = 190;
        /// <summary>Diameter increase per length (concrete poles 1/75).</summary>
        public double Taper { get; set; } = 1.0 / 75;
        public double DesignLoadKN { get; set; } = 5.0;
        public bool AutoEmbed { get; set; } = true;
        public double Embed { get; set; } = 2000;

        [JsonIgnore] public double EmbedUsed => AutoEmbed ? PolePresets.AutoEmbed(Length, DesignLoadKN) : Embed;
        [JsonIgnore] public double Top => Length - EmbedUsed;   // Z of the pole top above GL
        [JsonIgnore] public double Bottom => -EmbedUsed;
        public double DiaAt(double z) => TopDia + Math.Max(0, Top - z) * Taper;
        public double RadiusAt(double z) => DiaAt(z) / 2;
        public string Designation(bool jp)
        {
            if (Kind == PoleKind.Steel) return jp ? $"鋼管柱 {Length / 1000:0.#}m φ{TopDia:0.#}" : $"Steel pole {Length / 1000:0.#} m Ø{TopDia:0.#}";
            return $"{Length / 1000:0.#}M-{TopDia / 10:0.#}CM-{DesignLoadKN:0.0}kN";
        }
    }

    /// <summary>One piece of equipment / cable placed on the pole (instance of a catalogue definition).</summary>
    public sealed class EquipItem
    {
        public string Id { get; set; }
        public string Def { get; set; }
        /// <summary>Id of the item whose switching-off also switched this one off (switched back on together).</summary>
        public string OffBy { get; set; }
        public bool On { get; set; } = true;
        /// <summary>Height of the item's reference point, measured from GL or downward from the pole top.</summary>
        public double H { get; set; }
        public HRef Ref { get; set; } = HRef.GL;
        /// <summary>Direction around the pole in degrees: 0 = front (toward the viewer), 90 = right (+X, line side), 180 = back, 270 = left.</summary>
        public double Side { get; set; }
        public double Qty { get; set; } = 1;
        public string Spec { get; set; }
        public string Remark { get; set; }
        public string LabelJp { get; set; }
        public string LabelEn { get; set; }
        /// <summary>AutoCAD colour index for all lines of this item (0 = layer colour).</summary>
        public int Color { get; set; }
        /// <summary>Id of the item this one is mounted on (e.g. insulators on an arm). Empty = automatic.</summary>
        public string At { get; set; }
        /// <summary>Template parameters (sizes, lengths, counts ...).</summary>
        public Dictionary<string, double> P { get; set; } = new Dictionary<string, double>();
        public double Get(string k, double def) => P != null && P.TryGetValue(k, out var v) && !double.IsNaN(v) ? v : def;
        public EquipItem Clone() { var c = (EquipItem)MemberwiseClone(); c.P = new Dictionary<string, double>(P ?? new()); return c; }
    }

    public sealed class PoleJob
    {
        public int Version { get; set; } = 1;
        public string Title { get; set; } = "";
        /// <summary>Name under which this job was saved as a user configuration (empty = none).</summary>
        public string ConfigName { get; set; } = "";
        public PoleSpec Pole { get; set; } = new PoleSpec();
        public List<EquipItem> Items { get; set; } = new List<EquipItem>();
        public Detail Detail { get; set; } = Detail.Standard;
        public bool ViewFront { get; set; } = true;
        public bool ViewSide { get; set; } = true;
        public bool View3D { get; set; }
        public bool LabelsJp { get; set; } = true;
        /// <summary>Labels and BOM show both names ("日本語 / English").</summary>
        public bool LabelsBoth { get; set; }
        public LabelMode Labels { get; set; } = LabelMode.Names;
        public bool Dims { get; set; } = true;
        /// <summary>Name labels are inserted as AutoCAD multileaders (MLEADER); off = lines + text.</summary>
        public bool MLeaders { get; set; } = true;
        public bool Bom { get; set; } = true;
        public bool GroundLine { get; set; } = true;
        public bool Masks { get; set; } = true;
        /// <summary>2D output: cut hidden line parts away and merge duplicates (overkill) instead of using wipeouts.</summary>
        public bool CleanLines { get; set; }
        /// <summary>Direction of the "current 3D view" output (azimuth / elevation in degrees).</summary>
        public double ViewAz { get; set; } = -35;
        /// <summary>Turn of the front / side views around the pole axis (degrees, Shift+drag in the 2D previews).</summary>
        public double ElevRot { get; set; }
        public double ViewEl { get; set; } = 25;
        /// <summary>Drawing scale denominator (1:50 -> 50): text and arrow sizes are paper mm × Scale.</summary>
        public double Scale { get; set; } = 50;
        public double TextPaper { get; set; } = 2.5;
        /// <summary>Text height of the name labels / balloons on paper (mm); 0 = same as TextPaper.</summary>
        public double LabelPaper { get; set; } = 3.5;
        public double LabelH => (LabelPaper > 0 ? LabelPaper : TextPaper) * Scale;
        public string LayerPrefix { get; set; } = "PT_";
        public string WipeoutLayer { get; set; } = "01_WIPEOUT";
        public bool AsBlock { get; set; } = true;

        public EquipItem Item(string id) => Items.FirstOrDefault(i => i.Id == id);
        public bool Has(string def) => Items.Any(i => i.On && i.Def == def);
        public double Z(EquipItem it) => it.Ref == HRef.Top ? Pole.Top - it.H : it.H;

        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
        { WriteIndented = false, Converters = { new JsonStringEnumConverter() }, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        static readonly JsonSerializerOptions OptPretty = new JsonSerializerOptions
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
          Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        public string ToJson(bool pretty = false) => JsonSerializer.Serialize(this, pretty ? OptPretty : Opt);
        public static PoleJob FromJson(string s)
        {
            try { var j = JsonSerializer.Deserialize<PoleJob>(s, Opt); j?.Normalize(); return j; } catch { return null; }
        }
        public PoleJob Clone() => FromJson(ToJson());

        public void Normalize()
        {
            Pole ??= new PoleSpec(); Items ??= new List<EquipItem>();
            Pole.Length = Clamp(Pole.Length, 3000, 30000, 12000);
            Pole.TopDia = Clamp(Pole.TopDia, 50, 800, 190);
            Pole.Taper = Clamp(Pole.Taper, 0, 0.05, 1.0 / 75);
            Pole.Embed = Clamp(Pole.Embed, 0, Pole.Length - 1000, 2000);
            Scale = Clamp(Scale, 1, 1000, 50); TextPaper = Clamp(TextPaper, 0.5, 20, 2.5); LabelPaper = Clamp(LabelPaper, 0, 20, 3.5);
            int n = 1; var ids = new HashSet<string>();
            foreach (var it in Items)
            {
                it.P ??= new Dictionary<string, double>();
                if (string.IsNullOrEmpty(it.Id) || !ids.Add(it.Id)) { while (!ids.Add("i" + n)) n++; it.Id = "i" + n; }
                if (double.IsNaN(it.H) || double.IsInfinity(it.H)) it.H = 0;
                if (double.IsNaN(it.Qty) || it.Qty < 0) it.Qty = 1;
            }
        }
        static double Clamp(double v, double lo, double hi, double def) => double.IsNaN(v) || double.IsInfinity(v) ? def : Math.Max(lo, Math.Min(hi, v));

        /// <summary>Name of a scene item in the drawing language(s).</summary>
        public string NameOf(SceneItem it)
        {
            if (!LabelsBoth) return it.Name(LabelsJp);
            string a = it.Name(true), b = it.Name(false);
            return a == b ? a : LabelsJp ? a + " / " + b : b + " / " + a;
        }

        public EquipItem AddFromCatalog(string defId)
        {
            var d = Catalog.Find(defId); if (d == null) return null;
            var it = d.NewItem();
            int n = Items.Count + 1; while (Items.Any(i => i.Id == "i" + n)) n++;
            it.Id = "i" + n; Items.Add(it); return it;
        }
    }

    /// <summary>Japanese pole size presets. Values follow common JIS A 5373 power-distribution poles;
    /// check against the maker's catalogue before ordering.</summary>
    public static class PolePresets
    {
        public sealed class Preset
        {
            public string Name; public PoleKind Kind; public double L, Top, Load, Taper = 1.0 / 75;
            public override string ToString() => Name;
        }

        public static readonly double[] ConcreteLengths = { 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
        public static readonly double[] ConcreteTops = { 140, 190, 220, 250 };
        public static readonly double[] Loads = { 2.5, 3.5, 5.0, 7.0, 10.0 };
        public static readonly double[] SteelDia = { 139.8, 165.2, 190.7, 216.3, 267.4, 318.5 };

        public static List<Preset> All()
        {
            var l = new List<Preset>();
            void C(double m, double top, double kn) => l.Add(new Preset { Name = $"{m}M-{top / 10:0}CM-{kn:0.0}kN", Kind = PoleKind.Concrete, L = m * 1000, Top = top, Load = kn });
            C(8, 190, 3.5); C(9, 190, 3.5); C(10, 190, 3.5); C(10, 190, 5.0); C(11, 190, 5.0);
            C(12, 190, 3.5); C(12, 190, 5.0); C(12, 190, 7.0); C(13, 190, 5.0); C(13, 190, 7.0);
            C(14, 190, 5.0); C(14, 190, 7.0); C(15, 190, 7.0); C(15, 190, 10.0); C(16, 190, 7.0); C(16, 190, 10.0);
            C(7, 140, 2.5); C(8, 140, 2.5);
            foreach (var (m, d) in new[] { (8.0, 165.2), (10.0, 190.7), (12.0, 216.3), (14.0, 267.4) })
                l.Add(new Preset { Name = $"鋼管柱 {m}m φ{d}", Kind = PoleKind.Steel, L = m * 1000, Top = d, Load = 0, Taper = 0 });
            return l;
        }

        /// <summary>Embedment per the basic values of the technical-standard interpretation (art. 59):
        /// L ≤ 15 m: L/6; 15 &lt; L ≤ 16 m: 2.5 m; 16 &lt; L ≤ 20 m: 2.8 m; +0.3 m for design load &gt; 6.87 kN with L ≥ 14 m.
        /// Rounded up to 50 mm.</summary>
        public static double AutoEmbed(double L, double loadKN)
        {
            double e = L <= 15000 ? L / 6 : L <= 16000 ? 2500 : 2800;
            if (loadKN > 6.87 && L >= 14000) e += 300;
            return Math.Ceiling(e / 50) * 50;
        }
    }

    // ------------------------------------------------------------------ catalogue

    public sealed class EquipDef
    {
        public string Id { get; set; }
        public string Template { get; set; }
        public string Cat { get; set; }
        public string Jp { get; set; }
        public string En { get; set; }
        public string Spec { get; set; } = "";
        public string Remark { get; set; } = "";
        public double Qty { get; set; } = 1;
        public HRef Ref { get; set; } = HRef.GL;
        public double H { get; set; }
        public double Side { get; set; }
        public Dictionary<string, double> P { get; set; } = new Dictionary<string, double>();
        /// <summary>Optional user block names used instead of the generated geometry (front / side / 3D).</summary>
        public string BlockFront { get; set; }
        public string BlockSide { get; set; }
        public string Block3D { get; set; }
        public bool User { get; set; }
        /// <summary>Definition id of the item this one is normally mounted on (first enabled one is used).</summary>
        public string AtDef { get; set; }
        /// <summary>Japanese counting unit for the BOM (本, 個, 台, 式 ...).</summary>
        public string Unit { get; set; } = "個";
        public string Name(bool jp) => jp ? Jp : En;
        public static string UnitEn(string jp) => jp switch
        { "本" => "pcs", "個" => "pcs", "台" => "units", "式" => "set", "組" => "sets", "箇所" => "places", "枚" => "pcs", "面" => "pcs", "m" => "m", _ => jp };
        public EquipItem NewItem() => new EquipItem
        { Def = Id, H = H, Ref = Ref, Side = Side, Qty = Qty, Spec = Spec, Remark = Remark, P = new Dictionary<string, double>(P ?? new()) };
    }

    public static class Catalog
    {
        public static readonly (string id, string jp, string en)[] Categories =
        {
            ("top", "引込・腕金", "Service & arms"),
            ("sw", "開閉器・変成器", "Switchgear & VCT"),
            ("ins", "碍子・端末", "Insulators & terminations"),
            ("cab", "ケーブル・電線", "Cables & wires"),
            ("pipe", "電線管", "Conduits & pipes"),
            ("box", "箱類", "Boxes"),
            ("guy", "支線", "Guy wire"),
            ("earth", "接地", "Earthing"),
            ("base", "基礎・地中", "Foundation & underground"),
            ("lp", "雷保護", "Lightning protection"),
            ("misc", "その他", "Other"),
            ("user", "ユーザー登録", "User library"),
        };

        static List<EquipDef> _all;
        public static List<EquipDef> All { get { if (_all == null) Load(); return _all; } }
        public static EquipDef Find(string id) => All.FirstOrDefault(d => d.Id == id);
        public static string UserFile;   // set by the host (e.g. %APPDATA%\PoleTool\library.json)

        static EquipDef D(string id, string tpl, string cat, string jp, string en, string spec, double h, HRef r = HRef.GL,
                          double side = 0, double qty = 1, string remark = "", params (string, double)[] p)
        {
            var d = new EquipDef { Id = id, Template = tpl, Cat = cat, Jp = jp, En = en, Spec = spec, H = h, Ref = r, Side = side, Qty = qty, Remark = remark };
            foreach (var (k, v) in p) d.P[k] = v;
            return d;
        }

        /// <summary>Built-in definitions. Heights are defaults for a 12 m pole (top 10 m above GL), as in a typical
        /// 構内第一柱 drawing; Top-referenced items follow the pole top when the length changes.</summary>
        public static List<EquipDef> BuiltIn() => new List<EquipDef>
        {
            // --- service & arms
            D("svc_line", "service_line", "top", "架空引込線（電力会社）", "Overhead service line (utility)", "3φ3W 6.6kV", 300, HRef.Top, 90, 1, "電力会社施工", ("len", 2500), ("sag", 150)),
            D("deadend_arm", "arm", "top", "引留腕金", "Dead-end crossarm", "75×75×1500", 300, HRef.Top, 90, 1, "", ("len", 1500), ("a", 75), ("t", 6)),
            D("deadend_fit", "deadend_fit", "top", "中線引留金物", "Middle-phase dead-end fitting", "CP用 (MWH-U)", 300, HRef.Top, 90, 1, ""),
            D("deadend_ins", "deadend_ins", "ins", "高圧耐張碍子・耐張ストラップ", "HV strain insulators & straps", "普通用", 300, HRef.Top, 90, 6, "電力会社施工", ("perPhase", 2)),
            D("arm_vct", "arm", "top", "腕金（VCT用）", "Crossarm (for VCT)", "75×75×1500", 1800, HRef.Top, 0, 1, "", ("len", 1500), ("a", 75), ("t", 6)),
            D("arm_head", "arm", "top", "腕金（ケーブルヘッド用）", "Crossarm (cable heads)", "75×75×900", 3400, HRef.Top, 90, 1, "", ("len", 900), ("a", 75), ("t", 6), ("oneSided", 1)),
            D("arm_tie", "arm_tie", "top", "アームタイ", "Arm tie", "", 1800, HRef.Top, 0, 2, "", ("drop", 450)),
            D("arm_band", "band", "top", "アームバンド", "Arm band", "", 1800, HRef.Top, 0, 1, ""),
            D("strong_band", "band", "top", "強力バンド", "Heavy-duty band", "19cm", 300, HRef.Top, 0, 1, "", ("w", 60)),
            // --- switchgear
            D("pas", "pas", "sw", "区分開閉器（PAS）", "Sectionalizing switch (PAS)", "地絡保護装置付高圧交流負荷開閉器 7.2kV 300A LA内蔵", 750, HRef.Top, 0, 1, "", ("w", 560), ("d", 330), ("hgt", 420)),
            D("ugs", "pas", "sw", "区分開閉器（UGS）", "Sectionalizing switch (UGS)", "地絡保護装置付地中線用負荷開閉器 7.2kV 300A", 750, HRef.Top, 0, 1, "", ("w", 520), ("d", 300), ("hgt", 400)),
            D("vct", "vct", "sw", "VCT（電力需給用計器用変成器）", "Metering VCT", "6.6kV 3φ3W", 1600, HRef.Top, 0, 1, "電力会社支給", ("w", 520), ("d", 320), ("hgt", 420)),
            D("bolt_conn", "bolt_conn", "sw", "ボルトコネクター＋高圧引下線用コネクタカバー", "Bolt connectors + covers", "5号", 1100, HRef.Top, 0, 6, "電力会社施工"),
            // --- insulators & terminations
            D("pin_ins", "pin_ins", "ins", "高圧ピン碍子", "HV pin insulators", "普通用", 1800, HRef.Top, 0, 3, "", ("onArm", 1)),
            D("cable_head", "cable_head", "ins", "6.6kV屋外用端末処理", "6.6 kV outdoor terminations", "屋外・耐塩 CVT100sq用", 3400, HRef.Top, 90, 3, ""),
            D("la", "la", "lp", "避雷器（LA）", "Lightning arresters (LA)", "8.4kV 2.5kA", 3400, HRef.Top, 270, 3, "", ("onArm", 1)),
            // --- cables
            D("jumper", "jumper", "cab", "高圧引下線", "HV jumpers", "PDC 22sq", 0, HRef.GL, 0, 3, ""),
            D("hv_cable", "hv_cable", "cab", "高圧ケーブル", "HV cable", "6.6kV CVT 100sq", 0, HRef.GL, 90, 1, "", ("toX", 6000), ("depth", 900)),
            D("ctrl_cable", "ctrl_cable", "cab", "PAS制御線", "PAS control cable", "CVV-S 1.25sq-6C", 0, HRef.GL, 0, 1, "", ("pass", 0)),
            D("pulse_cable", "link_cable", "cab", "パルス信号線", "Pulse signal cable", "CVV-S 1.25sq-2C", 0, HRef.GL, 0, 1, "計器箱→パルス変換器→構内"),
            D("meter_cable", "meter_cable", "cab", "計器用ケーブル（VCT二次）", "Meter cable (VCT secondary)", "CVV 3.5sq-6C", 0, HRef.GL, 0, 1, "電力会社施工", ("pass", 0)),
            // --- conduits
            D("riser_pipe", "riser_pipe", "pipe", "ケーブル保護管（エントランスキャップ付）", "Cable riser pipe (with entrance cap)", "G104", 4600, HRef.Top, 90, 1, "厚鋼電線管", ("dia", 113.4), ("bottom", 0)),
            D("conduit_ctrl", "conduit", "pipe", "電線管（制御線）", "Conduit (control)", "G28", 2000, HRef.GL, 345, 1, "", ("dia", 34), ("top", 2400), ("bottom", 0)),
            D("conduit_ntt", "conduit", "pipe", "電線管（通信線）", "Conduit (telecom)", "VE28", 2000, HRef.GL, 180, 1, "", ("dia", 34), ("top", 3000), ("bottom", 0)),
            D("conduit_pulse", "conduit", "pipe", "電線管（パルス信号線）", "Conduit (pulse signal)", "G22", 2000, HRef.GL, 215, 1, "", ("dia", 26.5), ("top", 1800), ("bottom", 0)),
            D("conduit_meter", "conduit", "pipe", "電線管（計器用）", "Conduit (meter)", "G54", 2000, HRef.GL, 255, 1, "", ("dia", 60.5), ("top", 1700), ("bottom", 0)),
            D("pipe_band", "pipe_band", "pipe", "自在バンド（管固定用）", "Pipe bands", "", 0, HRef.GL, 0, 1, "", ("pitch", 1500)),
            // --- boxes
            D("sog_box", "box", "box", "SOG制御箱", "SOG control box", "", 2000, HRef.GL, 0, 1, "", ("w", 250), ("d", 160), ("hgt", 350)),
            D("pas_rbox", "box", "box", "PAS操作箱（R.BOX）", "PAS operation box", "", 2600, HRef.GL, 0, 1, "", ("w", 160), ("d", 100), ("hgt", 200)),
            D("meter_box", "box", "box", "屋外用計器箱", "Outdoor meter box", "FBA-22M", 2000, HRef.GL, 270, 1, "電力会社支給", ("w", 300), ("d", 200), ("hgt", 450), ("roof", 1)),
            D("pulse_box", "box", "box", "パルス変換器収納箱", "Pulse converter box", "OPK20-45A", 2000, HRef.GL, 225, 1, "", ("w", 200), ("d", 130), ("hgt", 300)),
            D("relay_box", "box", "box", "機器・継電器収納箱", "Relay box", "600×800×200", 1700, HRef.GL, 0, 1, "", ("w", 600), ("d", 200), ("hgt", 800), ("roof", 1)),
            // --- guy
            D("guy", "guy", "guy", "支線", "Guy wire", "2.9φ-7本 亜鉛メッキ", 1700, HRef.Top, 270, 1, "", ("dist", 5000), ("insH", 4800), ("guard", 2000)),
            D("guy_ins", "part_of", "guy", "支線玉碍子", "Guy strain insulator", "", 0, HRef.GL, 0, 1, ""),
            D("guy_guard", "part_of", "guy", "支線ガード", "Guy guard", "硬質ポリエチレン", 0, HRef.GL, 0, 1, ""),
            D("guy_anchor", "part_of", "guy", "支線アンカー", "Guy anchor", "支線ブロック 550×400 ロッド付", 0, HRef.GL, 0, 1, ""),
            D("guy_grip", "part_of", "guy", "巻付グリップ", "Preformed grips", "玉碍子・シンブル用", 0, HRef.GL, 0, 4, ""),
            // --- earthing
            D("earth_a", "earth", "earth", "接地線（A種：PAS・LA）", "Earth wire (class A: PAS, LA)", "IV14sq", 0, HRef.GL, 215, 1, "", ("from", 0), ("pass", 0)),
            D("earth_d", "earth", "earth", "接地線（D種：腕金・金属部）", "Earth wire (class D: metal parts)", "IV5.5sq", 0, HRef.GL, 150, 1, "", ("from", 1), ("pass", 0)),
            D("earth_rod", "earth_rod", "earth", "接地極", "Earth electrode", "銅覆鋼棒 14φ×1500", -750, HRef.GL, 180, 2, ""),
            // --- foundation & underground
            D("neka", "neka", "base", "根かせ", "Pole anchor beams (根かせ)", "1000×170×140 Uボルト付", -500, HRef.GL, 0, 2, ""),
            D("base_plate", "base_plate", "base", "底板", "Base plate", "", 0, HRef.GL, 0, 1, ""),
            D("ug_pipe", "ug_pipe", "base", "地中管路", "Underground ducts", "FEP125, FEP30×3", -900, HRef.GL, 90, 1, "", ("len", 3000), ("n", 4), ("dia", 125)),
            D("handhole", "handhole", "base", "ハンドホール", "Handhole", "600×600×600", 0, HRef.GL, 90, 1, "", ("dist", 3500), ("w", 600), ("hgt", 600)),
            D("lrod", "lrod", "lp", "避雷針（突針）", "Lightning rod (air terminal)", "突針 φ19×1000", 0, HRef.Top, 0, 1, "", ("len", 1000)),
            D("earth_lp", "earth", "lp", "接地線（避雷器・避雷針用）", "Earth wire (arrester / rod)", "IV14sq", 0, HRef.GL, 230, 1, "", ("from", 2), ("pass", 0)),
            // --- other
            D("step_bolts", "step_bolts", "misc", "足場ボルト", "Step bolts", "CP用", 2700, HRef.GL, 30, 14, "", ("pitch", 450), ("topGap", 500)),
            D("custom_box", "box", "user", "機器（任意）", "Custom equipment (box)", "", 2500, HRef.GL, 0, 1, "", ("w", 300), ("d", 200), ("hgt", 300)),
            D("custom_cyl", "cyl", "user", "機器（円筒・任意）", "Custom equipment (cylinder)", "", 2500, HRef.GL, 0, 1, "", ("dia", 200), ("hgt", 400)),
        };

        static readonly Dictionary<string, (string at, string unit)> Extra = new Dictionary<string, (string, string)>
        {
            ["svc_line"] = ("deadend_ins", "式"), ["deadend_arm"] = (null, "本"), ["deadend_fit"] = (null, "個"), ["deadend_ins"] = ("deadend_arm", "個"),
            ["arm_vct"] = (null, "本"), ["arm_head"] = (null, "本"), ["arm_tie"] = ("arm_vct", "本"), ["arm_band"] = ("arm_vct", "個"),
            ["strong_band"] = ("deadend_arm", "個"), ["pas"] = (null, "台"), ["ugs"] = (null, "台"), ["vct"] = ("arm_vct", "台"),
            ["bolt_conn"] = (null, "個"), ["pin_ins"] = ("arm_vct", "個"), ["cable_head"] = ("arm_head", "箇所"), ["la"] = ("arm_head", "個"),
            ["jumper"] = (null, "式"), ["hv_cable"] = (null, "式"), ["ctrl_cable"] = ("sog_box", "式"), ["meter_cable"] = ("meter_box", "式"),
            ["riser_pipe"] = (null, "式"), ["conduit_ctrl"] = ("sog_box", "式"), ["conduit_ntt"] = (null, "式"), ["conduit_meter"] = ("meter_box", "式"),
            ["pipe_band"] = (null, "式"), ["sog_box"] = (null, "面"), ["pas_rbox"] = (null, "面"), ["meter_box"] = (null, "面"), ["relay_box"] = (null, "面"),
            ["pulse_box"] = (null, "面"), ["pulse_cable"] = ("pulse_box", "式"), ["conduit_pulse"] = ("pulse_box", "式"),
            ["guy"] = (null, "式"), ["guy_ins"] = ("guy", "個"), ["guy_guard"] = ("guy", "本"), ["guy_anchor"] = ("guy", "組"), ["guy_grip"] = ("guy", "個"),
            ["earth_a"] = (null, "式"), ["earth_d"] = (null, "式"), ["earth_rod"] = (null, "本"), ["neka"] = (null, "本"), ["base_plate"] = (null, "枚"),
            ["ug_pipe"] = (null, "式"), ["handhole"] = (null, "箇所"), ["step_bolts"] = (null, "本"), ["lrod"] = (null, "本"), ["earth_lp"] = (null, "式"), ["custom_box"] = (null, "台"), ["custom_cyl"] = (null, "台"),
        };

        public static void Load()
        {
            var list = BuiltIn();
            foreach (var d in list) if (Extra.TryGetValue(d.Id, out var x)) { d.AtDef = x.at; d.Unit = x.unit; }
            try
            {
                if (UserFile != null && File.Exists(UserFile))
                {
                    var user = JsonSerializer.Deserialize<List<EquipDef>>(File.ReadAllText(UserFile), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
                    if (user != null)
                        foreach (var u in user)
                        {
                            if (string.IsNullOrWhiteSpace(u?.Id)) continue;
                            u.User = true; u.P ??= new Dictionary<string, double>();
                            if (string.IsNullOrEmpty(u.Template)) u.Template = "box";
                            if (string.IsNullOrEmpty(u.Cat)) u.Cat = "user";
                            list.RemoveAll(d => d.Id == u.Id);   // a user entry may override a built-in one
                            list.Add(u);
                        }
                }
            }
            catch { /* a broken library file must not stop the tool */ }
            _all = list;
        }

        public static void SaveUser()
        {
            if (UserFile == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile));
            var user = All.Where(d => d.User).ToList();
            File.WriteAllText(UserFile, JsonSerializer.Serialize(user, new JsonSerializerOptions
            { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }

        /// <summary>Templates a user may base a library entry on.</summary>
        public static readonly string[] UserTemplates = { "box", "cyl", "conduit", "arm", "pin_ins", "la", "cable_head", "pas", "vct" };
    }
}
