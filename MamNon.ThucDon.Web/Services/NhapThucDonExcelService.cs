using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Services;

/// <summary>Một ô món đọc từ file Excel thực đơn tháng: (tuần, thứ, khối, bữa) = tên món.</summary>
public sealed class NhapO
{
    public int Ngay { get; set; }            // 0 = Thứ 2 ... 5 = Thứ 7
    public int KhoiId { get; set; }
    public int LoaiBuaAnId { get; set; }
    public int? NhomId { get; set; }
    public string Ten { get; set; } = "";
}

public sealed class NhapPhu
{
    public int Ngay { get; set; }
    public string Sang { get; set; } = "";
    public string Chieu { get; set; } = "";
}

/// <summary>Ngày nghỉ cả ngày ghi trong file (VD ô gộp "Nghỉ Lễ 2-9" phủ cả Thứ 2 → Thứ 4).</summary>
public sealed class NhapNghi
{
    public int Ngay { get; set; }            // 0 = Thứ 2 ... 5 = Thứ 7
    public string GhiChu { get; set; } = "";
}

/// <summary>Ngày CÓ món trong file nhưng hệ thống đang đặt là ngày nghỉ -> mặc định KHÔNG ghi (trước đây im lặng, nên trông như "đọc thiếu").</summary>
public sealed class NhapXungDot
{
    public int Ngay { get; set; }            // 0 = Thứ 2 ... 5 = Thứ 7
    public int SoO { get; set; }             // số ô món trong file của ngày đó
    public string TrangThai { get; set; } = "";   // "Nghỉ lễ" / "Nghỉ khác"
    public string GhiChu { get; set; } = "";      // ghi chú ngày nghỉ trong hệ thống
}

public sealed class NhapTuan
{
    public List<NhapXungDot> XungDot { get; set; } = new();
    public string GhiChuThu7 { get; set; } = "";   // câu ghi chú "Trẻ nghỉ học Thứ bảy …" ở ngoài bảng (nếu file có)
    public List<NhapNghi> NgayNghi { get; set; } = new();
    public List<NhapPhu> Phu { get; set; } = new();
    public string Sheet { get; set; } = "";
    public DateOnly BatDau { get; set; }
    public bool ThuBayNghi { get; set; }
    public List<NhapO> O { get; set; } = new();
    public List<string> BoQua { get; set; } = new();
}

public sealed class NhapMonChuaCo
{
    public string Key { get; set; } = "";
    public string Ten { get; set; } = "";
    public int? NhomId { get; set; }
    public string TenNhom { get; set; } = "";
    public int SoLan { get; set; }
    public List<(int Id, string Ten)> GoiY { get; set; } = new();
}

public sealed class NhapKetQuaDoc
{
    public List<NhapTuan> Tuan { get; set; } = new();
    public List<NhapMonChuaCo> ChuaCo { get; set; } = new();
    public int SoOKhop { get; set; }
    public List<string> Loi { get; set; } = new();
}

public sealed class NhapThucDonExcelService
{
    private readonly ThucDonDbContext _db;
    public NhapThucDonExcelService(ThucDonDbContext db) { _db = db; }

    // ---------- chuẩn hóa tên ----------
    public static string BoDau(string s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }

    /// <summary>Khóa so khớp: bỏ phần trong ngoặc "(xế)/(sáng)", bỏ dấu, chữ thường, bỏ ký tự lạ.</summary>
    public static string Khoa(string ten)
    {
        var s = Regex.Replace(ten ?? "", @"\(.*?\)", " ");
        s = BoDau(s).ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", " ").Trim();
        return s;
    }

    private static string GonTen(string s) =>
        Regex.Replace((s ?? "").Replace(' ', ' '), @"\s+", " ").Trim().TrimEnd('.', ',', ';');

    // Lỗi chính tả hay gặp trong file Excel -> tên đúng (khóa = tên bỏ dấu, viết thường). Gặp lỗi lặp lại thì thêm 1 dòng vào đây.
    private static readonly Dictionary<string, string> SuaChinhTaTheoKhoa = new()
    {
        ["yakul"] = "Yakult",
        ["mi quang"] = "Mỳ quảng",   // "Mì quãng" -> cùng tên món "Mỳ quảng" đang dùng ở các tuần khác
    };

    private static string SuaChinhTa(string s)
    {
        var k = Khoa(s);
        return SuaChinhTaTheoKhoa.TryGetValue(k, out var dung) ? dung : s;
    }

    private static readonly string[] TienToKhongPhaiMon =
        { "tre nghi", "nghi ", "nghi le", "tuu truong", "khai giang", "trang tri", "tre khong an", "nha truong", "hop ",
          "nguoi thuc hien", "hieu truong", "cam le," };   // chân trang: "Cẩm Lệ, ngày … tháng … năm …", "Người thực hiện", "HIỆU TRƯỞNG"

    private static bool LaGhiChuKhongPhaiMon(string ten)
    {
        var k = BoDau(ten).ToLowerInvariant().Trim() + " ";
        return TienToKhongPhaiMon.Any(t => k.StartsWith(t));
    }

    private static string GiaTri(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        if (cell.IsMerged()) cell = cell.MergedRange().FirstCell();
        return GonTen(cell.GetString());
    }

    // ---------- đọc file ----------
    public async Task<NhapKetQuaDoc> DocAsync(Stream stream)
    {
        var kq = new NhapKetQuaDoc();
        var dsBua = await _db.LoaiBuaAns.Include(b => b.Khoi).AsNoTracking().ToListAsync();
        int? Bua(string khoi, string ten) => dsBua.FirstOrDefault(b =>
            BoDau(b.Khoi.TenKhoi).ToLowerInvariant().Contains(khoi) && BoDau(b.TenBua).ToLowerInvariant() == ten)?.Id;

        XLWorkbook wb;
        try { wb = new XLWorkbook(stream); }
        catch (Exception ex) { kq.Loi.Add("Không đọc được file Excel: " + ex.Message); return kq; }

        using (wb)
        {
            foreach (var ws in wb.Worksheets)
            {
                try
                {
                    var tuan = DocSheet(ws, dsBua);
                    if (tuan is null) { kq.Loi.Add($"Sheet \"{ws.Name}\": không nhận ra là bảng thực đơn tuần (thiếu dòng \"Từ ngày … đến …\" hoặc hàng \"Thứ Hai…\")."); continue; }
                    kq.Tuan.Add(tuan);
                }
                catch (Exception ex) { kq.Loi.Add($"Sheet \"{ws.Name}\": lỗi đọc — {ex.Message}"); }
            }
        }
        kq.Tuan = kq.Tuan.OrderBy(t => t.BatDau).ToList();
        await DanhDauXungDotAsync(kq);
        await DoiChieuAsync(kq);
        return kq;
    }

    private NhapTuan? DocSheet(IXLWorksheet ws, List<LoaiBuaAn> dsBua)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, 14);
        if (lastRow == 0) return null;

        // Ngày bắt đầu: "Từ ngày 14/9/2026 đến …"
        DateOnly? batDau = null;
        for (var r = 1; r <= Math.Min(lastRow, 15) && batDau is null; r++)
        {
            var t = GiaTri(ws, r, 1);
            if (!BoDau(t).ToLowerInvariant().Contains("tu ngay")) continue;
            var m = Regex.Match(t, @"(\d{1,2})/(\d{1,2})/(\d{4})");
            if (m.Success && DateOnly.TryParse($"{m.Groups[3].Value}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[1].Value):00}", out var d)) batDau = d;
        }
        if (batDau is null) return null;
        var bd = batDau.Value;
        if (bd.DayOfWeek != DayOfWeek.Monday) bd = bd.AddDays(-(((int)bd.DayOfWeek + 6) % 7));

        // Hàng tiêu đề "Thứ Hai … Thứ Bảy"
        var hRow = 0;
        for (var r = 1; r <= Math.Min(lastRow, 20) && hRow == 0; r++)
            for (var c = 2; c <= lastCol; c++)
                if (BoDau(GiaTri(ws, r, c)).ToLowerInvariant().StartsWith("thu hai")) { hRow = r; break; }
        if (hRow == 0) return null;

        // cột bắt đầu của từng ngày + cột khối
        var cotKhoi = new Dictionary<(int ngay, string khoi), int>();
        var tenThu = new[] { "thu hai", "thu ba", "thu tu", "thu nam", "thu sau", "thu bay" };
        for (var c = 2; c <= lastCol; c++)
        {
            var h = BoDau(ws.Cell(hRow, c).GetString()).ToLowerInvariant().Trim();
            // So khớp NGUYÊN TỪ: "thu bay".StartsWith("thu ba") là true -> Thứ Bảy từng bị đọc nhầm thành Thứ Ba
            // (ghi đè cột Thứ Ba, Thứ Bảy thì mất). Phải khớp đúng cả cụm, không khớp tiền tố.
            var idx = Array.FindIndex(tenThu, x => h == x || h.StartsWith(x + " "));
            if (idx < 0) continue;
            for (var cc = c; cc <= c + 1 && cc <= lastCol; cc++)
            {
                var sub = BoDau(GiaTri(ws, hRow + 1, cc)).ToLowerInvariant();
                if (sub.Contains("nha tre")) cotKhoi[(idx, "nha tre")] = cc;
                else if (sub.Contains("mau giao")) cotKhoi[(idx, "mau giao")] = cc;
            }
        }
        if (cotKhoi.Count == 0) return null;

        // Nhãn bữa ở cột A
        var nhan = new List<(int row, string ma)>();
        for (var r = hRow + 2; r <= lastRow; r++)
        {
            var a = BoDau(GiaTri(ws, r, 1)).ToLowerInvariant();
            if (a.Contains("nguoi thuc hien") || a.Contains("hieu truong")) break;
            string? ma = null;
            if (a.Contains("buoi sang")) ma = "sang";
            else if (a.Contains("phu sang")) ma = "phusang";
            else if (a.Contains("buoi trua")) ma = "trua";
            else if (a.Contains("chinh chieu")) ma = "chieu";
            else if (a.Contains("phu chieu")) ma = "phuchieu";
            if (ma is not null) nhan.Add((r, ma));
        }
        if (nhan.Count == 0) return null;

        // Chân trang ("Cẩm Lệ, ngày … tháng … năm …", "Người thực hiện", "HIỆU TRƯỞNG", họ tên người ký) KHÔNG thuộc bảng món:
        // nếu không cắt ở đây, bữa cuối (Phụ chiều) sẽ đọc luôn các dòng này thành "món".
        var cuoiBang = lastRow;
        for (var r = hRow + 2; r <= lastRow; r++)
        {
            var hang = new StringBuilder();
            for (var c = 1; c <= lastCol; c++) hang.Append(' ').Append(BoDau(ws.Cell(r, c).GetString()).ToLowerInvariant());
            var t = hang.ToString();
            if (t.Contains("nguoi thuc hien") || t.Contains("hieu truong") || Regex.IsMatch(t, @"ngay\s+\d+\s+thang\s+\d+\s+nam")) { cuoiBang = r - 1; break; }
        }

        var tuan = new NhapTuan { Sheet = ws.Name, BatDau = bd };
        void BoQua(string msg) { if (!tuan.BoQua.Contains(msg)) tuan.BoQua.Add(msg); }
        var tenThuHienThi = new[] { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };

        // "Trẻ nghỉ học Thứ bảy" ở bất kỳ ô nào ngoài bảng chính
        for (var r = hRow; r <= Math.Min(lastRow, hRow + 4); r++)
            for (var c = 14; c <= lastCol + 2; c++)
            {
                var t = BoDau(ws.Cell(r, c).GetString()).ToLowerInvariant();
                if (t.Contains("nghi hoc thu bay")) { tuan.ThuBayNghi = true; tuan.GhiChuThu7 = GonTen(ws.Cell(r, c).GetString()); }
            }

        for (var i = 0; i < nhan.Count; i++)
        {
            var (row0, ma) = nhan[i];
            var row1 = i + 1 < nhan.Count ? nhan[i + 1].row - 1 : cuoiBang;
            if (ma == "phusang") continue; // bánh/sữa chua/yakult phụ sáng do hệ thống tự tính, không đưa vào lưới

            for (var ngay = 0; ngay < 6; ngay++)
                foreach (var khoi in new[] { "nha tre", "mau giao" })
                {
                    if (!cotKhoi.TryGetValue((ngay, khoi), out var col)) continue;
                    for (var r = row0; r <= row1; r++)
                    {
                        var v = GiaTri(ws, r, col);
                        if (v.Length == 0) continue;

                        // Ô ghi "Nghỉ Lễ 2-9"... ở dòng Buổi sáng (thường gộp cả cột/nhiều ngày) = NGÀY NGHỈ, không phải món.
                        if (tuan.NgayNghi.Any(n => n.Ngay == ngay)) continue;
                        if (ma == "sang" && BoDau(v).ToLowerInvariant().StartsWith("nghi ") && !BoDau(v).ToLowerInvariant().StartsWith("nghi hoc thu bay"))
                        {
                            tuan.NgayNghi.Add(new NhapNghi { Ngay = ngay, GhiChu = v });
                            continue;
                        }

                        string? tenBua; var ten = v;
                        if (ma == "trua")
                        {
                            if (LaGhiChuKhongPhaiMon(v)) { BoQua($"{tenThuHienThi[ngay]}: \"{v}\" (không phải món ăn — bỏ qua)"); continue; }
                            var m = Regex.Match(v, @"^\+?\s*(mặn|canh|xào|tráng miệng|cải thiện)\s*[:\s]+(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                            if (!m.Success) { BoQua($"{tenThuHienThi[ngay]} ({(khoi == "nha tre" ? "Nhà trẻ" : "Mẫu giáo")}): \"{v}\" — không nhận ra là món"); continue; }
                            var nhom = BoDau(m.Groups[1].Value).ToLowerInvariant();
                            ten = GonTen(Regex.Replace(m.Groups[2].Value, @"^[:\s]+", ""));
                            tenBua = nhom switch { "man" => "man", "canh" => "canh", "xao" => "xao", "trang mieng" => "trang mieng", "cai thien" => "man", _ => null };
                        }
                        else if (ma == "sang") tenBua = "sang";
                        else if (ma == "chieu") tenBua = khoi == "mau giao" ? "xe" : "chinh chieu";
                        else /* phuchieu */ { if (khoi != "nha tre") continue; tenBua = "xe"; }

                        if (ten.Length == 0) continue;
                        if (LaGhiChuKhongPhaiMon(ten)) { BoQua($"{tenThuHienThi[ngay]}: \"{ten}\" (không phải món ăn — bỏ qua)"); continue; }

                        var bua = dsBua.FirstOrDefault(b => BoDau(b.Khoi.TenKhoi).ToLowerInvariant().Contains(khoi) && BoDau(b.TenBua).ToLowerInvariant() == tenBua);
                        if (bua is null) { BoQua($"{tenThuHienThi[ngay]} ({(khoi == "nha tre" ? "Nhà trẻ" : "Mẫu giáo")}): \"{ten}\" — khối này không có bữa \"{tenBua}\""); continue; }

                        // ô gộp 2 khối sẽ sinh 2 dòng (mỗi khối 1 dòng) — đúng ý
                        tuan.O.Add(new NhapO { Ngay = ngay, KhoiId = bua.KhoiId, LoaiBuaAnId = bua.Id, NhomId = bua.NhomBuaAnId, Ten = SuaChinhTa(ten) });
                    }
                }
        }
        // Phụ sáng / Phụ chiều (dùng chung 2 khối) -> lưu riêng cho trang công khai
        int? HangCua(string ma) { foreach (var n in nhan) if (n.ma == ma) return n.row; return null; }
        var hSang = HangCua("phusang"); var hChieu = HangCua("phuchieu");
        for (var ngay = 0; ngay < 6; ngay++)
        {
            string Lay(int? hang)
            {
                if (hang is null) return "";
                foreach (var khoi in new[] { "nha tre", "mau giao" })
                    if (cotKhoi.TryGetValue((ngay, khoi), out var col))
                    {
                        var v = GiaTri(ws, hang.Value, col);
                        if (v.Length > 0 && !LaGhiChuKhongPhaiMon(v)) return v;
                    }
                return "";
            }
            var ps = Lay(hSang); var pc = Lay(hChieu);
            if (ps.Length > 0 || pc.Length > 0) tuan.Phu.Add(new NhapPhu { Ngay = ngay, Sang = SuaChinhTa(ps), Chieu = SuaChinhTa(pc) });
        }

        if (tuan.NgayNghi.Any(n => n.Ngay == 5)) tuan.ThuBayNghi = true;
        // Chỉ coi Thứ 7 là nghỉ khi có ghi chú "nghỉ học Thứ bảy" VÀ cột Thứ 7 không có món nào.
        if (tuan.O.Any(o => o.Ngay == 5)) tuan.ThuBayNghi = false;
        else if (cotKhoi.TryGetValue((5, "nha tre"), out var cotT7))
        {
            // Cột Thứ 7 không có món nhưng có ô ghi chú kiểu "Trẻ nghỉ học - Họp Hội đồng sư phạm" => Thứ 7 nghỉ.
            for (var r = hRow + 2; r <= cuoiBang; r++)
                if (BoDau(GiaTri(ws, r, cotT7)).ToLowerInvariant().StartsWith("tre nghi")) { tuan.ThuBayNghi = true; break; }
        }
        return tuan;
    }

    // ---------- đối chiếu với danh sách món ----------
    public async Task<Dictionary<string, int>> DungKhoaAsync()
    {
        var ds = await _db.MonAns.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Id).ToListAsync();
        var map = new Dictionary<string, int>();
        foreach (var m in ds) map.TryAdd(KhoaNhom(m.NhomBuaAnId, m.TenMon), m.Id);
        return map;
    }

    public static string KhoaNhom(int? nhomId, string ten) => $"{nhomId}|{Khoa(ten)}";

    private static HashSet<string> TuKhoa(string ten) => Khoa(ten).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private async Task DanhDauXungDotAsync(NhapKetQuaDoc kq)
    {
        if (kq.Tuan.Count == 0) return;
        var tu = kq.Tuan.Min(t => t.BatDau); var den = kq.Tuan.Max(t => t.BatDau).AddDays(6);
        var nghi = await _db.NgayHocs.AsNoTracking()
            .Where(n => n.Ngay >= tu && n.Ngay <= den && n.TrangThai != TrangThaiNgay.HocBinhThuong).ToListAsync();
        foreach (var t in kq.Tuan)
            foreach (var g in t.O.GroupBy(o => o.Ngay))
            {
                var ngay = t.BatDau.AddDays(g.Key);
                var nh = nghi.Where(n => n.Ngay == ngay).ToList();
                if (nh.Count == 0) continue;
                t.XungDot.Add(new NhapXungDot
                {
                    Ngay = g.Key, SoO = g.Count(),
                    TrangThai = nh.Any(n => n.TrangThai == TrangThaiNgay.NghiLe) ? "Nghỉ lễ" : "Nghỉ khác",
                    GhiChu = nh.Select(n => n.GhiChu).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? ""
                });
            }
    }

    private async Task DoiChieuAsync(NhapKetQuaDoc kq)
    {
        var mons = await _db.MonAns.AsNoTracking().Where(m => m.IsActive).ToListAsync();
        var nhoms = await _db.NhomBuaAns.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.TenNhom);
        var daCo = await DungKhoaAsync();

        var chuaCo = new Dictionary<string, NhapMonChuaCo>();
        foreach (var o in kq.Tuan.SelectMany(t => t.O))
        {
            var key = KhoaNhom(o.NhomId, o.Ten);
            if (daCo.ContainsKey(key)) { kq.SoOKhop++; continue; }
            if (!chuaCo.TryGetValue(key, out var x))
            {
                var tk = TuKhoa(o.Ten);
                var goiY = mons.Where(m => m.NhomBuaAnId == o.NhomId)
                    .Select(m => (m, diem: TuKhoa(m.TenMon) is var tm && tm.Count > 0 ? (double)tm.Intersect(tk).Count() / tm.Union(tk).Count() : 0))
                    .Where(p => p.diem >= 0.34).OrderByDescending(p => p.diem).Take(5)
                    .Select(p => (p.m.Id, p.m.TenMon)).ToList();
                x = new NhapMonChuaCo
                {
                    Key = key, Ten = o.Ten, NhomId = o.NhomId,
                    TenNhom = o.NhomId is int g && nhoms.TryGetValue(g, out var tn) ? tn : "",
                    GoiY = goiY
                };
                chuaCo[key] = x;
            }
            x.SoLan++;
        }
        kq.ChuaCo = chuaCo.Values.OrderBy(x => x.TenNhom).ThenBy(x => x.Ten).ToList();
    }
}
