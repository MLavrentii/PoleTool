using System.Collections.Generic;
using System.Globalization;

namespace PoleToolApp.UI
{
    /// <summary>English / Japanese UI texts. English is the key; missing entries stay English.</summary>
    public static class Loc
    {
        public static bool Jp;
        /// <summary>Language of equipment names in the window: -1 = same as the UI, 0 = Japanese, 1 = English, 2 = both.</summary>
        public static int Names = -1;
        public static string NameOf(string jp, string en)
        {
            int m = Names < 0 ? (Jp ? 0 : 1) : Names;
            if (string.IsNullOrEmpty(jp)) return en; if (string.IsNullOrEmpty(en)) return jp;
            return m == 0 ? jp : m == 1 ? en : (jp == en ? jp : jp + " / " + en);
        }
        public static string T(string en) => Jp && en != null && J.TryGetValue(en, out var v) ? v : en;
        public static string F(string en, params object[] a) => string.Format(CultureInfo.InvariantCulture, T(en), a);

        static readonly Dictionary<string, string> J = new Dictionary<string, string>
        {
            ["Pole Tool"] = "装柱図ツール",
            ["Configuration"] = "構成",
            ["Load"] = "読込",
            ["Replace the current equipment with this configuration?"] = "現在の機器をこの構成で置き換えますか？",
            ["Detail"] = "詳細度",
            ["Sketch"] = "簡易", ["Standard"] = "標準", ["Detailed"] = "詳細",
            ["UI language"] = "表示言語",
            ["Pole"] = "柱", ["Equipment"] = "機器", ["Drawing"] = "図面", ["Library"] = "ライブラリ",
            ["Preset"] = "規格", ["Apply"] = "適用",
            ["Kind"] = "種類", ["Concrete pole"] = "コンクリート柱", ["Steel pipe pole"] = "鋼管柱",
            ["Length (m)"] = "全長 (m)", ["Top Ø (mm)"] = "末口径 (mm)", ["Taper 1 :"] = "テーパ 1 :",
            ["Design load (kN)"] = "設計荷重 (kN)", ["Automatic embedment"] = "根入れ自動計算", ["Embedment (mm)"] = "根入れ (mm)",
            ["Add"] = "追加", ["Remove"] = "削除", ["Duplicate"] = "複製",
            ["Name (JP)"] = "名称（日本語）", ["Name (EN)"] = "名称（英語）", ["Specification"] = "仕様・寸法", ["Remarks"] = "備考",
            ["Height (mm)"] = "高さ (mm)", ["Measured from"] = "基準", ["GL (up)"] = "GLから上", ["Pole top (down)"] = "柱頂から下",
            ["Direction (°)"] = "方向 (°)", ["Quantity"] = "数量", ["Mounted on"] = "取付先", ["(automatic)"] = "（自動）",
            ["Parameters"] = "パラメータ",
            ["Select an item in the list to edit it."] = "リストの項目を選択すると編集できます。",
            ["Views"] = "図", ["Front view"] = "正面図", ["Side view"] = "側面図", ["Isometric view"] = "立体図",
            ["Labels"] = "引出し", ["None"] = "なし", ["Names"] = "名称", ["Balloons (①②)"] = "番号（①②）",
            ["Label language"] = "図面の言語", ["Japanese"] = "日本語", ["English"] = "英語",
            ["Dimensions"] = "寸法", ["BOM table"] = "物品表", ["Ground line (GL)"] = "地盤線 (GL)", ["Hide hidden lines (wipeouts)"] = "隠れ線処理（ワイプアウト）",
            ["Scale 1 :"] = "縮尺 1 :", ["Text height (paper mm)"] = "文字高さ（用紙 mm）", ["Layer prefix"] = "画層の接頭辞",
            ["Wipeout layer"] = "ワイプアウト画層", ["Insert as one block"] = "1つのブロックとして挿入", ["Title / block name"] = "タイトル・ブロック名",
            ["Front"] = "正面", ["Side"] = "側面", ["3D"] = "3D", ["Sheet"] = "全体", ["Fit"] = "全体表示",
            ["Insert 2D drawing"] = "2D図面を挿入", ["Insert 3D model"] = "3Dモデルを挿入", ["Save job…"] = "保存…", ["Open job…"] = "開く…", ["Close"] = "閉じる",
            ["New entry"] = "新規", ["Save entry"] = "登録", ["Delete entry"] = "登録削除", ["Add to pole"] = "柱に追加",
            ["Template"] = "形状テンプレート", ["Category"] = "分類", ["Unit"] = "単位", ["Default height (mm)"] = "既定の高さ (mm)",
            ["Parameters (w=300; d=200; hgt=400)"] = "パラメータ (w=300; d=200; hgt=400)",
            ["Block front view"] = "ブロック（正面）", ["Block side view"] = "ブロック（側面）", ["Block 3D"] = "ブロック（3D）",
            ["User library entries are saved to %APPDATA%\\PoleTool\\library.json. A block name, if set and present in the drawing, replaces the generated shape (block base point = centre of the equipment)."] =
                "ユーザー登録は %APPDATA%\\PoleTool\\library.json に保存されます。ブロック名を指定し図面内に存在する場合、生成形状の代わりに挿入されます（基点 = 機器の中心）。",
            ["Wheel: zoom · drag: pan · double-click: fit · 3D: drag to orbit"] = "ホイール：ズーム ・ ドラッグ：移動 ・ ダブルクリック：全体 ・ 3D：ドラッグで回転",
            ["Pole top {0} mm above GL · Ø at GL {1:0} mm · embedment {2:0} mm"] = "柱頂 GL+{0} mm ・ GL位置径 {1:0} mm ・ 根入れ {2:0} mm",
            ["Embedment follows the basic values of the technical standard interpretation (art. 59). Check soil and load conditions."] =
                "根入れは電技解釈第59条の基本値によります。地盤・荷重条件を確認してください。",
            ["Equipment parameters"] = "機器パラメータ",
            ["Name"] = "名称",
            ["Equipment names"] = "機器名の言語",
            ["Drawing text"] = "図面の文字",
            ["Label text height (paper mm)"] = "引出し文字高さ（用紙 mm）",
            ["Label text {0:0.0} mm on paper (1:{1:0} → {2:0} mm in the model)"] = "引出し文字 用紙上 {0:0.0} mm（1:{1:0} → モデル上 {2:0} mm）",
            ["Excel material list…"] = "Excel材料表…", ["materials"] = "材料表",
            ["Equipment list, fixing materials (bands, bolts, cable ties …) and cable / pipe lengths of the current pole as an .xlsx file"] = "現在の柱の機器一覧・取付材料（バンド・ボルト・結束バンド等）・ケーブル／管の長さを .xlsx で出力",
            ["Saved: {0}\nOpen it now?"] = "保存しました：{0}\n今すぐ開きますか？",
            ["Cable route at boxes"] = "箱の位置でのケーブル経路",
            ["Behind boxes (between box and pole)"] = "箱の裏（箱と柱の間）", ["In front of boxes"] = "箱の前面", ["Beside boxes (never on top)"] = "箱の横（重ならない）",
            ["Labels as AutoCAD multileaders (MLEADER)"] = "引出線をAutoCADのマルチ引出線（MLEADER）で作成",
            ["Arrange"] = "整列", ["Arranged around the pole: {0} element(s) moved to a free place."] = "柱まわりを整列しました：{0} 個を空いている位置へ移動。",
            ["Moves pipes, boxes and cable runs that overlap around the pole side by side (Ctrl+Z undoes)"] = "柱まわりで重なっている管・箱・ケーブルを横に並べ直します（Ctrl+Z で元に戻す）",
            ["Overlap around the pole: {0} ↔ {1}"] = "柱まわりで重なっています：{0} ↔ {1}",
            ["Insert sheet"] = "シートを挿入", ["Insert current view as 2D"] = "現在の表示を2Dで挿入", ["Insert current view as 3D model"] = "現在の表示で3Dモデルを挿入",
            ["Views, labels, dimensions and BOM as set on the Drawing tab"] = "図面タブの設定どおりの図・引出し・寸法・物品表",
            ["The view shown in the preview (front, side, turned or 3D direction) as clean lines: hidden lines removed, duplicates merged"] = "プレビューに表示中の図（正面・側面・回転・3D方向）を線だけで挿入：隠れ線削除・重複線統合",
            ["3D solids; the AutoCAD view is turned to the preview direction"] = "3Dソリッドで挿入し、AutoCADの視点をプレビューと同じ向きにします",
            ["Insert options (advanced)"] = "挿入オプション（詳細）",
            ["View turned {0:0}° around the pole (button 0° resets)"] = "柱の軸まわりに {0:0}° 回転（0° ボタンで戻す）",
            ["Reset the turn of the front / side views"] = "正面・側面図の回転を元に戻す",
            ["Wheel: zoom · drag: pan (3D: orbit) · Shift+drag: turn around the pole axis (all views) · double-click: fit"] = "ホイール：ズーム ・ ドラッグ：移動（3D：回転） ・ Shift+ドラッグ：柱の軸まわりに回転（全表示） ・ ダブルクリック：全体", ["List names"] = "リストの名称",
            ["Language of labels, captions and the BOM in the preview and in AutoCAD"] = "プレビューとAutoCADの引出し・図名・物品表の言語",
            ["Save as…"] = "名前を付けて保存…", ["Delete"] = "削除", ["Cancel"] = "キャンセル",
            ["Name of the configuration"] = "構成の名前",
            ["'{0}' exists. Overwrite?"] = "「{0}」は既にあります。上書きしますか？",
            ["Saved configuration '{0}'."] = "構成「{0}」を保存しました。",
            ["Built-in configurations cannot be deleted."] = "標準の構成は削除できません。",
            ["Delete configuration '{0}'?"] = "構成「{0}」を削除しますか？",
            ["Replace the current pole with this configuration?"] = "現在の柱をこの構成で置き換えますか？",
            ["Ctrl+drag an element: move it · +Shift: height only · Ctrl+Shift: turn around the pole · Alt: fine steps"] = "Ctrl+ドラッグ：機器を移動 ・ +Shift：高さのみ ・ Ctrl+Shift：柱まわりに回転 ・ Alt：細かく",
            ["Ctrl+drag: up/down = height, across the pole = side. Shift = height only, Ctrl+Shift = turn, Alt = fine. Esc cancels, Ctrl+Z undoes."] = "Ctrl+ドラッグ：上下＝高さ、柱をまたぐ＝方向。Shift＝高さのみ、Ctrl+Shift＝回転、Alt＝細かく。Esc で取消、Ctrl+Z で元に戻す。",
            ["Also switched off: {0}"] = "一緒にOFFにしました：{0}", ["Also switched on: {0}"] = "一緒にONにしました：{0}",
            ["Insert this 3D view as 2D"] = "この3D表示を2Dで挿入",
            ["Projects the pole in the current 3D preview direction, cuts away hidden lines and merges duplicate lines (overkill). No wipeouts."] =
                "現在の3Dプレビューの向きで投影し、隠れ線を削除して重複線を統合（オーバーキル）します。ワイプアウトは使いません。",
            ["Remove hidden lines + overkill (no wipeouts)"] = "隠れ線削除＋重複線統合（ワイプアウトなし）",
            ["Azimuth {0:0}° · elevation {1:0}°"] = "方位 {0:0}° ・ 仰角 {1:0}°",
            ["Wheel: zoom · drag: pan · double-click: fit · 3D: drag to orbit, Shift+drag turns around the pole axis"] = "ホイール：ズーム ・ ドラッグ：移動 ・ ダブルクリック：全体 ・ 3D：ドラッグで回転、Shift+ドラッグで柱の軸まわりに回転", ["Same as UI"] = "表示言語と同じ", ["Japanese + English"] = "日本語＋英語",
            ["Colour"] = "色", ["By layer"] = "画層の色", ["Red"] = "赤", ["Yellow"] = "黄", ["Green"] = "緑", ["Cyan"] = "シアン",
            ["Blue"] = "青", ["Magenta"] = "マゼンタ", ["White / black"] = "白／黒", ["Grey"] = "灰", ["Light grey"] = "薄灰", ["Orange"] = "橙",
            ["Drag equipment: up/down = height, across the pole = side. Esc cancels, Ctrl+Z undoes."] = "機器をドラッグ：上下＝高さ、柱をまたぐ＝取付方向。Esc で取消、Ctrl+Z で元に戻す。",
            ["Drag works in the Front and Side views."] = "ドラッグ移動は正面・側面表示で使えます。",
        };

        /// <summary>Explanations of template parameter keys.</summary>
        public static string Param(string key) => key switch
        {
            "len" => Jp ? "長さ" : "length", "a" => Jp ? "山形鋼の辺" : "angle leg", "t" => Jp ? "板厚" : "thickness",
            "oneSided" => Jp ? "片側腕金 (1/0)" : "one-sided (1/0)", "w" => Jp ? "幅" : "width", "d" => Jp ? "奥行" : "depth",
            "hgt" => Jp ? "高さ" : "height", "dia" => Jp ? "外径" : "outer Ø", "dia2" => Jp ? "外径（2本目以降）" : "Ø of other ducts",
            "top" => Jp ? "上端高さ" : "top height", "bottom" => Jp ? "下端高さ" : "bottom height", "roof" => Jp ? "屋根 (1/0)" : "roof (1/0)",
            "dist" => Jp ? "柱からの距離" : "distance from pole", "insH" => Jp ? "玉碍子の高さ" : "insulator height",
            "guard" => Jp ? "ガード長さ" : "guard length", "pitch" => Jp ? "間隔" : "pitch", "topGap" => Jp ? "柱頂からの空き" : "gap below top",
            "perPhase" => Jp ? "1相あたり個数" : "per phase", "sag" => Jp ? "たるみ" : "sag", "drop" => Jp ? "取付下がり" : "drop",
            "toX" => Jp ? "地中の長さ" : "underground run", "depth" => Jp ? "埋設深さ" : "burial depth", "n" => Jp ? "管の本数" : "number of ducts",
            "from" => Jp ? "接地対象 0=PAS 1=腕金 2=避雷器・避雷針" : "source 0=PAS 1=arms 2=arrester/rod", "onArm" => Jp ? "腕金に取付" : "on arm",
            _ => key
        };
    }
}
