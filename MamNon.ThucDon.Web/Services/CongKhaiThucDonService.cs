using System.Globalization;
using System.Text.Json;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Services;

// ----- Dữ liệu trả cho trang phụ huynh: tên trường, tuần, ngày, nhóm tuổi, tên bữa, tên món và (tab "Chi tiết") bản chụp nguyên liệu + đơn giá + thành tiền từng ngày. -----
public sealed record PhuHuynhMon(string Loai, string Ten);
public sealed record PhuHuynhBua(string Ma, string Ten, List<PhuHuynhMon> Mon);
public sealed record PhuHuynhNgay(string Thu, string Ngay, string NgayNgan, bool Nghi, List<PhuHuynhBua> NhaTre, List<PhuHuynhBua> MauGiao,
    JsonElement? ChiTiet = null);
public sealed record PhuHuynhTuan(
    bool CoDuLieu, string Truong, string TieuDe, string TuNgay, string DenNgay, string BatDau,
    string? TuanTruoc, string? TuanSau, string? CapNhat, List<PhuHuynhNgay> Ngay, bool HienChiTiet = true);

/// <summary>Màn quản trị "Công khai thực đơn": tình trạng tuần đang chọn.</summary>
public sealed class CongKhaiIndexVm
{
    public DateOnly Tuan { get; set; }
    public string TieuDe { get; set; } = "";
    public int SoMonNhaTre { get; set; }
    public int SoMonMauGiao { get; set; }
    public string TrangThaiNoiBo { get; set; } = "";
    public bool DaDuyetNoiBo { get; set; }
    public CongKhaiTuan? DaCongKhai { get; set; }
    public List<CongKhaiTuan> LichSu { get; set; } = new();
    public List<(DateOnly Tuan, string TieuDe)> TieuDeLichSu { get; set; } = new();
    /// <summary>Phụ sáng / Phụ chiều từng ngày (Thứ 2..Thứ 7) — nhập tay hoặc lấy từ file Excel; dùng chung cho 2 khối.</summary>
    public List<(string Sang, string Chieu)> Phu { get; set; } = new();
    /// <summary>File Excel chi tiết từng ngày (Thứ 2..Thứ 7) — BẮT BUỘC có thì mới công khai được ngày đó.</summary>
    public List<FileNgayVm> FileNgay { get; set; } = new();
    public bool BatBuocFile { get; set; }
}

public sealed class FileNgayVm
{
    public int Ngay { get; set; }
    public DateOnly Date { get; set; }
    public bool Nghi { get; set; }
    public bool CoMon { get; set; }
    public string? TenFile { get; set; }
    public string? TaiLuc { get; set; }
    public string? NguoiTai { get; set; }
    public bool CoFile => !string.IsNullOrEmpty(TenFile);
    /// <summary>Ngày có món (không nghỉ) thì phải có file chi tiết mới công khai được.</summary>
    public bool CanFile => CoMon && !Nghi;
}

public sealed class FileNgayRow
{
    public string Ngay { get; set; } = "";
    public string TenFile { get; set; } = "";
    public string TaiLuc { get; set; } = "";
    public string? NguoiTai { get; set; }
    public string DuLieu { get; set; } = "";
}

public sealed class PhuRow
{
    public int Ngay { get; set; }
    public string? PhuSang { get; set; }
    public string? PhuChieu { get; set; }
}

/// <summary>Dựng thực đơn cho phụ huynh từ thực đơn nội bộ, lưu BẢN CHỤP khi nhân viên bấm "Công khai", và đọc bản chụp cho trang công khai.</summary>
public sealed class CongKhaiThucDonService
{
    private readonly ThucDonDbContext _db;
    private readonly CongKhaiDbContext _ck;
    private readonly IConfiguration _cfg;

    public CongKhaiThucDonService(ThucDonDbContext db, CongKhaiDbContext ck, IConfiguration cfg)
    {
        _db = db; _ck = ck; _cfg = cfg;
    }

    /// <summary>Bật/tắt tab "Chi tiết" (nguyên liệu + đơn giá + thành tiền) trên trang phụ huynh. Mặc định BẬT; đặt CongKhai:HienChiTiet = false để tắt.</summary>
    public bool HienChiTiet => !string.Equals(_cfg["CongKhai:HienChiTiet"], "false", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ file Excel chi tiết từng ngày (BẮT BUỘC để công khai)
    private async Task<Dictionary<DateOnly, FileNgayRow>> LayFileTuanAsync(DateOnly thuHai)
    {
        var tu = thuHai.ToString("yyyy-MM-dd");
        var den = thuHai.AddDays(5).ToString("yyyy-MM-dd");
        var rows = await _ck.Database.SqlQuery<FileNgayRow>(
            $"SELECT ngay AS Ngay, ten_file AS TenFile, tai_luc AS TaiLuc, nguoi_tai AS NguoiTai, du_lieu AS DuLieu FROM cong_khai_file_ngay WHERE ngay >= {tu} AND ngay <= {den}").ToListAsync();
        var kq = new Dictionary<DateOnly, FileNgayRow>();
        foreach (var r in rows)
            if (DateOnly.TryParseExact(r.Ngay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) kq[d] = r;
        return kq;
    }

    private static string HienGio(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("HH:mm dd/MM/yyyy") : "";

    /// <summary>Nhận file Excel chi tiết của 1 ngày (0 = Thứ 2 ... 5 = Thứ 7): đọc, kiểm tra đúng ngày, rồi lưu (thay file cũ của ngày đó nếu có).</summary>
    public async Task<(bool Ok, string Msg)> LuuFileNgayAsync(DateOnly tuan, int ngayIdx, Stream stream, string tenFile, string? nguoi)
    {
        tuan = ThuHai(tuan);
        if (ngayIdx < 0 || ngayIdx > 5) return (false, "Ngày không hợp lệ.");
        var ngay = tuan.AddDays(ngayIdx);
        var kq = ChiTietNgayExcelService.Doc(stream);
        if (!kq.Ok || kq.Json is null) return (false, kq.Loi ?? "Không đọc được file.");
        if (kq.Ngay != ngay)
            return (false, $"File này là của ngày {kq.Ngay:dd/MM/yyyy}, không phải {TenThu[ngayIdx]} {ngay:dd/MM/yyyy}. Hãy chọn đúng file của ngày này.");

        var key = ngay.ToString("yyyy-MM-dd");
        var luc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var ten = tenFile.Length > 200 ? tenFile[^200..] : tenFile;
        var nguoiLuu = string.IsNullOrWhiteSpace(nguoi) ? "" : nguoi.Trim();
        var json = kq.Json;
        await _ck.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR REPLACE INTO cong_khai_file_ngay (ngay, ten_file, tai_luc, nguoi_tai, du_lieu) VALUES ({key}, {ten}, {luc}, {nguoiLuu}, {json})");
        return (true, $"Đã nhận file chi tiết {TenThu[ngayIdx]} {ngay:dd/MM} ({kq.SoDongSang} dòng ăn sáng, {kq.SoDongTrua} dòng ăn trưa). Nhớ bấm \"Cập nhật công khai\" để phụ huynh thấy.");
    }

    public async Task XoaFileNgayAsync(DateOnly tuan, int ngayIdx)
    {
        tuan = ThuHai(tuan);
        if (ngayIdx < 0 || ngayIdx > 5) return;
        var key = tuan.AddDays(ngayIdx).ToString("yyyy-MM-dd");
        await _ck.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cong_khai_file_ngay WHERE ngay = {key}");
    }

    /// <summary>Phụ chiều mặc định khi ngày đó CHƯA lưu gì ở mục "Phụ sáng & Phụ chiều" (file Excel của trường: Sữa Ellac Family mỗi ngày). Đặt CongKhai:PhuChieuMacDinh = "" để tắt.</summary>
    public string PhuChieuMacDinh => _cfg["CongKhai:PhuChieuMacDinh"] ?? "Sữa Ellac Family";

    public string TenTruong => _cfg["CongKhai:TenTruong"] is { Length: > 0 } t ? t : "TRƯỜNG MẦM NON HƯỚNG DƯƠNG";

    public static DateOnly ThuHai(DateOnly ngay) => LichSuThucDonViewModel.ThuHaiCuaTuan(ngay);

    public static string TieuDeTuan(DateOnly thuHai)
    {
        var (nam, th) = LichSuThucDonViewModel.ThangCuaTuan(thuHai);
        var idx = Math.Max(0, LichSuThucDonViewModel.CacTuanCuaThang(nam, th).IndexOf(thuHai)) + 1;
        return $"Tuần {idx} · Tháng {th}/{nam}";
    }

    // ------------------------------------------------------------------ thực đơn nội bộ -> các dòng (ngày, khối, bữa, món)
    private sealed record Dong(DateOnly Ngay, string Khoi, int ThuTu, string Bua, string Mon);

    private async Task<(List<Dong> Dong, HashSet<DateOnly> Nghi, List<TrangThaiThucDon> TrangThai)> DocNoiBoAsync(DateOnly thuHai)
    {
        var coSo = await _db.CoSos.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.TenCoSo).FirstOrDefaultAsync();
        if (coSo is null) return (new(), new(), new());
        var den = thuHai.AddDays(5);

        var nghi = (await _db.NgayHocs.AsNoTracking()
                .Where(n => n.CoSoId == coSo.Id && n.Ngay >= thuHai && n.Ngay <= den && n.TrangThai != TrangThaiNgay.HocBinhThuong)
                .Select(n => n.Ngay).ToListAsync()).ToHashSet();

        var raw = await _db.ThucDons.AsNoTracking()
            .Where(t => t.CoSoId == coSo.Id && t.MonAnId != null && t.NgayHoc.Ngay >= thuHai && t.NgayHoc.Ngay <= den)
            .Select(t => new { Ngay = t.NgayHoc.Ngay, Khoi = t.Khoi.TenKhoi, ThuTu = (int)t.LoaiBuaAn.ThuTu, Bua = t.LoaiBuaAn.TenBua, Mon = t.MonAn!.TenMon, t.TrangThai })
            .ToListAsync();

        var dong = raw.Where(r => !nghi.Contains(r.Ngay))
            .Select(r => new Dong(r.Ngay, r.Khoi, r.ThuTu, r.Bua, ChiTietNgayTinhToanService.TenMonHienThi(r.Mon).Trim()))
            .Where(r => r.Mon.Length > 0).ToList();
        dong = GhepPhu(thuHai, dong, nghi, await LayPhuAsync(thuHai), PhuChieuMacDinh);
        return (dong, nghi, raw.Select(r => r.TrangThai).ToList());
    }

    // ------------------------------------------------------------------ Phụ sáng / Phụ chiều (bảng riêng cong_khai_phu)
    public async Task<Dictionary<int, (string Sang, string Chieu)>> LayPhuAsync(DateOnly thuHai)
    {
        var key = thuHai.ToString("yyyy-MM-dd");
        var rows = await _ck.Database.SqlQuery<PhuRow>(
            $"SELECT ngay AS Ngay, phu_sang AS PhuSang, phu_chieu AS PhuChieu FROM cong_khai_phu WHERE tuan_bat_dau = {key}").ToListAsync();
        return rows.ToDictionary(r => r.Ngay, r => ((r.PhuSang ?? "").Trim(), (r.PhuChieu ?? "").Trim()));
    }

    public async Task LuuPhuAsync(DateOnly tuan, IList<string?> sang, IList<string?> chieu)
    {
        tuan = ThuHai(tuan);
        var key = tuan.ToString("yyyy-MM-dd");
        for (var i = 0; i < 6; i++)
        {
            var sa = (i < sang.Count ? sang[i] : null)?.Trim() ?? "";
            var ch = (i < chieu.Count ? chieu[i] : null)?.Trim() ?? "";
            await _ck.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ({key}, {i}, {sa}, {ch})");
        }
    }

    /// <summary>Lưu Phụ sáng / Phụ chiều CHỈ cho các ngày được truyền (0 = Thứ 2 ... 5 = Thứ 7); ngày khác giữ nguyên. Dùng ở Trang chủ.</summary>
    public async Task LuuPhuTheoNgayAsync(DateOnly tuan, IReadOnlyDictionary<int, (string Sang, string Chieu)> theoNgay)
    {
        tuan = ThuHai(tuan);
        var key = tuan.ToString("yyyy-MM-dd");
        foreach (var (i, p) in theoNgay)
        {
            if (i < 0 || i > 5) continue;
            var sa = (p.Sang ?? "").Trim();
            var ch = (p.Chieu ?? "").Trim();
            await _ck.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ({key}, {i}, {sa}, {ch})");
        }
    }

    /// <summary>Thêm bữa PHỤ SÁNG / PHỤ CHIỀU (cả 2 khối) vào các dòng của tuần. Ngày đã nhập phụ chiều thì thay "Xế" sữa của Nhà trẻ.</summary>
    private static List<Dong> GhepPhu(DateOnly thuHai, List<Dong> dong, HashSet<DateOnly> nghi,
        Dictionary<int, (string Sang, string Chieu)> phu, string macDinhChieu)
    {
        var kq = dong.ToList();
        for (var i = 0; i < 6; i++)
        {
            var d = thuHai.AddDays(i);
            if (nghi.Contains(d)) continue;
            var daLuu = phu.TryGetValue(i, out var p);
            if (!daLuu) p = ("", (macDinhChieu ?? "").Trim());
            foreach (var khoi in new[] { "Nhà trẻ", "Mẫu giáo" })
            {
                if (p.Sang.Length > 0) kq.Add(new Dong(d, khoi, 0, "PHỤ SÁNG", p.Sang));
                if (p.Chieu.Length == 0) continue;
                var coXeNhaTre = kq.Any(x => x.Ngay == d && x.Khoi == khoi && khoi == "Nhà trẻ" && x.Bua.Trim().Equals("XẾ", StringComparison.OrdinalIgnoreCase));
                if (!daLuu && coXeNhaTre) continue;                 // mặc định không đè món Xế đã soạn của Nhà trẻ
                kq.RemoveAll(x => x.Ngay == d && x.Khoi == khoi && khoi == "Nhà trẻ" && x.Bua.Trim().Equals("XẾ", StringComparison.OrdinalIgnoreCase));
                kq.Add(new Dong(d, khoi, 99, "PHỤ CHIỀU", p.Chieu));
            }
        }
        return kq;
    }

    // ------------------------------------------------------------------ các dòng -> dữ liệu hiển thị
    private static (string Ma, string Ten, int ThuTu, string Loai)? NhomHienThi(string khoi, string tenBua)
    {
        var b = tenBua.Trim().ToUpperInvariant();
        var nhaTre = khoi.Trim().Equals("Nhà trẻ", StringComparison.OrdinalIgnoreCase);
        return b switch
        {
            "SÁNG" => ("sang", "BUỔI SÁNG", 1, ""),
            "PHỤ SÁNG" => ("phusang", "PHỤ SÁNG", 2, ""),
            "CANH" => ("trua", "BUỔI TRƯA", 3, "Canh"),
            "MẶN" => ("trua", "BUỔI TRƯA", 3, "Mặn"),
            "XÀO" => ("trua", "BUỔI TRƯA", 3, "Xào"),
            "TRÁNG MIỆNG" => ("trua", "BUỔI TRƯA", 3, "Tráng miệng"),
            "CHÍNH CHIỀU" => ("chieu", "CHÍNH CHIỀU", 4, ""),
            "PHỤ CHIỀU" => ("phuchieu", "PHỤ CHIỀU", 5, ""),
            "XẾ" => nhaTre ? ("phuchieu", "PHỤ CHIỀU", 5, "") : ("chieu", "CHÍNH CHIỀU", 4, ""),   // Nhà trẻ: XẾ = sữa (phụ chiều); Mẫu giáo: XẾ = món chiều
            _ => null,
        };
    }

    private static readonly string[] TenThu = { "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy" };
    private static readonly string[] ThuNgan = { "T2", "T3", "T4", "T5", "T6", "T7" };

    private static List<PhuHuynhBua> GomBua(IEnumerable<Dong> dong, string khoi)
    {
        var kq = new List<PhuHuynhBua>();
        var theoNhom = dong.Where(d => d.Khoi.Equals(khoi, StringComparison.OrdinalIgnoreCase))
            .Select(d => (D: d, N: NhomHienThi(d.Khoi, d.Bua)))
            .Where(x => x.N is not null)
            .GroupBy(x => (x.N!.Value.Ma, x.N!.Value.Ten, x.N!.Value.ThuTu))
            .OrderBy(g => g.Key.ThuTu);
        foreach (var g in theoNhom)
        {
            var mon = g.OrderBy(x => x.D.ThuTu).Select(x => new PhuHuynhMon(x.N!.Value.Loai, x.D.Mon)).ToList();
            kq.Add(new PhuHuynhBua(g.Key.Ma, g.Key.Ten, mon));
        }
        return kq;
    }

    private PhuHuynhTuan Dung(DateOnly thuHai, List<Dong> dong, HashSet<DateOnly> nghi, DateTime? capNhatUtc, string? tuanTruoc, string? tuanSau,
        Dictionary<DateOnly, JsonElement>? chiTiet = null)
    {
        var ngay = new List<PhuHuynhNgay>();
        for (var i = 0; i < 6; i++)
        {
            var d = thuHai.AddDays(i);
            var cua = dong.Where(x => x.Ngay == d).ToList();
            var laNghi = nghi.Contains(d);
            JsonElement? ct = null;
            if (!laNghi && chiTiet is not null && chiTiet.TryGetValue(d, out var ctNgay)) ct = ctNgay;
            ngay.Add(new PhuHuynhNgay(TenThu[i], d.ToString("yyyy-MM-dd"), d.ToString("dd/MM"), laNghi,
                laNghi ? new() : GomBua(cua, "Nhà trẻ"), laNghi ? new() : GomBua(cua, "Mẫu giáo"), ct));
        }
        var capNhat = capNhatUtc is null ? null : capNhatUtc.Value.ToLocalTime().ToString("HH:mm dd/MM/yyyy");
        return new PhuHuynhTuan(true, TenTruong, TieuDeTuan(thuHai), thuHai.ToString("dd/MM/yyyy"), thuHai.AddDays(5).ToString("dd/MM/yyyy"),
            thuHai.ToString("yyyy-MM-dd"), tuanTruoc, tuanSau, capNhat, ngay, HienChiTiet);
    }

    public PhuHuynhTuan KhongCoDuLieu() =>
        new(false, TenTruong, "", "", "", "", null, null, null, new());

    // ------------------------------------------------------------------ quản trị
    public async Task<CongKhaiIndexVm> LayIndexAsync(DateOnly tuan)
    {
        tuan = ThuHai(tuan);
        var (dong, nghi, trangThai) = await DocNoiBoAsync(tuan);
        var vm = new CongKhaiIndexVm
        {
            Tuan = tuan,
            TieuDe = TieuDeTuan(tuan),
            SoMonNhaTre = dong.Count(d => d.Khoi.Equals("Nhà trẻ", StringComparison.OrdinalIgnoreCase) && !d.Bua.StartsWith("PHỤ")),
            SoMonMauGiao = dong.Count(d => d.Khoi.Equals("Mẫu giáo", StringComparison.OrdinalIgnoreCase) && !d.Bua.StartsWith("PHỤ")),
        };
        var phu = await LayPhuAsync(tuan);
        for (var i = 0; i < 6; i++)
            vm.Phu.Add(phu.TryGetValue(i, out var p) ? p : ("", ""));
        var files = await LayFileTuanAsync(tuan);
        vm.BatBuocFile = HienChiTiet;
        for (var i = 0; i < 6; i++)
        {
            var d = tuan.AddDays(i);
            files.TryGetValue(d, out var f);
            vm.FileNgay.Add(new FileNgayVm
            {
                Ngay = i, Date = d, Nghi = nghi.Contains(d),
                CoMon = dong.Any(x => x.Ngay == d && !x.Bua.StartsWith("PHỤ")),
                TenFile = f?.TenFile, TaiLuc = f is null ? null : HienGio(f.TaiLuc), NguoiTai = f?.NguoiTai,
            });
        }
        if (trangThai.Count == 0) vm.TrangThaiNoiBo = "Chưa có thực đơn";
        else if (trangThai.All(t => t == TrangThaiThucDon.DaDuyet)) { vm.TrangThaiNoiBo = "Đã duyệt"; vm.DaDuyetNoiBo = true; }
        else if (trangThai.Any(t => t == TrangThaiThucDon.Nhap)) vm.TrangThaiNoiBo = "Nháp";
        else vm.TrangThaiNoiBo = "Chờ duyệt";
        vm.DaCongKhai = await _ck.CongKhaiTuans.AsNoTracking().FirstOrDefaultAsync(x => x.TuanBatDau == tuan);
        vm.LichSu = await _ck.CongKhaiTuans.AsNoTracking().OrderByDescending(x => x.TuanBatDau).Take(30).ToListAsync();
        vm.TieuDeLichSu = vm.LichSu.Select(x => (x.TuanBatDau, TieuDeTuan(x.TuanBatDau))).ToList();
        return vm;
    }

    /// <summary>Xem trước: dựng thẳng từ thực đơn nội bộ (chưa công khai).</summary>
    public async Task<PhuHuynhTuan> XemTruocAsync(DateOnly tuan)
    {
        tuan = ThuHai(tuan);
        var (dong, nghi, _) = await DocNoiBoAsync(tuan);
        // Xem trước: chi tiết nguyên liệu lấy từ FILE EXCEL đã tải lên cho từng ngày (ngày chưa có file thì để trống).
        Dictionary<DateOnly, JsonElement>? chiTiet = null;
        if (HienChiTiet)
        {
            chiTiet = new Dictionary<DateOnly, JsonElement>();
            foreach (var (n, f) in await LayFileTuanAsync(tuan))
            {
                if (nghi.Contains(n)) continue;
                try { chiTiet[n] = JsonSerializer.Deserialize<JsonElement>(f.DuLieu); } catch (JsonException) { }
            }
        }
        return Dung(tuan, dong, nghi, null, null, null, chiTiet);
    }

    /// <summary>Công khai / cập nhật công khai: chụp lại thực đơn nội bộ của tuần vào bảng công khai (thay bản cũ nếu có).</summary>
    /// <param name="cacNgay">null/rỗng = công khai CẢ TUẦN. Có giá trị (0 = Thứ 2 ... 5 = Thứ 7) = chỉ CÁC NGÀY ĐÓ hiện cho phụ huynh
    /// (bản chụp cũ của tuần được thay hẳn, các ngày không chọn — thường là các ngày sau — bị ẩn). Trang chủ gửi 0..k = "đến hết ngày k".</param>
    public async Task<(bool Ok, string Message)> CongKhaiAsync(DateOnly tuan, string? nguoi, IReadOnlyCollection<int>? cacNgay = null)
    {
        tuan = ThuHai(tuan);
        var (dong, nghi, _) = await DocNoiBoAsync(tuan);

        var theoNgay = cacNgay is { Count: > 0 } && cacNgay.Count < 6;
        var ngayChon = theoNgay
            ? cacNgay!.Where(i => i >= 0 && i <= 5).Select(i => tuan.AddDays(i)).ToHashSet()
            : Enumerable.Range(0, 6).Select(i => tuan.AddDays(i)).ToHashSet();

        var dongChon = dong.Where(d => ngayChon.Contains(d.Ngay)).ToList();
        if (!dongChon.Any(d => !d.Bua.StartsWith("PHỤ")) && !ngayChon.Any(nghi.Contains))
            return (false, theoNgay ? "Các ngày đã chọn chưa có món nào trong thực đơn nên chưa công khai được."
                                    : "Tuần này chưa có món nào trong thực đơn nên chưa công khai được.");

        // BẮT BUỘC có file Excel chi tiết trong ngày cho mọi ngày có món (không nghỉ) sẽ công khai — thiếu thì dừng, không đổi gì ở bản công khai.
        var files = await LayFileTuanAsync(tuan);
        if (HienChiTiet)
        {
            var thieu = ngayChon
                .Where(n => !nghi.Contains(n) && dongChon.Any(d => d.Ngay == n && !d.Bua.StartsWith("PHỤ")) && !files.ContainsKey(n))
                .OrderBy(n => n).ToList();
            if (thieu.Count > 0)
                return (false, "thiếu file chi tiết trong ngày: " + string.Join(", ", thieu.Select(n => $"{TenThu[n.DayNumber - tuan.DayNumber]} ({n:dd/MM})")) +
                    ". Vào \"Công khai thực đơn\" → \"File chi tiết từng ngày\" để tải file Excel của ngày đó lên, rồi công khai lại.");
        }

        var cu = await _ck.CongKhaiTuans.Include(x => x.Mons).Include(x => x.ChiTiets).FirstOrDefaultAsync(x => x.TuanBatDau == tuan);
        var moi = cu is null;
        var nghiCu = new HashSet<DateOnly>();
        if (cu is null)
        {
            cu = new CongKhaiTuan { TuanBatDau = tuan, PhienBan = 1 };
            _ck.CongKhaiTuans.Add(cu);
        }
        else
        {
            var xoa = cu.Mons.ToList();   // thay hẳn bản chụp cũ của tuần: ngày không được chọn sẽ không còn hiện
            _ck.CongKhaiMons.RemoveRange(xoa);
            foreach (var m in xoa) cu.Mons.Remove(m);
            var xoaCt = cu.ChiTiets.ToList();   // bản chụp chi tiết nguyên liệu cũng thay hẳn
            _ck.CongKhaiChiTiets.RemoveRange(xoaCt);
            foreach (var c in xoaCt) cu.ChiTiets.Remove(c);
            cu.PhienBan += 1;
        }
        cu.CongKhaiLuc = DateTime.UtcNow;
        cu.NguoiCongKhai = nguoi;
        cu.DangCongKhai = true;

        nghiCu.RemoveWhere(ngayChon.Contains);
        foreach (var n in nghi.Where(ngayChon.Contains)) nghiCu.Add(n);
        cu.NgayNghi = string.Join(",", nghiCu.OrderBy(x => x).Select(x => x.ToString("yyyy-MM-dd")));

        foreach (var d in dongChon)
            cu.Mons.Add(new CongKhaiMon { Ngay = d.Ngay, Khoi = d.Khoi, ThuTuBua = d.ThuTu, TenBua = d.Bua, TenMon = d.Mon });

        // Chụp chi tiết nguyên liệu + giá của các ngày được công khai = đúng nội dung FILE EXCEL đã tải lên (ngày không có file thì để trống).
        if (HienChiTiet)
            foreach (var n in ngayChon.Where(x => !nghi.Contains(x)).OrderBy(x => x))
                if (files.TryGetValue(n, out var f)) cu.ChiTiets.Add(new CongKhaiChiTiet { Ngay = n, DuLieu = f.DuLieu });

        await _ck.SaveChangesAsync();
        var pham = theoNgay
            ? $"{TieuDeTuan(tuan)} từ {ngayChon.Min():dd/MM} đến hết {ngayChon.Max():dd/MM}"
            : TieuDeTuan(tuan);
        return (true, (moi ? $"Đã công khai {pham}." : $"Đã cập nhật công khai {pham} (phiên bản {cu.PhienBan})."));
    }

    public async Task<bool> GoCongKhaiAsync(DateOnly tuan)
    {
        tuan = ThuHai(tuan);
        var x = await _ck.CongKhaiTuans.FirstOrDefaultAsync(t => t.TuanBatDau == tuan);
        if (x is null) return false;
        x.DangCongKhai = false;
        await _ck.SaveChangesAsync();
        return true;
    }

    // ------------------------------------------------------------------ trang công khai (đọc bản chụp)
    public async Task<PhuHuynhTuan> DocCongKhaiAsync(DateOnly? tuan)
    {
        var cacTuan = await _ck.CongKhaiTuans.AsNoTracking().Where(x => x.DangCongKhai)
            .OrderBy(x => x.TuanBatDau).Select(x => x.TuanBatDau).ToListAsync();
        if (cacTuan.Count == 0) return KhongCoDuLieu();

        DateOnly chon;
        if (tuan is not null && cacTuan.Contains(ThuHai(tuan.Value))) chon = ThuHai(tuan.Value);
        else
        {
            var homNay = ThuHai(DateOnly.FromDateTime(DateTime.Today));
            chon = cacTuan.Contains(homNay) ? homNay
                 : cacTuan.Where(t => t <= homNay).DefaultIfEmpty(cacTuan[^1]).Max();   // chưa công khai tuần này -> tuần gần nhất đã công khai
        }

        var ban = await _ck.CongKhaiTuans.AsNoTracking().Include(x => x.Mons).Include(x => x.ChiTiets).FirstAsync(x => x.TuanBatDau == chon);
        var dong = ban.Mons.Select(m => new Dong(m.Ngay, m.Khoi, m.ThuTuBua, m.TenBua, m.TenMon)).ToList();
        var nghi = (ban.NgayNghi ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null).Where(d => d is not null).Select(d => d!.Value).ToHashSet();
        var chiTiet = new Dictionary<DateOnly, JsonElement>();
        if (HienChiTiet)
            foreach (var c in ban.ChiTiets)
            {
                try { chiTiet[c.Ngay] = JsonSerializer.Deserialize<JsonElement>(c.DuLieu); } catch (JsonException) { }
            }
        var i = cacTuan.IndexOf(chon);
        return Dung(chon, dong, nghi, ban.CongKhaiLuc,
            i > 0 ? cacTuan[i - 1].ToString("yyyy-MM-dd") : null,
            i < cacTuan.Count - 1 ? cacTuan[i + 1].ToString("yyyy-MM-dd") : null, chiTiet);
    }
}
