using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Services;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// "Bảng kê thực đơn - Chi tiền chợ" phiên bản SỐNG: chọn CƠ SỞ + NGÀY BẤT KỲ
/// (không giới hạn theo tuần cố định, dùng mãi qua các tháng sau), món ăn ở
/// mục "1. Thực đơn" chọn bằng dropdown thật (đúng danh mục + công thức trong
/// database) — đổi món hoặc đổi sĩ số là nguyên liệu ở mục 3 (Sáng) và mục 4
/// (Chính+xế) tính lại NGAY qua AJAX (dùng NguyenLieuCalculator, ƯỚC LƯỢNG
/// theo công thức — chưa phải số liệu thật, dùng để lên kế hoạch). Riêng mục
/// 2 (Gia vị hằng ngày) LUÔN hiện đủ danh sách cố định (định mức ở
/// /kho/dinh-muc-gia-vi), ưu tiên phiếu xuất kho thật nếu ngày đó đã lập;
/// sửa trực tiếp + bấm "Lưu gia vị" ngay tại đây cũng lập/ghi đè phiếu đó
/// (tạm thời cho riêng ngày này, không đổi định mức chung) — xem
/// <see cref="ChiTietNgayTinhToanService.LayGiaViThatAsync"/> và
/// <see cref="LuuGiaVi"/>.
/// </summary>
[Authorize(Policy = ChinhSach.KhongPhaiQuanLy)]
[Route("chi-tiet-ngay")]
public sealed class ChiTietNgayController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NguyenLieuCalculator _nguyenLieuCalc;
    private readonly ChiTietNgayTinhToanService _tinhToanSvc;
    private readonly ExcelExportService _excelExport;
    private static readonly SemaphoreSlim LuuLock = new(1, 1);

    public ChiTietNgayController(
        ThucDonDbContext db, UserManager<ApplicationUser> userManager,
        NguyenLieuCalculator nguyenLieuCalc, ChiTietNgayTinhToanService tinhToanSvc,
        ExcelExportService excelExport)
    {
        _excelExport = excelExport;
        _db = db;
        _userManager = userManager;
        _nguyenLieuCalc = nguyenLieuCalc;
        _tinhToanSvc = tinhToanSvc;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? coSoId, DateOnly? ngay)
    {
        var dsCoSoHienThi = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync();
        // Mở trang mà chưa chọn cơ sở -> mặc định "Trường Mầm non Bạch Dương - PH" (nếu không có thì cơ sở đầu tiên).
        coSoId ??= (dsCoSoHienThi.FirstOrDefault(x => x.TenCoSo.TrimEnd().EndsWith("PH", StringComparison.OrdinalIgnoreCase))
                    ?? dsCoSoHienThi.FirstOrDefault())?.Id;

        var vm = new ChiTietNgayViewModel
        {
            CoSoId = coSoId,
            Ngay = ngay ?? DateOnly.FromDateTime(DateTime.Today),
            DsCoSo = dsCoSoHienThi,
            DsMonAn = await _db.MonAns.Where(x => x.IsActive).OrderBy(x => x.TenMon).ToListAsync(),
        };

        if (coSoId is not null)
        {
            var thuHai = vm.Ngay.AddDays(-(((int)vm.Ngay.DayOfWeek + 6) % 7));
            var thuBay = thuHai.AddDays(5);
            vm.ThuBayNghi = await _db.NgayHocs.AnyAsync(n =>
                n.CoSoId == coSoId && n.Ngay == thuBay && n.TrangThai != TrangThaiNgay.HocBinhThuong);
        }

        var khoiMauGiao = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        var khoiNhaTre = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        if (khoiMauGiao is null || khoiNhaTre is null)
        {
            // Chưa chạy đủ file SQL (04/07) -> chưa có đúng 2 khối, không tính tiếp được.
            return View(vm);
        }

        vm.KhoiIdMauGiao = khoiMauGiao.Id;
        vm.KhoiIdNhaTre = khoiNhaTre.Id;

        var dsBuaMg = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiMauGiao.Id).OrderBy(b => b.ThuTu).ToListAsync();
        var dsBuaNt = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiNhaTre.Id).OrderBy(b => b.ThuTu).ToListAsync();

        Dictionary<int, int?> monMg = new(), monNt = new();
        if (coSoId is not null)
        {
            var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSoId && n.Ngay == vm.Ngay);
            vm.TenCoSo = (await _db.CoSos.FindAsync(coSoId.Value))?.TenCoSo;

            if (ngayHoc is not null)
            {
                var cacThucDon = await _db.ThucDons
                    .Where(t => t.CoSoId == coSoId && t.NgayHocId == ngayHoc.Id)
                    .ToListAsync();
                monMg = cacThucDon.Where(t => t.KhoiId == khoiMauGiao.Id).ToDictionary(t => t.LoaiBuaAnId, t => t.MonAnId);
                monNt = cacThucDon.Where(t => t.KhoiId == khoiNhaTre.Id).ToDictionary(t => t.LoaiBuaAnId, t => t.MonAnId);
            }

            var dinhMucCacNhom = await _db.DinhMucAnNgays
                .Include(d => d.NhomChiPhi)
                .Where(d => d.CoSoId == coSoId && d.Ngay == vm.Ngay)
                .ToListAsync();
            var dmAnSang = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Ăn sáng");
            var dmMauGiao = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Mẫu giáo");
            var dmNhaTre = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Nhà trẻ");
            vm.SoSuatAnSang = dmAnSang?.SoSuatAn ?? 0;
            vm.SoSuatMauGiao = dmMauGiao?.SoSuatAn ?? 0;
            vm.SoSuatNhaTre = dmNhaTre?.SoSuatAn ?? 0;
            vm.TienAnSang = dmAnSang?.TienAn1Chau ?? 10000; // mặc định 10.000đ/cháu nếu ngày chưa lưu
            vm.TienMauGiao = dmMauGiao?.TienAn1Chau ?? 29000; // mặc định 29.000đ/cháu
            vm.TienNhaTre = dmNhaTre?.TienAn1Chau ?? 29000; // mặc định 29.000đ/cháu

            var giaViThat = await _tinhToanSvc.LayGiaViThatAsync(coSoId.Value, vm.Ngay);
            vm.GiaViThat = giaViThat.DanhSach;
            vm.CoPhieuGiaVi = giaViThat.CoPhieu;
        }

        vm.BuaMauGiao = dsBuaMg.Select(b => new ChiTietNgayBuaVm
        {
            BuaId = b.Id,
            TenBua = b.TenBua,
            NhomBuaAnId = b.NhomBuaAnId,
            MonAnId = monMg.TryGetValue(b.Id, out var m1) ? m1 : null,
        }).ToList();

        vm.BuaNhaTre = dsBuaNt.Select(b => new ChiTietNgayBuaVm
        {
            BuaId = b.Id,
            TenBua = b.TenBua,
            NhomBuaAnId = b.NhomBuaAnId,
            MonAnId = monNt.TryGetValue(b.Id, out var m2) ? m2 : null,
        }).ToList();

        return View(vm);
    }

    /// <summary>Gom các món đã dùng ở TUẦN GẦN NHẤT trong lịch sử (tuần có thực đơn; ưu tiên tuần trước tuần của <paramref name="ngay"/>,
    /// không có thì tuần mới nhất có thực đơn), theo (khối, bữa). Null nếu lịch sử chưa có thực đơn.</summary>
    private async Task<(DateOnly ThuHai, Dictionary<(int Khoi, int Bua), List<int>> Pool, LoaiBuaAn? SangMg, LoaiBuaAn? SangNt, LoaiBuaAn? XeMg, LoaiBuaAn? ChinhChieuNt, int KhoiMg, int KhoiNt)?> LayPoolLichSuAsync(int coSoId, DateOnly ngay)
    {
        var thuHaiHienTai = ngay.AddDays(-(((int)ngay.DayOfWeek + 6) % 7));
        var coMon = _db.ThucDons.Where(t => t.CoSoId == coSoId && t.MonAnId != null);
        var ngayGanNhat = await coMon.Where(t => t.NgayHoc.Ngay < thuHaiHienTai)
            .OrderByDescending(t => t.NgayHoc.Ngay).Select(t => (DateOnly?)t.NgayHoc.Ngay).FirstOrDefaultAsync();
        ngayGanNhat ??= await coMon.OrderByDescending(t => t.NgayHoc.Ngay).Select(t => (DateOnly?)t.NgayHoc.Ngay).FirstOrDefaultAsync();
        if (ngayGanNhat is null) return null;

        var thuHaiNguon = ngayGanNhat.Value.AddDays(-(((int)ngayGanNhat.Value.DayOfWeek + 6) % 7));
        var dong = await coMon.Where(t => t.NgayHoc.Ngay >= thuHaiNguon && t.NgayHoc.Ngay < thuHaiNguon.AddDays(6))
            .Select(t => new { t.KhoiId, t.LoaiBuaAnId, MonId = t.MonAnId!.Value }).ToListAsync();
        var pool = dong.GroupBy(d => (d.KhoiId, d.LoaiBuaAnId)).ToDictionary(g => g.Key, g => g.Select(x => x.MonId).Distinct().ToList());

        var khoiMg = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        var khoiNt = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        if (khoiMg is null || khoiNt is null) return null;
        return (thuHaiNguon, pool,
            await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiMg.Id && b.TenBua == "SÁNG"),
            await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiNt.Id && b.TenBua == "SÁNG"),
            await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiMg.Id && b.TenBua == "XẾ"),
            await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiNt.Id && b.TenBua == "CHÍNH CHIỀU"),
            khoiMg.Id, khoiNt.Id);
    }

    /// <summary>Bốc món cho 1 ngày: mỗi (khối, bữa) lấy 1 món trong pool, ưu tiên món CHƯA dùng ở các ngày trước (<paramref name="daDung"/>);
    /// hết món chưa dùng thì bốc lại. Áp quy tắc: SÁNG Nhà trẻ = SÁNG Mẫu giáo; CHÍNH CHIỀU Nhà trẻ = XẾ Mẫu giáo.</summary>
    private static (Dictionary<string, int> Mg, Dictionary<string, int> Nt) BocMotNgay(
        (DateOnly ThuHai, Dictionary<(int Khoi, int Bua), List<int>> Pool, LoaiBuaAn? SangMg, LoaiBuaAn? SangNt, LoaiBuaAn? XeMg, LoaiBuaAn? ChinhChieuNt, int KhoiMg, int KhoiNt) p,
        Random rnd, Dictionary<(int, int), HashSet<int>> daDung)
    {
        Dictionary<string, int> Chon(int khoi)
        {
            var kq = new Dictionary<string, int>();
            foreach (var ((k, bua), ds) in p.Pool.Where(x => x.Key.Khoi == khoi))
            {
                if (!daDung.TryGetValue((k, bua), out var dung)) daDung[(k, bua)] = dung = new HashSet<int>();
                var chuaDung = ds.Where(m => !dung.Contains(m)).ToList();
                if (chuaDung.Count == 0) { dung.Clear(); chuaDung = ds; }
                var m1 = chuaDung[rnd.Next(chuaDung.Count)];
                dung.Add(m1);
                kq[bua.ToString()] = m1;
            }
            return kq;
        }
        var mg = Chon(p.KhoiMg);
        var nt = Chon(p.KhoiNt);
        if (p.SangMg is not null && p.SangNt is not null && mg.TryGetValue(p.SangMg.Id.ToString(), out var sang)) nt[p.SangNt.Id.ToString()] = sang;
        if (p.XeMg is not null && p.ChinhChieuNt is not null && mg.TryGetValue(p.XeMg.Id.ToString(), out var xe)) nt[p.ChinhChieuNt.Id.ToString()] = xe;
        return (mg, nt);
    }

    /// <summary>AJAX: nút "Ngẫu nhiên ngày này" — mỗi bữa chọn NGẪU NHIÊN 1 món trong các món đã dùng cho bữa đó ở tuần gần nhất
    /// trong lịch sử. Chỉ trả về lựa chọn, không ghi CSDL.</summary>
    [HttpGet("ngau-nhien")]
    public async Task<IActionResult> NgauNhien(int coSoId, DateOnly ngay)
    {
        var p = await LayPoolLichSuAsync(coSoId, ngay);
        if (p is null) return Json(new { ok = false, loi = "Lịch sử chưa có thực đơn nào để lấy dữ liệu." });
        var (mg, nt) = BocMotNgay(p.Value, new Random(), new Dictionary<(int, int), HashSet<int>>());
        return Json(new { ok = true, tuan = p.Value.ThuHai.ToString("dd/MM/yyyy"), monMauGiao = mg, monNhaTre = nt });
    }

    /// <summary>AJAX: nút "Ngẫu nhiên cả tuần" — Thứ 2 → Thứ 7 của tuần chứa <paramref name="ngay"/>. Quy tắc:
    /// (1) mỗi bữa chỉ chọn món ĐANG DÙNG thuộc đúng nhóm bữa đó; (2) trong cả tuần KHÔNG lặp món;
    /// (3) KHÔNG lấy món đã dùng ở TUẦN GẦN NHẤT (tuần gần nhất trước tuần này có thực đơn).
    /// (4) Món dạng cháo/súp không quá nhiều trong tuần (trẻ ngán): tối đa <see cref="TranMonCungHoTrongTuan"/> món mỗi họ.
    /// Hết món thỏa các điều kiện thì nới dần: cho phép lại món của tuần gần nhất, rồi vượt trần "họ món", sau cùng mới cho lặp trong tuần
    /// (có báo trong "canhBao"). Nhà trẻ ăn cùng món Mẫu giáo ở các bữa trùng tên (SÁNG/CANH/MẶN/TRÁNG MIỆNG, CHÍNH CHIỀU = XẾ).
    /// Chỉ trả về lựa chọn, không ghi CSDL; số suất ăn của tuần gần nhất được sao sang tuần mới khi bấm Lưu / Xuất Excel.</summary>
    /// <summary>Trần số món cùng "họ" (theo chữ đầu tên món) trong 1 tuần khi ngẫu nhiên — sửa số ở đây nếu muốn nhiều/ít hơn.</summary>
    private static readonly (string TienTo, int Toida)[] TranMonCungHoTrongTuan = { ("Cháo", 2), ("Súp", 1) };

    private static string? HoMon(string? tenMon)
    {
        var ten = (tenMon ?? "").Trim();
        foreach (var (tienTo, _) in TranMonCungHoTrongTuan)
            if (ten.StartsWith(tienTo, StringComparison.OrdinalIgnoreCase)) return tienTo;
        return null;
    }

    [HttpGet("ngau-nhien-ca-tuan")]
    public async Task<IActionResult> NgauNhienCaTuan(int coSoId, DateOnly ngay)
    {
        // Gọi từ Dashboard (thực đơn dùng chung mọi cơ sở) không truyền cơ sở -> lấy cơ sở đại diện (active đầu tiên theo tên).
        coSoId = coSoId > 0 ? coSoId : ((await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).FirstOrDefaultAsync())?.Id ?? 0);
        var thuHai = ngay.AddDays(-(((int)ngay.DayOfWeek + 6) % 7));

        // Tuần gần nhất TRƯỚC tuần đang soạn mà có thực đơn.
        var coMon = _db.ThucDons.Where(t => t.CoSoId == coSoId && t.MonAnId != null);
        var ngayGanNhat = await coMon.Where(t => t.NgayHoc.Ngay < thuHai)
            .OrderByDescending(t => t.NgayHoc.Ngay).Select(t => (DateOnly?)t.NgayHoc.Ngay).FirstOrDefaultAsync();
        if (ngayGanNhat is null) return Json(new { ok = false, loi = "Chưa có tuần nào trước tuần này có thực đơn để làm mốc." });
        var thuHaiNguon = ngayGanNhat.Value.AddDays(-(((int)ngayGanNhat.Value.DayOfWeek + 6) % 7));
        var monTuanGanNhat = (await coMon.Where(t => t.NgayHoc.Ngay >= thuHaiNguon && t.NgayHoc.Ngay < thuHaiNguon.AddDays(7))
            .Select(t => t.MonAnId!.Value).Distinct().ToListAsync()).ToHashSet();

        var khoiMg = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        var khoiNt = await _db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        if (khoiMg is null || khoiNt is null) return Json(new { ok = false, loi = "Chưa có đủ 2 khối Mẫu giáo / Nhà trẻ." });
        var buaMg = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiMg.Id).OrderBy(b => b.ThuTu).ToListAsync();
        var buaNt = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiNt.Id).OrderBy(b => b.ThuTu).ToListAsync();
        var monTheoNhomTho = (await _db.MonAns.AsNoTracking().Where(m => m.IsActive && m.NhomBuaAnId != null)
                .Select(m => new { m.Id, m.TenMon, Nhom = m.NhomBuaAnId!.Value }).ToListAsync())
            .ToList();
        var hoCuaMon = monTheoNhomTho.ToDictionary(m => m.Id, m => HoMon(m.TenMon));
        var monTheoNhom = monTheoNhomTho.GroupBy(m => m.Nhom).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var soMonTheoHo = TranMonCungHoTrongTuan.ToDictionary(x => x.TienTo, _ => 0);
        var tranHo = TranMonCungHoTrongTuan.ToDictionary(x => x.TienTo, x => x.Toida);
        var soVuotHo = 0;

        var rnd = new Random();
        var daDungTuan = new HashSet<int>();
        var daDungSuaNt = new HashSet<int>();   // bữa XẾ của Nhà trẻ (sữa) ít món, không áp quy tắc không lặp
        var soGiongTuanTruoc = 0;
        var soLapTrongTuan = 0;
        var cacNgay = new List<object>();

        int? Boc(LoaiBuaAn bua, HashSet<int> daDung, bool ap)
        {
            if (bua.NhomBuaAnId is null || !monTheoNhom.TryGetValue(bua.NhomBuaAnId.Value, out var ds) || ds.Count == 0) return null;
            bool VuotHo(int m) => ap && hoCuaMon[m] is string ho && soMonTheoHo[ho] >= tranHo[ho];
            var tot = ds.Where(m => !daDung.Contains(m) && !VuotHo(m) && (!ap || !monTuanGanNhat.Contains(m))).ToList();
            if (tot.Count == 0) { tot = ds.Where(m => !daDung.Contains(m) && !VuotHo(m)).ToList(); if (tot.Count > 0 && ap) soGiongTuanTruoc++; }
            if (tot.Count == 0) { tot = ds.Where(m => !daDung.Contains(m)).ToList(); if (tot.Count > 0 && ap) soVuotHo++; }
            if (tot.Count == 0) { tot = ds; if (ap) soLapTrongTuan++; }
            var chon = tot[rnd.Next(tot.Count)];
            daDung.Add(chon);
            if (ap && hoCuaMon[chon] is string hoChon) soMonTheoHo[hoChon]++;
            return chon;
        }

        for (var i = 0; i < 6; i++)
        {
            var mg = new Dictionary<string, int>();
            var nt = new Dictionary<string, int>();
            var monMgTheoTen = new Dictionary<string, (int Mon, int? Nhom)>();
            foreach (var bua in buaMg)
            {
                var m = Boc(bua, daDungTuan, true);
                if (m is null) continue;
                mg[bua.Id.ToString()] = m.Value;
                monMgTheoTen[bua.TenBua] = (m.Value, bua.NhomBuaAnId);
            }
            foreach (var bua in buaNt)
            {
                // Nhà trẻ ăn cùng món Mẫu giáo ở bữa trùng tên (CHÍNH CHIỀU của Nhà trẻ = XẾ của Mẫu giáo).
                var tenMg = bua.TenBua == "CHÍNH CHIỀU" ? "XẾ" : bua.TenBua;
                if (!(bua.TenBua == "XẾ") && monMgTheoTen.TryGetValue(tenMg, out var chung) && chung.Nhom == bua.NhomBuaAnId)
                {
                    nt[bua.Id.ToString()] = chung.Mon;
                    continue;
                }
                var m = bua.TenBua == "XẾ" ? Boc(bua, daDungSuaNt, false) : Boc(bua, daDungTuan, true);
                if (m is not null) nt[bua.Id.ToString()] = m.Value;
            }
            cacNgay.Add(new { ngay = thuHai.AddDays(i).ToString("yyyy-MM-dd"), monMauGiao = mg, monNhaTre = nt });
        }

        var canhBao = new List<string>();
        if (soGiongTuanTruoc > 0) canhBao.Add($"{soGiongTuanTruoc} bữa không còn món khác tuần gần nhất nên phải dùng lại món của tuần đó.");
        if (soVuotHo > 0) canhBao.Add($"{soVuotHo} bữa hết món khác nên phải vượt số món cháo/súp tối đa trong tuần.");
        if (soLapTrongTuan > 0) canhBao.Add($"{soLapTrongTuan} bữa hết món nên phải lặp món trong tuần.");
        return Json(new { ok = true, tuan = thuHaiNguon.ToString("dd/MM/yyyy"), cacNgay, canhBao });
    }

    /// <summary>AJAX: tính lại "gia vị" (3 cột) + "sáng" + "chính+xế" (2 cột) mỗi khi đổi món/sĩ số.</summary>
    [HttpPost("tinh-toan")]
    public async Task<IActionResult> TinhToan([FromBody] ChiTietNgayTinhToanRequest req)
    {
        var dsBuaMg = await _db.LoaiBuaAns.Where(b => b.KhoiId == req.KhoiIdMauGiao).ToListAsync();
        var dsBuaNt = await _db.LoaiBuaAns.Where(b => b.KhoiId == req.KhoiIdNhaTre).ToListAsync();

        var tatCaMonId = req.MonMauGiao.Values.Concat(req.MonNhaTre.Values)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var monTheoId = (await _db.MonAns.Where(m => tatCaMonId.Contains(m.Id))
                .Include(m => m.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
                .ToListAsync())
            .ToDictionary(m => m.Id);

        List<ThucDonEntity> XayThucDonTam(List<LoaiBuaAn> dsBua, Dictionary<int, int?> monDaChon) =>
            dsBua.Select(bua =>
            {
                var monId = monDaChon.TryGetValue(bua.Id, out var m) ? m : null;
                return new ThucDonEntity
                {
                    LoaiBuaAnId = bua.Id,
                    LoaiBuaAn = bua,
                    MonAnId = monId,
                    MonAn = monId is not null && monTheoId.TryGetValue(monId.Value, out var mon) ? mon : null,
                };
            }).ToList();

        var thucDonMg = XayThucDonTam(dsBuaMg, req.MonMauGiao);
        var thucDonNt = XayThucDonTam(dsBuaNt, req.MonNhaTre);

        // Bữa SÁNG dùng CHUNG cho cả 2 khối (đúng file Excel thực đơn: 1 món sáng, 1 sĩ số
        // "Ăn sáng", 1 mức tiền/cháu) — lấy món sáng của Mẫu giáo × số suất ăn sáng.
        var maCoSoTinh = req.CoSoId > 0
            ? await _db.CoSos.AsNoTracking().Where(c => c.Id == req.CoSoId).Select(c => c.MaCoSo).FirstOrDefaultAsync()
            : null;
        var buaSangMg = thucDonMg.Where(t => t.LoaiBuaAn.TenBua == "SÁNG");
        var ketQuaSang = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            buaSangMg.Select(t => (t, req.SoSuatAnSang)), maCoSo: maCoSoTinh);

        var ketQuaMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatMauGiao)), boSungTrangMieng: 5, maCoSo: maCoSoTinh);   // tráng miệng Mẫu giáo: số trẻ + 5 (đúng file mẫu: =J7+5)
        var ketQuaNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatNhaTre)), boSungTrangMieng: 1, maCoSo: maCoSoTinh);   // tráng miệng Nhà trẻ: số trẻ + 1 (đúng file mẫu: =I7+1)

        // Nhu cầu nguyên liệu TÁCH THEO TỪNG MÓN (không gộp giữa các món) — dùng để nhận ra phần của món Súp (Tinh) và tách mục 4 thành từng món ở cuối hàm.
        var nhuCauMonMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatMauGiao)), boSungTrangMieng: 5, tachTheoMon: true, maCoSo: maCoSoTinh);
        var nhuCauMonNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatNhaTre)), boSungTrangMieng: 1, tachTheoMon: true, maCoSo: maCoSoTinh);

        var response = _tinhToanSvc.Tinh(
            ketQuaSang, ketQuaNt, ketQuaMg, req.SoSuatNhaTre, req.SoSuatMauGiao, maCoSoTinh, req.SoSuatAnSang,
            sangLaChao: thucDonMg.Any(t => t.LoaiBuaAn.TenBua == "SÁNG" && t.MonAn != null && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)),
            nhuCauTheoMonNt: nhuCauMonNt, nhuCauTheoMonMg: nhuCauMonMg,
            ngay: req.Ngay == default ? null : req.Ngay);

        // Mục 3/4: nếu ngày này ĐÃ có số lưu cố định (do bấm Sửa/Xóa ở từng dòng, hoặc phiếu
        // xuất kho thật) thì hiện số đã lưu thay cho số ước lượng theo thực đơn.
        if (req.CoSoId > 0 && req.Ngay != default)
        {
            var uocLuongChinh = response.Chinh.ToList();   // số ước lượng theo thực đơn (trước khi bị số đã lưu thay thế)
            var thucPham = await _tinhToanSvc.LayThucPhamThatAsync(req.CoSoId, req.Ngay, maCoSoTinh);
            if (thucPham.CoPhieuSang)
            {
                response.Sang = thucPham.Sang;
                response.TongTienSang = thucPham.TongTienSang;
                response.CoPhieuSang = true;
            }
            if (thucPham.CoPhieuChinhXe)
            {
                response.Chinh = ChiTietNgayTinhToanService.SapXepTheoThucDon(thucPham.Chinh, uocLuongChinh);
                response.TongTienChinhNhaTre = thucPham.TongTienChinhNhaTre;
                response.TongTienChinhMauGiao = thucPham.TongTienChinhMauGiao;
                response.CoPhieuChinhXe = true;
                response.GhiChuLech = ChiTietNgayTinhToanService.PhatHienPhieuCu(uocLuongChinh, thucPham.Chinh);
            }
        }

        // Mục 4: tách nguyên liệu dùng chung giữa nhiều món (VD "Canh A + Gà sốt B + Cháo C") ra từng dòng riêng cho từng món.
        _tinhToanSvc.TachTheoMon(response, nhuCauMonNt, nhuCauMonMg, maCoSoTinh);

        return Json(response);
    }

    /// <summary>Lưu thực đơn + sĩ số cho đúng 1 (cơ sở, ngày). Món ăn (mục "1. Thực đơn") được
    /// ghi GIỐNG HỆT cho MỌI cơ sở active — vì thực đơn dùng chung cho cả trường (đã xác nhận
    /// bằng dữ liệu thật). Riêng sĩ số/tiền ăn 1 cháu chỉ áp dụng cho đúng cơ sở đang xem, vì
    /// mỗi cơ sở có số cháu khác nhau.</summary>
    [HttpPost("luu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Luu(
        int coSoId, DateOnly ngay, int khoiIdMauGiao, int khoiIdNhaTre,
        string monMauGiaoJson, string monNhaTreJson,
        int soSuatAnSang, int soSuatMauGiao, int soSuatNhaTre,
        decimal tienAnSang, decimal tienMauGiao, decimal tienNhaTre,
        bool guiDuyet, bool xuatExcelSauKhiLuu = false,
        string? excelGiaViJson = null, string? excelSangJson = null, string? excelChinhJson = null,
        string? draftTuanJson = null, bool canDoiChenhLech = true, bool luuCaTuan = false)
    {
        var monMg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int?>>(monMauGiaoJson) ?? new();
        var monNt = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int?>>(monNhaTreJson) ?? new();

        var nguoiDung = await _userManager.GetUserAsync(User);
        var nguoiLap = nguoiDung?.HoTen ?? nguoiDung?.UserName;
        var trangThaiMoi = guiDuyet ? TrangThaiThucDon.ChoDuyet : TrangThaiThucDon.Nhap;

        // ---- Quy tắc chuẩn hoá thực đơn của 1 ngày (dùng cho CẢ ngày đang xem lẫn các ngày khác trong tuần) ----
        var buaSangMg = await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiIdMauGiao && b.TenBua == "SÁNG");
        var buaSangNt = await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiIdNhaTre && b.TenBua == "SÁNG");
        var xeMg = await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiIdMauGiao && b.TenBua == "XẾ");
        var chinhChieuNt = await _db.LoaiBuaAns.FirstOrDefaultAsync(b => b.KhoiId == khoiIdNhaTre && b.TenBua == "CHÍNH CHIỀU");

        void ChuanHoaMon(Dictionary<string, int?> mg, Dictionary<string, int?> nt)
        {
            // Bữa SÁNG dùng chung: món sáng của Nhà trẻ luôn = món sáng của Mẫu giáo.
            if (buaSangMg is not null && buaSangNt is not null)
                nt[buaSangNt.Id.ToString()] = mg.TryGetValue(buaSangMg.Id.ToString(), out var monSang) ? monSang : null;

            // Món chiều: XẾ của Mẫu giáo = CHÍNH CHIỀU của Nhà trẻ (thực đơn gốc). Bên nào để TRỐNG thì lấy theo bên kia.
            if (xeMg is not null && chinhChieuNt is not null)
            {
                var kMg = xeMg.Id.ToString(); var kNt = chinhChieuNt.Id.ToString();
                mg.TryGetValue(kMg, out var vMg); nt.TryGetValue(kNt, out var vNt);
                if (vMg is null && vNt is not null) mg[kMg] = vNt;
                else if (vNt is null && vMg is not null) nt[kNt] = vMg;
            }
        }

        async Task LuuChoKhoiAsync(int coSo, int ngayHocId, int khoiId, Dictionary<string, int?> monDaChon)
        {
            var dsBua = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiId).ToListAsync();
            foreach (var bua in dsBua)
            {
                var monId = monDaChon.TryGetValue(bua.Id.ToString(), out var m) ? m : null;
                var thucDon = await _db.ThucDons.FirstOrDefaultAsync(t =>
                    t.CoSoId == coSo && t.KhoiId == khoiId && t.NgayHocId == ngayHocId && t.LoaiBuaAnId == bua.Id);

                if (thucDon is null)
                {
                    _db.ThucDons.Add(new ThucDonEntity
                    {
                        CoSoId = coSo,
                        KhoiId = khoiId,
                        NgayHocId = ngayHocId,
                        LoaiBuaAnId = bua.Id,
                        MonAnId = monId,
                        TrangThai = trangThaiMoi,
                        NguoiLap = nguoiLap,
                        NgayLap = DateOnly.FromDateTime(DateTime.Today),
                    });
                }
                else
                {
                    thucDon.MonAnId = monId;
                    thucDon.TrangThai = trangThaiMoi;
                    thucDon.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        var dsCoSo = await _db.CoSos.Where(x => x.IsActive).ToListAsync();
        var nhomTheoTen = await _db.NhomChiPhis.ToDictionaryAsync(n => n.TenNhom, n => n.Id);

        // Sĩ số/tiền ăn thuộc RIÊNG từng cơ sở (mỗi cơ sở có số cháu khác nhau). Giá trị null = không đổi.
        async Task LuuDinhMucAsync(int coSoLuu, DateOnly ngayLuu, string tenNhom, int? soSuat, decimal? tienAn)
        {
            if (soSuat is null && tienAn is null) return;
            if (!nhomTheoTen.TryGetValue(tenNhom, out var nhomId)) return;

            var dinhMuc = await _db.DinhMucAnNgays.FirstOrDefaultAsync(d =>
                d.CoSoId == coSoLuu && d.NhomChiPhiId == nhomId && d.Ngay == ngayLuu);
            if (dinhMuc is null)
            {
                _db.DinhMucAnNgays.Add(new DinhMucAnNgay
                {
                    CoSoId = coSoLuu,
                    NhomChiPhiId = nhomId,
                    Ngay = ngayLuu,
                    SoSuatAn = soSuat ?? 0,
                    TienAn1Chau = tienAn ?? 0,
                });
            }
            else
            {
                if (soSuat is not null) dinhMuc.SoSuatAn = soSuat.Value;
                if (tienAn is not null) dinhMuc.TienAn1Chau = tienAn.Value;
            }
        }

        // Lưu trọn 1 ngày: món GIỐNG HỆT cho mọi cơ sở active (mỗi cơ sở cần NgayHoc riêng) + sĩ số/tiền từng cơ sở.
        async Task LuuMotNgayAsync(DateOnly ngayLuu, Dictionary<string, int?>? mg, Dictionary<string, int?>? nt, List<DinhMucCoSo> dinhMuc)
        {
            if (mg is not null && nt is not null)
            {
                var mgChuan = new Dictionary<string, int?>(mg);
                var ntChuan = new Dictionary<string, int?>(nt);
                ChuanHoaMon(mgChuan, ntChuan);

                foreach (var coSo in dsCoSo)
                {
                    // Tạo ngày học nếu chưa có bằng INSERT OR IGNORE: nếu (cơ sở, ngày) đã tồn tại (VD bấm Lưu 2 lần / mở 2 tab cùng lúc)
                    // thì bỏ qua thay vì lỗi "UNIQUE constraint failed: ngay_hoc.co_so_id, ngay_hoc.ngay", rồi đọc lại dòng đã có.
                    await _db.Database.ExecuteSqlRawAsync(
                        "INSERT OR IGNORE INTO ngay_hoc (co_so_id, ngay, trang_thai) VALUES ({0}, {1}, 'Học bình thường')",
                        coSo.Id, ngayLuu.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                    var ngayHoc = await _db.NgayHocs.FirstAsync(n => n.CoSoId == coSo.Id && n.Ngay == ngayLuu);

                    await LuuChoKhoiAsync(coSo.Id, ngayHoc.Id, khoiIdMauGiao, mgChuan);
                    await LuuChoKhoiAsync(coSo.Id, ngayHoc.Id, khoiIdNhaTre, ntChuan);
                }
            }

            foreach (var x in dinhMuc)
            {
                await LuuDinhMucAsync(x.CoSoId, ngayLuu, "Ăn sáng", x.SuatSang, x.TienSang);
                await LuuDinhMucAsync(x.CoSoId, ngayLuu, "Mẫu giáo", x.SuatMauGiao, x.TienMauGiao);
                await LuuDinhMucAsync(x.CoSoId, ngayLuu, "Nhà trẻ", x.SuatNhaTre, x.TienNhaTre);
            }
            await _db.SaveChangesAsync();
        }

        // ---- Gom các ngày cần lưu: ngày đang xem (theo màn hình) + (nếu bấm "Lưu nháp") cả tuần Thứ 2 → Thứ 7
        //      của cả 3 cơ sở theo BẢN NHÁP trình duyệt đang giữ.
        var cacNgay = new Dictionary<DateOnly, NgayCanLuu>
        {
            [ngay] = new NgayCanLuu
            {
                Mg = monMg,
                Nt = monNt,
                DinhMuc = { new DinhMucCoSo(coSoId, soSuatAnSang, soSuatMauGiao, soSuatNhaTre, tienAnSang, tienMauGiao, tienNhaTre) },
            },
        };

        if (luuCaTuan && !string.IsNullOrWhiteSpace(draftTuanJson))
        {
            var optLuu = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cacDraft = System.Text.Json.JsonSerializer.Deserialize<List<DraftNgayCoSo>>(draftTuanJson, optLuu) ?? new();
            foreach (var d in cacDraft)
            {
                if (d.Ngay == ngay && d.CoSoId == coSoId) continue;   // ngày/cơ sở đang xem: lấy theo màn hình
                if (!cacNgay.TryGetValue(d.Ngay, out var e)) { e = new NgayCanLuu(); cacNgay[d.Ngay] = e; }
                if (d.Ngay != ngay && e.Mg is null && d.MonMauGiao is not null)
                {
                    e.Mg = d.MonMauGiao;
                    e.Nt = d.MonNhaTre ?? new();
                }
                if (d.SoSuatAnSang is not null || d.SoSuatMauGiao is not null || d.SoSuatNhaTre is not null
                    || d.TienAnSang is not null || d.TienMauGiao is not null || d.TienNhaTre is not null)
                    e.DinhMuc.Add(new DinhMucCoSo(d.CoSoId, d.SoSuatAnSang, d.SoSuatMauGiao, d.SoSuatNhaTre, d.TienAnSang, d.TienMauGiao, d.TienNhaTre));
            }
        }

        // Chỉ cho 1 lượt Lưu chạy tại 1 thời điểm: 2 lượt Lưu chồng nhau (bấm đúp, mở 2 tab) cùng tạo 1 ngày học mới sẽ đụng nhau.
        await LuuLock.WaitAsync();
        try
        {
            foreach (var (ngayLuu, e) in cacNgay.OrderBy(x => x.Key))
                await LuuMotNgayAsync(ngayLuu, e.Mg, e.Nt, e.DinhMuc);
        }
        finally
        {
            LuuLock.Release();
        }

        // Nút "Xuất Excel" gộp sẵn thao tác Lưu nháp trước khi tải file, để người dùng
        // không phải bấm Lưu nháp thủ công rồi mới bấm Xuất Excel — chuyển thẳng sang
        // route tải file, đúng đủ dữ liệu VỪA lưu (không còn TempData thông báo nữa vì
        // không quay lại trang Index).
        if (xuatExcelSauKhiLuu)
        {
            // Xuất Excel đúng theo số ĐANG HIỆN TRÊN TRANG (mục 2/3/4, kể cả số vừa gõ trong ô
            // Sửa chưa bấm Lưu) — không ghi gì thêm vào CSDL, chỉ dùng để dựng file.
            var opt = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<DongTrenTrang>? Doc(string? json) => string.IsNullOrWhiteSpace(json)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<List<DongTrenTrang>>(json, opt);

            var giaVi = Doc(excelGiaViJson);
            var sang = Doc(excelSangJson);
            var chinh = Doc(excelChinhJson);

            var trenTrang = new ExcelDuLieuTrenTrang(
                coSoId, ngay,
                giaVi?.Select(d => new NguyenLieuBaCotDto(
                    d.HangHoaId, d.TenHang ?? "", d.Dvt,
                    d.SlAnSang, d.SlNhaTre, d.SlMauGiao, d.DonGia,
                    d.SlAnSang * d.DonGia, d.SlNhaTre * d.DonGia, d.SlMauGiao * d.DonGia)).ToList(),
                sang?.Select(d => new NguyenLieuMotCotDto(
                    d.TenHang ?? "", d.Dvt, d.SoLuong, d.DonGia, d.SoLuong * d.DonGia, d.HangHoaId, 0, d.TenMon)).ToList(),
                chinh?.Select(d => new NguyenLieuHaiCotDto(
                    d.TenHang ?? "", d.Dvt, d.SlNhaTre, d.SlMauGiao, d.DonGia,
                    d.SlNhaTre * d.DonGia, d.SlMauGiao * d.DonGia, d.HangHoaId, 0, d.TenMon)).ToList());

            // Bản nháp chưa lưu của các ngày/cơ sở khác trong tuần (trình duyệt giữ) — để Excel ra
            // đủ Thứ 2 → Thứ 7 mà không cần mở từng ngày bấm Lưu nháp. Không ghi gì vào CSDL.
            var drafts = string.IsNullOrWhiteSpace(draftTuanJson)
                ? new List<DraftNgayCoSo>()
                : System.Text.Json.JsonSerializer.Deserialize<List<DraftNgayCoSo>>(draftTuanJson, opt) ?? new();

            try
            {
                var bytes = await _excelExport.XuatBangKeChoNgayAsync(ngay, null, drafts, canDoiChenhLech);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", LichSuThucDonViewModel.TenFileTuan(ngay));
            }
        catch (ThieuBangGiaException ex)
        {
            if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return StatusCode(422, new { ok = false, message = ex.Message, items = ex.Dong });
            TempData["ThongBao"] = ex.Message;
            return RedirectToAction(nameof(Index), new { coSoId, ngay });
        }
        }

        TempData["ThongBao"] = luuCaTuan
            ? $"Đã {(guiDuyet ? "gửi duyệt" : "lưu nháp")} {cacNgay.Count} ngày trong tuần (Thứ 2 → Thứ 7) cho cả 3 cơ sở."
            : (guiDuyet ? "Đã gửi duyệt (món áp dụng cho mọi cơ sở)." : "Đã lưu nháp (món áp dụng cho mọi cơ sở).");
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    // ------------------------------------------------------------------------------------
    // Mục 3 (Chi tiền thực phẩm sáng) + mục 4 (Chi tiền thực phẩm ăn chính + xế): Sửa/Xóa
    // từng dòng, giống mục 2. Số sửa được lưu vào phiếu xuất kho của đúng (cơ sở, ngày):
    // loại "Thực phẩm sáng" cho mục 3, "Thực phẩm chính + xế" cho mục 4. Lần sửa/xóa ĐẦU
    // TIÊN trong ngày, các dòng đang hiện trên màn hình (số ước lượng theo thực đơn) được
    // ghi cố định vào phiếu cùng lúc (snapshotJson) để không dòng nào bị mất.
    // "Xóa" = đưa số lượng về 0 (dòng vẫn còn trong bảng, giống mục 2).
    // ------------------------------------------------------------------------------------
    /// <summary>1 dòng số liệu đang hiện trên trang (JS gửi lên khi bấm Xuất Excel) — dùng chung cho cả
    /// mục 2 (SlAnSang/SlNhaTre/SlMauGiao), mục 3 (SoLuong) và mục 4 (SlNhaTre/SlMauGiao).</summary>
    private sealed record DinhMucCoSo(int CoSoId, int? SuatSang, int? SuatMauGiao, int? SuatNhaTre,
        decimal? TienSang, decimal? TienMauGiao, decimal? TienNhaTre);

    private sealed class NgayCanLuu
    {
        public Dictionary<string, int?>? Mg { get; set; }
        public Dictionary<string, int?>? Nt { get; set; }
        public List<DinhMucCoSo> DinhMuc { get; } = new();
    }

    private sealed record DongTrenTrang(
        int HangHoaId, string? TenHang, string? Dvt, decimal DonGia,
        decimal SoLuong, decimal SlAnSang, decimal SlNhaTre, decimal SlMauGiao, string? TenMon = null);

    private sealed record DongThucPhamSnapshot(int HangHoaId, decimal SoLuong, decimal SlNhaTre, decimal SlMauGiao, decimal DonGia);

    [HttpPost("luu-mot-thuc-pham")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuMotDongThucPham(
        int coSoId, DateOnly ngay, string loai, int hangHoaId, decimal donGia,
        decimal soLuong, decimal slNhaTre, decimal slMauGiao, string? snapshotJson)
    {
        TempData["LoiMau"] = "Trang Chi tiết ngày chỉ để xem — không được sửa/xóa nguyên liệu.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
#pragma warning disable CS0162

        await GhiMotDongThucPhamAsync(coSoId, ngay, loai, hangHoaId, donGia, soLuong, slNhaTre, slMauGiao, snapshotJson);
        TempData["ThongBao"] = "Đã lưu dòng thực phẩm cho riêng ngày này.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    [HttpPost("xoa-mot-thuc-pham")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaDongThucPham(int coSoId, DateOnly ngay, string loai, int hangHoaId, decimal donGia, string? snapshotJson)
    {
        TempData["LoiMau"] = "Trang Chi tiết ngày chỉ để xem — không được sửa/xóa nguyên liệu.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
#pragma warning disable CS0162

        await GhiMotDongThucPhamAsync(coSoId, ngay, loai, hangHoaId, donGia, 0, 0, 0, snapshotJson);
        TempData["ThongBao"] = "Đã xóa số lượng mặt hàng này cho riêng ngày này.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    /// <summary>Bỏ phần đã lưu cố định của mục 3 hoặc mục 4 ở ngày này — bảng quay lại hiện số
    /// ước lượng theo thực đơn. CHÚ Ý: xoá cả phiếu xuất kho loại đó của ngày này.</summary>
    [HttpPost("dat-lai-thuc-pham")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DatLaiThucPham(int coSoId, DateOnly ngay, string loai)
    {
        TempData["LoiMau"] = "Trang Chi tiết ngày chỉ để xem — không được sửa/xóa nguyên liệu.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
#pragma warning disable CS0162

        var loaiXuat = loai == "sang" ? LoaiXuatKho.ThucPhamSang : LoaiXuatKho.ThucPhamChinhXe;
        var phieu = await _db.PhieuXuatKhos.Include(p => p.ChiTiets)
            .FirstOrDefaultAsync(p => p.CoSoId == coSoId && p.NgayXuat == ngay && p.LoaiXuat == loaiXuat);
        if (phieu is not null)
        {
            _db.ChiTietXuatKhos.RemoveRange(phieu.ChiTiets);
            _db.PhieuXuatKhos.Remove(phieu);
            await _db.SaveChangesAsync();
        }
        TempData["ThongBao"] = "Đã bỏ số lưu cố định — bảng tính lại theo thực đơn.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    private async Task GhiMotDongThucPhamAsync(
        int coSoId, DateOnly ngay, string loai, int hangHoaId, decimal donGia,
        decimal soLuong, decimal slNhaTre, decimal slMauGiao, string? snapshotJson)
    {
        var laSang = loai == "sang";
        var loaiXuat = laSang ? LoaiXuatKho.ThucPhamSang : LoaiXuatKho.ThucPhamChinhXe;

        int? nhomNhaTreId = null, nhomMauGiaoId = null;
        if (!laSang)
        {
            var nhomTheoTen = await _db.NhomChiPhis.ToDictionaryAsync(n => n.TenNhom, n => n.Id);
            if (!nhomTheoTen.TryGetValue("Nhà trẻ", out var nt) || !nhomTheoTen.TryGetValue("Mẫu giáo", out var mg))
                throw new InvalidOperationException("Thiếu nhóm chi phí Nhà trẻ / Mẫu giáo — chạy đủ Database/01 trước.");
            nhomNhaTreId = nt;
            nhomMauGiaoId = mg;
        }

        var phieu = await _db.PhieuXuatKhos
            .Include(p => p.ChiTiets)
            .FirstOrDefaultAsync(p => p.CoSoId == coSoId && p.NgayXuat == ngay && p.LoaiXuat == loaiXuat);

        void ThemDong(int hhId, decimal gia, decimal sl, decimal slNt, decimal slMg)
        {
            if (laSang)
            {
                phieu!.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hhId, NhomChiPhiId = null, SoLuong = sl, DonGia = gia });
            }
            else
            {
                phieu!.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hhId, NhomChiPhiId = nhomNhaTreId, SoLuong = slNt, DonGia = gia });
                phieu.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hhId, NhomChiPhiId = nhomMauGiaoId, SoLuong = slMg, DonGia = gia });
            }
        }

        if (phieu is null)
        {
            phieu = new PhieuXuatKho
            {
                CoSoId = coSoId,
                NgayXuat = ngay,
                LoaiXuat = loaiXuat,
                NguoiLap = User.Identity?.Name,
            };
            _db.PhieuXuatKhos.Add(phieu);

            // Lần đầu: ghi cố định đủ các dòng đang hiện (trừ dòng đang sửa — ghi ở dưới).
            if (!string.IsNullOrWhiteSpace(snapshotJson))
            {
                var dongHienTai = System.Text.Json.JsonSerializer.Deserialize<List<DongThucPhamSnapshot>>(
                    snapshotJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                var hopLe = await _db.HangHoas.Where(h => dongHienTai.Select(d => d.HangHoaId).Contains(h.Id)).Select(h => h.Id).ToListAsync();
                foreach (var d in dongHienTai.Where(d => d.HangHoaId != hangHoaId && hopLe.Contains(d.HangHoaId)))
                    ThemDong(d.HangHoaId, d.DonGia, d.SoLuong, d.SlNhaTre, d.SlMauGiao);
            }
        }
        else
        {
            // Phiếu đã có: chỉ thay đúng các dòng của hàng hoá này, dòng khác giữ nguyên.
            var dongCu = phieu.ChiTiets.Where(c => c.HangHoaId == hangHoaId).ToList();
            _db.ChiTietXuatKhos.RemoveRange(dongCu);
            foreach (var d in dongCu) phieu.ChiTiets.Remove(d);
        }

        ThemDong(hangHoaId, donGia, soLuong, slNhaTre, slMauGiao);
        await _db.SaveChangesAsync();
    }

    /// <summary>Nút "Sửa" ở 1 dòng mục "2. Gia vị hằng ngày" trên trang Chi tiết ngày — lưu
    /// TẠM THỜI cho ĐÚNG 1 ngày + ĐÚNG 1 hàng hoá này (không đụng tới các hàng hoá khác
    /// trong cùng phiếu, không đụng tới định mức cố định ở /kho/dinh-muc-gia-vi).</summary>
    [HttpPost("luu-mot-gia-vi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuMotDongGiaVi(int coSoId, DateOnly ngay, int hangHoaId, decimal donGia, decimal slAnSang, decimal slNhaTre, decimal slMauGiao)
    {
        TempData["LoiMau"] = "Trang Chi tiết ngày chỉ để xem — không được sửa/xóa nguyên liệu.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
#pragma warning disable CS0162

        await LuuMotHangHoaVaoPhieuGiaViAsync(coSoId, ngay, hangHoaId, donGia, slAnSang, slNhaTre, slMauGiao);
        TempData["ThongBao"] = "Đã lưu gia vị cho riêng ngày này.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    /// <summary>Nút "Xóa" ở 1 dòng mục "2. Gia vị hằng ngày" — đưa số lượng CỦA RIÊNG NGÀY
    /// NÀY về 0 cho đúng hàng hoá đó (ghi 1 dòng = 0 vào phiếu, KHÔNG phải bỏ hẳn khỏi danh
    /// sách — mục 2 luôn hiện đủ danh sách cố định — và cũng KHÔNG đụng định mức chung,
    /// nên những ngày khác chưa từng xóa vẫn tự tính bình thường theo định mức).</summary>
    [HttpPost("xoa-mot-gia-vi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XoaDongGiaVi(int coSoId, DateOnly ngay, int hangHoaId, decimal donGia)
    {
        TempData["LoiMau"] = "Trang Chi tiết ngày chỉ để xem — không được sửa/xóa nguyên liệu.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
#pragma warning disable CS0162

        await LuuMotHangHoaVaoPhieuGiaViAsync(coSoId, ngay, hangHoaId, donGia, 0, 0, 0);
        TempData["ThongBao"] = "Đã xóa số lượng mặt hàng này cho riêng ngày này.";
        return RedirectToAction(nameof(Index), new { coSoId, ngay });
    }

    /// <summary>Tạo phiếu "Gia vị hằng ngày" cho (cơ sở, ngày) nếu chưa có, rồi GHI ĐÈ 3
    /// dòng (Ăn sáng/Nhà trẻ/Mẫu giáo) của ĐÚNG 1 hàng hoá — các hàng hoá khác đã có trong
    /// phiếu (nếu có) giữ nguyên, không bị đụng tới.</summary>
    private async Task LuuMotHangHoaVaoPhieuGiaViAsync(int coSoId, DateOnly ngay, int hangHoaId, decimal donGia, decimal slAnSang, decimal slNhaTre, decimal slMauGiao)
    {
        var nhomTheoTen = await _db.NhomChiPhis.ToDictionaryAsync(n => n.TenNhom, n => n.Id);
        if (!nhomTheoTen.TryGetValue("Ăn sáng", out var nhomAnSangId)
            || !nhomTheoTen.TryGetValue("Nhà trẻ", out var nhomNhaTreId)
            || !nhomTheoTen.TryGetValue("Mẫu giáo", out var nhomMauGiaoId))
        {
            throw new InvalidOperationException("Thiếu nhóm chi phí — chạy đủ Database/01, 11 trước.");
        }

        var phieu = await _db.PhieuXuatKhos
            .Include(p => p.ChiTiets)
            .FirstOrDefaultAsync(p => p.CoSoId == coSoId && p.NgayXuat == ngay && p.LoaiXuat == LoaiXuatKho.GiaViHangNgay);

        if (phieu is null)
        {
            phieu = new PhieuXuatKho
            {
                CoSoId = coSoId,
                NgayXuat = ngay,
                LoaiXuat = LoaiXuatKho.GiaViHangNgay,
                NguoiLap = User.Identity?.Name,
            };
            _db.PhieuXuatKhos.Add(phieu);
        }

        // Chỉ xoá đúng các dòng CŨ CỦA HÀNG HOÁ NÀY — hàng hoá khác trong cùng phiếu giữ nguyên.
        var dongCu = phieu.ChiTiets.Where(c => c.HangHoaId == hangHoaId).ToList();
        _db.ChiTietXuatKhos.RemoveRange(dongCu);
        foreach (var d in dongCu) phieu.ChiTiets.Remove(d);

        // Luôn ghi đủ 3 dòng, KỂ CẢ bằng 0 — 1 dòng = 0 vẫn tính là "ngày này đã chỉnh tay
        // hàng hoá này", để định mức tự động không "hồi sinh" lại số cũ (xem LayGiaViThatAsync).
        phieu.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hangHoaId, NhomChiPhiId = nhomAnSangId, SoLuong = slAnSang, DonGia = donGia });
        phieu.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hangHoaId, NhomChiPhiId = nhomNhaTreId, SoLuong = slNhaTre, DonGia = donGia });
        phieu.ChiTiets.Add(new ChiTietXuatKho { HangHoaId = hangHoaId, NhomChiPhiId = nhomMauGiaoId, SoLuong = slMauGiao, DonGia = donGia });

        await _db.SaveChangesAsync();
    }
}
