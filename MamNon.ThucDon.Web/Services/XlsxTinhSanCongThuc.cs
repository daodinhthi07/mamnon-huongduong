using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Bổ sung KẾT QUẢ TÍNH SẴN (&lt;v&gt;) cho mọi ô công thức trong file .xlsx còn thiếu.
/// Vì sao cần: ClosedXML chỉ ghi công thức; kết quả chỉ có khi <c>RecalculateAllFormulas()</c> chạy thành công.
/// Nếu bước đó lỗi (hoặc bị bỏ qua) thì file chỉ mở đúng trên Excel máy tính (tự tính lại khi mở). Điện thoại,
/// Zalo/Drive xem trước, Protected View không tự tính nên các ô công thức (tên hàng, ĐVT, đơn giá, thành tiền, tổng...)
/// hiện TRỐNG. Lớp này tự tính các công thức đơn giản đang dùng trong bảng kê (+ - * /, so sánh, SUM, IF, VLOOKUP tra chính xác)
/// rồi ghi kết quả vào file, không phụ thuộc bộ tính của ClosedXML. Công thức nào không hiểu được thì bỏ qua (giữ nguyên).
/// Mọi lỗi đều được nuốt: tệ nhất là trả lại đúng file ban đầu.
/// </summary>
public static class XlsxTinhSanCongThuc
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelDoc = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace RelPkg = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] BoSungGiaTri(byte[] xlsx, ILogger? logger = null)
    {
        try
        {
            return XuLy(xlsx, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Không bổ sung được kết quả tính sẵn cho các ô công thức trong file Excel; giữ nguyên file gốc.");
            return xlsx;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Đọc / ghi gói .xlsx
    // ------------------------------------------------------------------------------------------------

    private static byte[] XuLy(byte[] xlsx, ILogger? logger)
    {
        var tenEntry = new List<string>();
        var noiDung = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var vao = new MemoryStream(xlsx))
        using (var zip = new ZipArchive(vao, ZipArchiveMode.Read))
        {
            foreach (var e in zip.Entries)
            {
                using var luong = e.Open();
                using var ms = new MemoryStream();
                luong.CopyTo(ms);
                tenEntry.Add(e.FullName);
                noiDung[e.FullName] = ms.ToArray();
            }
        }

        if (!noiDung.TryGetValue("xl/workbook.xml", out var wbBytes) ||
            !noiDung.TryGetValue("xl/_rels/workbook.xml.rels", out var relBytes))
            return xlsx;

        var wbDoc = DocTuBytes(wbBytes);
        var relDoc = DocTuBytes(relBytes);
        var mucTieu = new Dictionary<string, string>();
        foreach (var r in relDoc.Descendants(RelPkg + "Relationship"))
        {
            var id = (string?)r.Attribute("Id");
            var tg = (string?)r.Attribute("Target");
            if (id != null && tg != null) mucTieu[id] = tg;
        }

        var chuoi = new List<string>();
        if (noiDung.TryGetValue("xl/sharedStrings.xml", out var ssBytes))
        {
            var ssRoot = DocTuBytes(ssBytes).Root;
            if (ssRoot != null)
            {
                foreach (var si in ssRoot.Elements(Main + "si"))
                {
                    var sb = new StringBuilder();
                    foreach (var t in si.Descendants(Main + "t"))
                    {
                        if (t.Parent != null && t.Parent.Name == Main + "rPh") continue;
                        sb.Append(t.Value);
                    }
                    chuoi.Add(sb.ToString());
                }
            }
        }

        var cacSheet = new List<SheetData>();
        foreach (var sh in wbDoc.Descendants(Main + "sheet"))
        {
            var ten = (string?)sh.Attribute("name");
            var rid = (string?)sh.Attribute(RelDoc + "id");
            if (ten == null || rid == null || !mucTieu.TryGetValue(rid, out var tg)) continue;
            var duongDan = tg.StartsWith("/") ? tg.TrimStart('/') : "xl/" + tg;
            if (!noiDung.TryGetValue(duongDan, out var shBytes)) continue;
            cacSheet.Add(new SheetData(ten, duongDan, DocTuBytes(shBytes)));
        }

        var o = new Dictionary<(string, int, int), Cell>();
        foreach (var sh in cacSheet)
        {
            foreach (var c in sh.Doc.Descendants(Main + "c"))
            {
                var diaChi = (string?)c.Attribute("r");
                if (diaChi == null || !ThamChieuO(diaChi, out var hang, out var cot)) continue;
                var f = c.Element(Main + "f");
                var v = c.Element(Main + "v");
                var kieu = (string?)c.Attribute("t");
                object? giaTri = null;
                if (v != null && v.Value.Length > 0) giaTri = DocGiaTri(kieu, v.Value, chuoi);
                else if (kieu == "inlineStr") giaTri = string.Concat(c.Descendants(Main + "t").Select(x => x.Value));
                var cell = new Cell(sh.Ten, c, f?.Value, giaTri);
                if (cell.Formula != null && cell.Value != null)
                {
                    cell.CoSan = true;     // ClosedXML đã tính sẵn rồi
                    cell.Done = true;
                    cell.KetQua = cell.Value;
                }
                o[(sh.Ten, hang, cot)] = cell;
            }
        }

        var may = new MayTinh(o);
        var soBoSung = 0;
        var soBoQua = 0;
        var soCoSan = 0;
        foreach (var c in o.Values)
        {
            if (c.Formula == null) continue;
            if (c.CoSan) { soCoSan++; continue; }
            var kq = may.GiaTri(c);
            var loi = kq as Loi;
            if (loi != null && loi.KhongHoTro) { soBoQua++; continue; }
            GhiGiaTri(c.El, kq);
            soBoSung++;
        }

        logger?.LogInformation("Kết quả tính sẵn của công thức: đã có {CoSan}, bổ sung {BoSung}, bỏ qua (không hiểu được) {BoQua}.", soCoSan, soBoSung, soBoQua);
        if (soBoSung == 0) return xlsx;

        foreach (var sh in cacSheet)
        {
            using var ms = new MemoryStream();
            using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
            {
                sh.Doc.Save(w);
            }
            noiDung[sh.DuongDan] = ms.ToArray();
        }

        using var ra = new MemoryStream();
        using (var zipRa = new ZipArchive(ra, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var ten in tenEntry)
            {
                var entry = zipRa.CreateEntry(ten, CompressionLevel.Optimal);
                using var ghi = entry.Open();
                var bytes = noiDung[ten];
                ghi.Write(bytes, 0, bytes.Length);
            }
        }
        return ra.ToArray();
    }

    private static XDocument DocTuBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return XDocument.Load(ms, LoadOptions.PreserveWhitespace);
    }

    private static object DocGiaTri(string? kieu, string text, List<string> chuoi)
    {
        switch (kieu)
        {
            case "s":
                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < chuoi.Count ? chuoi[i] : "";
            case "str":
            case "inlineStr":
                return text;
            case "e":
                return new Loi(text, false);
            case "b":
                return text == "1";
            default:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                return text;
        }
    }

    private static void GhiGiaTri(XElement el, object? kq)
    {
        el.Elements(Main + "v").Remove();
        string text;
        string? kieu = null;
        if (kq is double d)
        {
            text = d.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (kq is bool b)
        {
            text = b ? "1" : "0";
            kieu = "b";
        }
        else if (kq is string s)
        {
            text = s;
            kieu = "str";
        }
        else if (kq is Loi l)
        {
            text = l.Ma;
            kieu = "e";
        }
        else
        {
            text = "0";   // công thức trỏ tới ô trống -> Excel hiện 0
        }
        el.SetAttributeValue("t", kieu);   // null = bỏ thuộc tính t (số)
        el.Add(new XElement(Main + "v", text));
    }

    /// <summary>"B148" -> (hàng 148, cột 2). Không có $ (đã bỏ trước khi gọi).</summary>
    private static bool ThamChieuO(string r, out int hang, out int cot)
    {
        hang = 0;
        cot = 0;
        var i = 0;
        while (i < r.Length && char.IsLetter(r[i]))
        {
            cot = cot * 26 + (char.ToUpperInvariant(r[i]) - 'A' + 1);
            i++;
        }
        if (i == 0 || i == r.Length) return false;
        return int.TryParse(r.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out hang) && hang > 0;
    }

    // ------------------------------------------------------------------------------------------------
    // Dữ liệu ô
    // ------------------------------------------------------------------------------------------------

    private sealed class SheetData
    {
        public SheetData(string ten, string duongDan, XDocument doc)
        {
            Ten = ten;
            DuongDan = duongDan;
            Doc = doc;
        }
        public string Ten { get; }
        public string DuongDan { get; }
        public XDocument Doc { get; }
    }

    private sealed class Cell
    {
        public Cell(string sheet, XElement el, string? formula, object? value)
        {
            Sheet = sheet;
            El = el;
            Formula = formula;
            Value = value;
        }
        public string Sheet { get; }
        public XElement El { get; }
        public string? Formula { get; }
        public object? Value { get; }
        public bool CoSan { get; set; }
        public bool Done { get; set; }
        public bool Dang { get; set; }
        public object? KetQua { get; set; }
    }

    /// <summary>Giá trị lỗi của Excel (#DIV/0!, #N/A...) hoặc "không hiểu được công thức" (KhongHoTro).</summary>
    private sealed class Loi
    {
        public Loi(string ma, bool khongHoTro)
        {
            Ma = ma;
            KhongHoTro = khongHoTro;
        }
        public string Ma { get; }
        public bool KhongHoTro { get; }

        public static readonly Loi Chia0 = new Loi("#DIV/0!", false);
        public static readonly Loi Value = new Loi("#VALUE!", false);
        public static readonly Loi NA = new Loi("#N/A", false);
        public static readonly Loi Ref = new Loi("#REF!", false);
        public static readonly Loi ChuaHieu = new Loi("#KHONGHOTRO", true);
    }

    private sealed class Vung
    {
        public Vung(string sheet, int h1, int c1, int h2, int c2)
        {
            Sheet = sheet;
            H1 = h1;
            C1 = c1;
            H2 = h2;
            C2 = c2;
        }
        public string Sheet { get; }
        public int H1 { get; }
        public int C1 { get; }
        public int H2 { get; }
        public int C2 { get; }
    }

    private sealed class MayTinh
    {
        private readonly Dictionary<(string, int, int), Cell> _o;

        public MayTinh(Dictionary<(string, int, int), Cell> o)
        {
            _o = o;
        }

        public object? GiaTri(string sheet, int hang, int cot)
        {
            return _o.TryGetValue((sheet, hang, cot), out var c) ? GiaTri(c) : null;
        }

        public object? GiaTri(Cell c)
        {
            if (c.Formula == null) return c.Value;
            if (c.Done) return c.KetQua;
            if (c.Dang) return Loi.ChuaHieu;      // vòng lặp tham chiếu
            c.Dang = true;
            try
            {
                if (c.Formula.Length == 0) c.KetQua = Loi.ChuaHieu;   // công thức dùng chung (shared) — không đọc được
                else c.KetQua = new PhanTich(this, c.Sheet, c.Formula).TinhToan();
            }
            catch (NotSupportedException)
            {
                c.KetQua = Loi.ChuaHieu;
            }
            catch (FormatException)
            {
                c.KetQua = Loi.ChuaHieu;
            }
            finally
            {
                c.Dang = false;
            }
            c.Done = true;
            return c.KetQua;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Bộ phân tích + tính công thức
    // ------------------------------------------------------------------------------------------------

    private sealed class PhanTich
    {
        private static readonly Regex MauToken = new Regex(
            @"\G\s*(?:(?<num>\d+\.?\d*(?:[eE][+-]?\d+)?)|(?<str>""[^""]*"")|(?<ref>(?:'[^']+'!|[A-Za-z_][\w.]*!)?\$?[A-Z]{1,3}\$?\d+(?::\$?[A-Z]{1,3}\$?\d+)?)|(?<fn>[A-Za-z_][\w.]*)\(|(?<op><=|>=|<>|[-+*/(),=<>]))",
            RegexOptions.CultureInvariant);

        private readonly MayTinh _m;
        private readonly string _sheet;
        private readonly List<KeyValuePair<string, string>> _t = new List<KeyValuePair<string, string>>();
        private int _i;

        public PhanTich(MayTinh m, string sheet, string congThuc)
        {
            _m = m;
            _sheet = sheet;
            var f = congThuc.Trim();
            if (f.StartsWith("=")) f = f.Substring(1);
            var pos = 0;
            while (pos < f.Length)
            {
                var kq = MauToken.Match(f, pos);
                if (!kq.Success)
                {
                    if (f.Substring(pos).Trim().Length == 0) break;
                    throw new NotSupportedException();
                }
                pos = kq.Index + kq.Length;
                if (kq.Groups["num"].Success) _t.Add(new KeyValuePair<string, string>("num", kq.Groups["num"].Value));
                else if (kq.Groups["str"].Success) _t.Add(new KeyValuePair<string, string>("str", kq.Groups["str"].Value));
                else if (kq.Groups["ref"].Success) _t.Add(new KeyValuePair<string, string>("ref", kq.Groups["ref"].Value));
                else if (kq.Groups["fn"].Success) _t.Add(new KeyValuePair<string, string>("fn", kq.Groups["fn"].Value));
                else _t.Add(new KeyValuePair<string, string>("op", kq.Groups["op"].Value));
            }
        }

        public object? TinhToan()
        {
            var r = SoSanh();
            if (_i != _t.Count) throw new NotSupportedException();
            return Don(r);
        }

        private bool Peek(out string loai, out string text)
        {
            if (_i < _t.Count)
            {
                loai = _t[_i].Key;
                text = _t[_i].Value;
                return true;
            }
            loai = "";
            text = "";
            return false;
        }

        private void Can(string ky)
        {
            string loai;
            string text;
            if (!Peek(out loai, out text) || text != ky) throw new NotSupportedException();
            _i++;
        }

        private object? Don(object? v)
        {
            var vung = v as Vung;
            if (vung != null) return _m.GiaTri(vung.Sheet, vung.H1, vung.C1);
            return v;
        }

        /// <summary>Đổi về số: trả double hoặc Loi.</summary>
        private object So(object? v)
        {
            var g = Don(v);
            if (g is Loi) return g;
            if (g == null) return 0.0;
            if (g is bool) return (bool)g ? 1.0 : 0.0;
            if (g is double) return g;
            var s = g as string;
            if (s != null)
            {
                if (s.Length == 0) return 0.0;
                double d;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return Loi.Value;
            }
            return Loi.Value;
        }

        private object? SoSanh()
        {
            var a = Cong();
            string loai;
            string text;
            if (Peek(out loai, out text) && loai == "op" &&
                (text == "=" || text == "<>" || text == "<" || text == ">" || text == "<=" || text == ">="))
            {
                _i++;
                var b = Cong();
                var x = Don(a);
                var y = Don(b);
                if (x is Loi) return x;
                if (y is Loi) return y;
                if (x == null) x = 0.0;
                if (y == null) y = 0.0;
                int kq;
                if (x is string && y is string) kq = string.Compare((string)x, (string)y, StringComparison.OrdinalIgnoreCase);
                else if (x is double && y is double) kq = ((double)x).CompareTo((double)y);
                else if (x is bool && y is bool) kq = ((bool)x).CompareTo((bool)y);
                else if (x is string) kq = 1;
                else if (y is string) kq = -1;
                else kq = 0;
                switch (text)
                {
                    case "=": return kq == 0;
                    case "<>": return kq != 0;
                    case "<": return kq < 0;
                    case ">": return kq > 0;
                    case "<=": return kq <= 0;
                    default: return kq >= 0;
                }
            }
            return a;
        }

        private object? Cong()
        {
            var a = Nhan();
            string loai;
            string text;
            while (Peek(out loai, out text) && loai == "op" && (text == "+" || text == "-"))
            {
                _i++;
                var b = Nhan();
                var x = So(a);
                var y = So(b);
                if (x is Loi) a = x;
                else if (y is Loi) a = y;
                else if (text == "+") a = (double)x + (double)y;
                else a = (double)x - (double)y;
            }
            return a;
        }

        private object? Nhan()
        {
            var a = DauAm();
            string loai;
            string text;
            while (Peek(out loai, out text) && loai == "op" && (text == "*" || text == "/"))
            {
                _i++;
                var b = DauAm();
                var x = So(a);
                var y = So(b);
                if (x is Loi) a = x;
                else if (y is Loi) a = y;
                else if (text == "*") a = (double)x * (double)y;
                else if ((double)y == 0.0) a = Loi.Chia0;
                else a = (double)x / (double)y;
            }
            return a;
        }

        private object? DauAm()
        {
            string loai;
            string text;
            if (Peek(out loai, out text) && loai == "op" && (text == "-" || text == "+"))
            {
                _i++;
                var v = So(DauAm());
                if (v is Loi) return v;
                if (text == "-") return -(double)v;
                return (double)v;
            }
            return NguyenTo();
        }

        private object? NguyenTo()
        {
            string loai;
            string text;
            if (!Peek(out loai, out text)) throw new NotSupportedException();

            if (loai == "num")
            {
                _i++;
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (loai == "str")
            {
                _i++;
                return text.Substring(1, text.Length - 2);
            }
            if (loai == "ref")
            {
                _i++;
                return TaoVung(text);
            }
            if (loai == "op" && text == "(")
            {
                _i++;
                var r = SoSanh();
                Can(")");
                return r;
            }
            if (loai == "fn")
            {
                _i++;
                var args = new List<object?>();
                string l2;
                string t2;
                if (Peek(out l2, out t2) && t2 == ")")
                {
                    _i++;
                }
                else
                {
                    while (true)
                    {
                        args.Add(SoSanh());
                        string l3;
                        string t3;
                        if (Peek(out l3, out t3) && t3 == ",")
                        {
                            _i++;
                            continue;
                        }
                        Can(")");
                        break;
                    }
                }
                return GoiHam(text.ToUpperInvariant(), args);
            }
            throw new NotSupportedException();
        }

        private Vung TaoVung(string text)
        {
            var sheet = _sheet;
            var idx = text.LastIndexOf('!');
            if (idx >= 0)
            {
                sheet = text.Substring(0, idx).Trim('\'');
                text = text.Substring(idx + 1);
            }
            text = text.Replace("$", "");
            var parts = text.Split(':');
            int h1;
            int c1;
            if (!ThamChieuO(parts[0], out h1, out c1)) throw new NotSupportedException();
            var h2 = h1;
            var c2 = c1;
            if (parts.Length > 1 && !ThamChieuO(parts[1], out h2, out c2)) throw new NotSupportedException();
            return new Vung(sheet, Math.Min(h1, h2), Math.Min(c1, c2), Math.Max(h1, h2), Math.Max(c1, c2));
        }

        private object? GoiHam(string ten, List<object?> args)
        {
            if (ten == "SUM")
            {
                double tong = 0;
                foreach (var a in args)
                {
                    var vung = a as Vung;
                    if (vung != null)
                    {
                        if ((long)(vung.H2 - vung.H1 + 1) * (vung.C2 - vung.C1 + 1) > 200000) throw new NotSupportedException();
                        for (var h = vung.H1; h <= vung.H2; h++)
                        {
                            for (var c = vung.C1; c <= vung.C2; c++)
                            {
                                var g = _m.GiaTri(vung.Sheet, h, c);
                                if (g is Loi) return g;
                                if (g is double) tong += (double)g;
                            }
                        }
                    }
                    else
                    {
                        var n = So(a);
                        if (n is Loi) return n;
                        tong += (double)n;
                    }
                }
                return tong;
            }

            if (ten == "IF")
            {
                if (args.Count < 2) throw new NotSupportedException();
                var dk = Don(args[0]);
                if (dk is Loi) return dk;
                bool dung;
                if (dk is bool) dung = (bool)dk;
                else if (dk is double) dung = (double)dk != 0.0;
                else if (dk == null) dung = false;
                else return Loi.Value;
                if (dung) return Don(args[1]);
                if (args.Count > 2) return Don(args[2]);
                return false;
            }

            if (ten == "VLOOKUP")
            {
                // Chỉ hỗ trợ tra cứu CHÍNH XÁC (tham số thứ 4 = 0 / FALSE) — đúng cách bảng kê đang dùng.
                if (args.Count < 4) throw new NotSupportedException();
                var khoa = Don(args[0]);
                var vung = args[1] as Vung;
                if (vung == null) throw new NotSupportedException();
                var cs = So(args[2]);
                var kieu = Don(args[3]);
                var chinhXac = (kieu is double && (double)kieu == 0.0) || (kieu is bool && !(bool)kieu);
                if (!chinhXac) throw new NotSupportedException();
                if (khoa is Loi) return khoa;
                if (cs is Loi) return cs;
                var cotTra = (int)(double)cs;
                if (cotTra < 1 || vung.C1 + cotTra - 1 > vung.C2) return Loi.Ref;
                if (vung.H2 - vung.H1 + 1 > 200000) throw new NotSupportedException();
                for (var h = vung.H1; h <= vung.H2; h++)
                {
                    var g = _m.GiaTri(vung.Sheet, h, vung.C1);
                    if (g == null) continue;
                    var gs = g as string;
                    var ks = khoa as string;
                    var bang = (gs != null && ks != null && string.Equals(gs, ks, StringComparison.OrdinalIgnoreCase))
                               || (g is double && khoa is double && (double)g == (double)khoa);
                    if (bang) return _m.GiaTri(vung.Sheet, h, vung.C1 + cotTra - 1);
                }
                return Loi.NA;
            }

            throw new NotSupportedException();   // hàm khác: bỏ qua ô này, không đoán
        }
    }
}
