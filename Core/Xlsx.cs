using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Minimal .xlsx writer (no external libraries): several sheets, strings / numbers / formulas,
    /// a few fixed cell styles, column widths, merged cells, frozen header row, print setup.
    /// </summary>
    public sealed class Xlsx
    {
        public enum St { Normal = 0, Header = 1, Cell = 2, Num = 3, Title = 4, Sub = 5, Group = 6, NumBold = 7, Note = 8, CellCenter = 9 }

        public sealed class Sheet
        {
            public string Name;
            public readonly SortedDictionary<int, SortedDictionary<int, (object v, St s)>> Cells = new();
            public readonly List<string> Merges = new List<string>();
            public double[] Widths = new double[0];
            public int FreezeRow;   // rows above this stay visible (0 = none)
            public bool Landscape;
            public void Set(int row, int col, object v, St s = St.Cell)
            {
                if (!Cells.TryGetValue(row, out var r)) Cells[row] = r = new SortedDictionary<int, (object, St)>();
                r[col] = (v, s);
            }
            public void Merge(int r0, int c0, int r1, int c1) => Merges.Add(Ref(r0, c0) + ":" + Ref(r1, c1));
        }

        public readonly List<Sheet> Sheets = new List<Sheet>();
        public Sheet Add(string name) { var s = new Sheet { Name = name }; Sheets.Add(s); return s; }

        public static string Col(int c) { var sb = new StringBuilder(); c++; while (c > 0) { int m = (c - 1) % 26; sb.Insert(0, (char)('A' + m)); c = (c - 1) / 26; } return sb.ToString(); }
        public static string Ref(int r, int c) => Col(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
        static string X(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        public void Save(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            void Put(string name, string content)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < Sheets.Count; i++)
                ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            ct.Append("</Types>");
            Put("[Content_Types].xml", ct.ToString());
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < Sheets.Count; i++)
            {
                wb.Append($"<sheet name=\"{X(Sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            }
            wb.Append("</sheets></workbook>");
            rels.Append($"<Relationship Id=\"rId{Sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Put("xl/workbook.xml", wb.ToString());
            Put("xl/_rels/workbook.xml.rels", rels.ToString());
            Put("xl/styles.xml", Styles());
            for (int i = 0; i < Sheets.Count; i++) Put($"xl/worksheets/sheet{i + 1}.xml", SheetXml(Sheets[i]));
        }

        static string Styles()
        {
            const string font = "游ゴシック";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.##\"/></numFmts>" +
                $"<fonts count=\"5\"><font><sz val=\"10\"/><name val=\"{font}\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"{font}\"/></font>" +
                $"<font><b/><sz val=\"16\"/><name val=\"{font}\"/></font><font><b/><sz val=\"10\"/><name val=\"{font}\"/></font><font><sz val=\"9\"/><color rgb=\"FF666666\"/><name val=\"{font}\"/></font></fonts>" +
                "<fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F5D8A\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF2F2F2\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
                "<border><left style=\"thin\"><color rgb=\"FF9FB3C8\"/></left><right style=\"thin\"><color rgb=\"FF9FB3C8\"/></right><top style=\"thin\"><color rgb=\"FF9FB3C8\"/></top><bottom style=\"thin\"><color rgb=\"FF9FB3C8\"/></bottom><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"10\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                                                                         // Normal
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" + // Header
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +          // Cell
                "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" + // Num
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                                                 // Title
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                                                                 // Sub
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"><alignment vertical=\"center\"/></xf>" +           // Group
                "<xf numFmtId=\"164\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +                     // NumBold
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf>" +             // Note
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" + // CellCenter
                "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
        }

        static string SheetXml(Sheet s)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"");
            if (s.FreezeRow > 0) sb.Append($"><pane ySplit=\"{s.FreezeRow}\" topLeftCell=\"A{s.FreezeRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            else sb.Append("/></sheetViews>");
            if (s.Widths.Length > 0)
            {
                sb.Append("<cols>");
                for (int i = 0; i < s.Widths.Length; i++) sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{s.Widths[i].ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            foreach (var (r, cols) in s.Cells)
            {
                sb.Append($"<row r=\"{r + 1}\">");
                foreach (var (c, (v, st)) in cols)
                {
                    string rf = Ref(r, c); int si = (int)st;
                    switch (v)
                    {
                        case null: sb.Append($"<c r=\"{rf}\" s=\"{si}\"/>"); break;
                        case double or int or float or long:
                            sb.Append($"<c r=\"{rf}\" s=\"{si}\"><v>{Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture)}</v></c>"); break;
                        case string str when str.StartsWith("="):
                            sb.Append($"<c r=\"{rf}\" s=\"{si}\"><f>{X(str.Substring(1))}</f></c>"); break;
                        default:
                            sb.Append($"<c r=\"{rf}\" s=\"{si}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(v.ToString())}</t></is></c>"); break;
                    }
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (s.Merges.Count > 0) { sb.Append($"<mergeCells count=\"{s.Merges.Count}\">"); foreach (var m in s.Merges) sb.Append($"<mergeCell ref=\"{m}\"/>"); sb.Append("</mergeCells>"); }
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append($"<pageSetup paperSize=\"9\" orientation=\"{(s.Landscape ? "landscape" : "portrait")}\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }
    }
}
