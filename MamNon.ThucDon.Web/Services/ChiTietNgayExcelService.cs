using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace MamNon.ThucDon.Web.Services;

/// <summary>Kết quả đọc 1 file Excel "chi tiết trong ngày" (Ok = false thì Loi là lý do để báo cho người tải file).</summary>
public sealed record KetQuaDocChiTietNgay(bool Ok, string? Loi, DateOnly? Ngay, string? Json, int SoDongSang, int SoDongTrua);

/// <summary>
/// Đọc file Excel "BẢNG THEO DÕI VÀ ĐÁNH GIÁ CHẤT LƯỢNG THỰC PHẨM" của MỘT ngày (2 sheet: ĂN SÁNG + ĂN TRƯA, mỗi cơ sở 1 cột:
/// PHÂN HIỆU 2 / ĐIỂM TRƯỜNG 4) và đổi sang đúng JSON mà tab "Chi tiết" của trang phụ huynh (tong-hop-ngay.js) đang đọc:
/// { ok, ngay, coSo: [ { ma, ten, soSuatAnSang, soSuatNhaTre, soSuatMauGiao, coThucDon, sang, chinh, giaVi } ] }.
/// Cột được tìm theo chữ ở dòng tiêu đề (không cố định theo chữ cái cột) nên file thêm/bớt cột trống vẫn đọc được.
/// Không tính toán gì thêm: số lượng, đơn giá, thành tiền lấy NGUYÊN theo file.
/// </summary>
public static class ChiTietNgayExcelService
{
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web);
    private static readonly Regex CoSoRx = new(@"(PHAN HIEU|DIEM TRUONG)\s*(\d+)?", RegexOptions.Compiled);
    private static readonly Regex NgayRx = new(@"NGAY\s+(\d{1,2})\s+THANG\s+(\d{1,2})\s+NAM\s+(\d{4})", RegexOptions.Compiled);
    private static readonly Regex SoRx = new(@"^\d+([.,]0+)?$", RegexOptions.Compiled);

    private sealed class DongSang
    {
        public int Stt { get; set; }          // STT đúng như cột A của file
        public int HangHoaId { get; set; }
        public string TenHang { get; set; } = "";
        public string? Dvt { get; set; }
        public decimal SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien { get; set; }
    }

    private sealed class DongChinh
    {
        public int Stt { get; set; }          // STT đúng như cột A của file (đánh số riêng cho từng khối)
        public string Khoi { get; set; } = "";   // "nt" = Nhà trẻ, "mg" = Mẫu giáo
        public int HangHoaId { get; set; }
        public string TenHang { get; set; } = "";
        public string? Dvt { get; set; }
        public decimal SlNhaTre { get; set; }
        public decimal SlMauGiao { get; set; }
        public decimal DonGia { get; set; }
        public decimal TtNhaTre { get; set; }
        public decimal TtMauGiao { get; set; }
    }

    private sealed class CoSoKq
    {
        public int CoSoId { get; set; }
        public string Ma { get; set; } = "";
        public string Ten { get; set; } = "";
        public int SoSuatAnSang { get; set; }
        public int SoSuatNhaTre { get; set; }
        public int SoSuatMauGiao { get; set; }
        public bool CoThucDon { get; set; }
        public List<DongSang> Sang { get; set; } = new();
        public List<DongChinh> Chinh { get; set; } = new();
        public List<object> GiaVi { get; set; } = new();
    }

    private sealed class CotBang
    {
        public int HangTieuDe;
        public int Gia;
        public int CotCuoi;
        public List<(string Ma, string Ten)> CoSo = new();
        public Dictionary<string, int> Nt = new();   // số lượng Nhà trẻ theo cơ sở
        public Dictionary<string, int> Mg = new();   // số lượng Mẫu giáo theo cơ sở
        public Dictionary<string, int> Tp = new();   // "TỔNG TP" theo cơ sở (sheet ĂN SÁNG)
        public Dictionary<string, int> Tt = new();   // thành tiền theo cơ sở
    }

    public static KetQuaDocChiTietNgay Doc(Stream stream)
    {
        try
        {
            using var wb = new XLWorkbook(stream);
            var wsSang = wb.Worksheets.FirstOrDefault(w => Nd(w.Name).Contains("AN SANG"));
            var wsTrua = wb.Worksheets.FirstOrDefault(w => Nd(w.Name).Contains("AN TRUA"));
            if (wsSang is null || wsTrua is null)
                return Loi("Không thấy 2 sheet \"ĂN SÁNG\" và \"ĂN TRƯA\". Hãy tải đúng file chi tiết trong ngày của nhà trường.");

            var ngay = TimNgay(wsSang) ?? TimNgay(wsTrua);
            if (ngay is null)
                return Loi("Không đọc được ngày ở đầu file (dạng \"Thứ hai, ngày 14 tháng 9 năm 2026\").");

            var cotS = TimCot(wsSang);
            var cotT = TimCot(wsTrua);
            if (cotS is null || cotT is null)
                return Loi("Không thấy dòng tiêu đề (STT, Tên thực phẩm, ...) ở sheet ĂN SÁNG hoặc ĂN TRƯA.");

            var coSoList = new List<(string Ma, string Ten)>(cotS.CoSo);
            foreach (var c in cotT.CoSo)
                if (!coSoList.Any(x => x.Ma == c.Ma)) coSoList.Add(c);
            if (coSoList.Count == 0)
                return Loi("Không nhận ra cột cơ sở (PHÂN HIỆU / ĐIỂM TRƯỜNG) ở dòng tiêu đề.");

            var ds = coSoList.ToDictionary(c => c.Ma, c => new CoSoKq { Ma = c.Ma, Ten = c.Ten });

            // ------------------------------------------------ sheet ĂN SÁNG
            var lastS = wsSang.LastRowUsed()?.RowNumber() ?? 0;
            var daCoDongS = false;
            for (var r = cotS.HangTieuDe + 1; r <= lastS; r++)
            {
                var a = Nd(wsSang.Cell(r, 1).GetString());
                var b = Nd(wsSang.Cell(r, 2).GetString());
                if (a.StartsWith("SO SUAT AN SANG"))
                {
                    foreach (var kv in ds)
                    {
                        var v = So(wsSang, r, Cot(cotS.Tp, kv.Key));
                        if (v == 0) v = So(wsSang, r, Cot(cotS.Nt, kv.Key)) + So(wsSang, r, Cot(cotS.Mg, kv.Key));
                        kv.Value.SoSuatAnSang = (int)Math.Round(v);
                    }
                    continue;
                }
                if (a.StartsWith("TONG CONG") || b.StartsWith("TONG CONG"))
                {
                    if (daCoDongS) break;   // dòng \"Tổng cộng\" cuối bảng
                    continue;               // dòng \"Tổng cộng\" nằm ngay dưới tiêu đề (Tăng / Giảm / Tổng cộng) — bỏ qua
                }
                if (!LaStt(wsSang.Cell(r, 1))) continue;
                daCoDongS = true;
                var ten = wsSang.Cell(r, 2).GetString().Trim();
                if (ten.Length == 0) continue;
                var dvt = wsSang.Cell(r, 3).GetString().Trim();
                var stt = (int)Math.Round(So(wsSang, r, 1));
                var gia = So(wsSang, r, cotS.Gia);
                foreach (var kv in ds)
                {
                    var sl = So(wsSang, r, Cot(cotS.Tp, kv.Key));
                    var tt = So(wsSang, r, Cot(cotS.Tt, kv.Key));
                    // Giữ MỌI dòng của file (kể cả dòng toàn số 0) để hiển thị đúng như Excel.
                    kv.Value.Sang.Add(new DongSang { Stt = stt, TenHang = ten, Dvt = dvt, SoLuong = sl, DonGia = gia, ThanhTien = tt });
                }
            }

            // ------------------------------------------------ sheet ĂN TRƯA (khối NHÀ TRẺ rồi khối MẪU GIÁO)
            var lastT = wsTrua.LastRowUsed()?.RowNumber() ?? 0;
            string? khoi = null;
            var dem = 0;
            for (var r = cotT.HangTieuDe + 1; r <= lastT; r++)
            {
                var a = Nd(wsTrua.Cell(r, 1).GetString());
                if (a.StartsWith("TONG CONG"))
                {
                    // \"TỔNG CỘNG NHÀ TRẺ\" / \"TỔNG CỘNG MẪU GIÁO\" / \"... + ...\" hoặc \"Tổng cộng\" cuối khối = hết khối.
                    // \"Tổng cộng\" nằm ngay dưới tiêu đề khối (chưa có dòng nguyên liệu nào) thì chỉ là dòng tiêu đề phụ.
                    if (a.Length > 9 || dem > 0) { khoi = null; dem = 0; }
                    continue;
                }
                if (a == "NHA TRE") { khoi = "nt"; dem = 0; continue; }
                if (a.Length == 0 && LaDongMauGiao(wsTrua, r, cotT.CotCuoi)) { khoi = "mg"; dem = 0; continue; }
                if (a.StartsWith("SO SUAT AN TRUA"))
                {
                    foreach (var kv in ds)
                    {
                        if (khoi == "nt") kv.Value.SoSuatNhaTre = (int)Math.Round(So(wsTrua, r, Cot(cotT.Nt, kv.Key)));
                        else if (khoi == "mg") kv.Value.SoSuatMauGiao = (int)Math.Round(So(wsTrua, r, Cot(cotT.Mg, kv.Key)));
                    }
                    continue;
                }
                if (khoi is null || !LaStt(wsTrua.Cell(r, 1))) continue;
                dem++;
                var ten = wsTrua.Cell(r, 2).GetString().Trim();
                if (ten.Length == 0) continue;
                var dvt = wsTrua.Cell(r, 3).GetString().Trim();
                var stt = (int)Math.Round(So(wsTrua, r, 1));
                var gia = So(wsTrua, r, cotT.Gia);
                foreach (var kv in ds)
                {
                    var sl = So(wsTrua, r, Cot(khoi == "nt" ? cotT.Nt : cotT.Mg, kv.Key));
                    var tt = So(wsTrua, r, Cot(cotT.Tt, kv.Key));
                    // Giữ MỌI dòng của file (kể cả dòng toàn số 0, và các dòng trùng tên như 2 dòng Cà rốt) để hiển thị đúng như Excel.
                    kv.Value.Chinh.Add(khoi == "nt"
                        ? new DongChinh { Stt = stt, Khoi = "nt", TenHang = ten, Dvt = dvt, SlNhaTre = sl, DonGia = gia, TtNhaTre = tt }
                        : new DongChinh { Stt = stt, Khoi = "mg", TenHang = ten, Dvt = dvt, SlMauGiao = sl, DonGia = gia, TtMauGiao = tt });
                }
            }

            var soSang = ds.Values.Sum(x => x.Sang.Count(d => d.SoLuong != 0 || d.ThanhTien != 0));
            var soTrua = ds.Values.Sum(x => x.Chinh.Count(d => d.SlNhaTre != 0 || d.SlMauGiao != 0 || d.TtNhaTre != 0 || d.TtMauGiao != 0));
            if (soSang + soTrua == 0)
                return Loi("File không có dòng nguyên liệu nào (cột STT 1, 2, 3...). Kiểm tra lại file.");
            foreach (var c in ds.Values) c.CoThucDon = c.Sang.Any(d => d.SoLuong != 0 || d.ThanhTien != 0) || c.Chinh.Any(d => d.SlNhaTre != 0 || d.SlMauGiao != 0 || d.TtNhaTre != 0 || d.TtMauGiao != 0);

            var json = JsonSerializer.Serialize(new
            {
                ok = true,
                ngay = ngay.Value.ToString("yyyy-MM-dd"),
                nguon = "file",
                coSo = ds.Values.ToList(),
            }, JsonOpt);
            return new KetQuaDocChiTietNgay(true, null, ngay, json, soSang, soTrua);
        }
        catch (Exception ex)
        {
            return Loi("Không đọc được file Excel (" + ex.Message + "). Hãy lưu lại dạng .xlsx rồi tải lên.");
        }
    }

    private static KetQuaDocChiTietNgay Loi(string loi) => new(false, loi, null, null, 0, 0);

    // ------------------------------------------------------------------ tìm cột theo chữ ở dòng tiêu đề
    private static CotBang? TimCot(IXLWorksheet ws)
    {
        var hr = 0;
        for (var r = 1; r <= 15; r++)
            if (Nd(ws.Cell(r, 1).GetString()) == "STT") { hr = r; break; }
        if (hr == 0) return null;

        var cot = new CotBang { HangTieuDe = hr, CotCuoi = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 13) };
        for (var c = 1; c <= cot.CotCuoi; c++)
        {
            var h = Nd(ws.Cell(hr, c).GetString());
            if (h.Length == 0) continue;
            if (h.Contains("DON GIA")) cot.Gia = c;
            var m = CoSoRx.Match(h);
            if (!m.Success) continue;
            var laPh = m.Groups[1].Value == "PHAN HIEU";
            var ma = laPh ? "PH" : "ĐT";
            var ten = (laPh ? "Phân hiệu" : "Điểm trường") + (m.Groups[2].Success ? " " + m.Groups[2].Value : "");
            if (!cot.CoSo.Any(x => x.Ma == ma)) cot.CoSo.Add((ma, ten));
            if (h.Contains("THANH TIEN")) cot.Tt[ma] = c;
            else if (h.Contains("TONG TP")) cot.Tp[ma] = c;
            else if (h.Contains("NHA TRE")) cot.Nt[ma] = c;
            else if (h.Contains("MAU GIAO")) cot.Mg[ma] = c;
        }
        return cot;
    }

    private static int Cot(Dictionary<string, int> d, string ma) => d.TryGetValue(ma, out var c) ? c : 0;

    private static DateOnly? TimNgay(IXLWorksheet ws)
    {
        for (var r = 1; r <= 8; r++)
            for (var c = 1; c <= 4; c++)
            {
                var m = NgayRx.Match(Nd(ws.Cell(r, c).GetString()));
                if (!m.Success) continue;
                try { return new DateOnly(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        return null;
    }

    private static bool LaDongMauGiao(IXLWorksheet ws, int r, int cotCuoi)
    {
        for (var c = 1; c <= Math.Min(cotCuoi, 13); c++)
            if (Nd(ws.Cell(r, c).GetString()) == "MAU GIAO") return true;
        return false;
    }

    private static bool LaStt(IXLCell cell)
    {
        if (cell.IsEmpty()) return false;
        if (cell.TryGetValue<double>(out _)) return true;
        return SoRx.IsMatch(cell.GetString().Trim());
    }

    private static decimal So(IXLWorksheet ws, int r, int c)
    {
        if (c <= 0) return 0m;
        var cell = ws.Cell(r, c);
        if (cell.IsEmpty()) return 0m;
        double d;
        if (!cell.TryGetValue<double>(out d))
        {
            var s = cell.GetString().Trim().Replace(" ", "");
            if (s.Contains(',') && !s.Contains('.')) s = s.Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return 0m;
        }
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0m;
        return Math.Round((decimal)d, 4);
    }

    /// <summary>Bỏ dấu + IN HOA + gộp khoảng trắng/xuống dòng — để so khớp chữ tiêu đề không phụ thuộc cách gõ.</summary>
    private static string Nd(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString().ToUpperInvariant(), @"\s+", " ").Trim();
    }
}
