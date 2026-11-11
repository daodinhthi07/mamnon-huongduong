using System.Text.Json;
using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// TRANG CHỦ = Dashboard (địa chỉ "/", "/dashboard" tự chuyển về "/") — hiển thị thực đơn cả tuần dạng lưới (Thứ 2 -> Thứ 7 x các bữa),
/// tô màu theo khối (Mẫu giáo = xanh lá, Nhà trẻ = hồng), hiện ĐỒNG THỜI cả 2
/// khối, không cho chọn Cơ sở/Khối nữa — thực đơn dùng chung cho cả 3 cơ sở
/// (đã xác nhận bằng dữ liệu thật), chỉ cần chọn TUẦN. Cho chỉnh sửa TRỰC TIẾP
/// trên lưới — khác với /soan-thuc-don (chỉ soạn được từng ngày một lúc).
/// </summary>
// Mọi tài khoản đăng nhập đều xem được (đây là Trang chủ); chỉ Nhà bếp / Hiệu trưởng / Quản trị được lưu & đặt Thứ 7 nghỉ.
[Authorize]
[Route("dashboard")]
public sealed class DashboardController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MamNon.ThucDon.Web.Services.ExcelExportService _excelExport;
    private readonly MamNon.ThucDon.Web.Services.CongKhaiThucDonService _congKhai;

    public DashboardController(ThucDonDbContext db, UserManager<ApplicationUser> userManager, MamNon.ThucDon.Web.Services.ExcelExportService excelExport,
        MamNon.ThucDon.Web.Services.CongKhaiThucDonService congKhai)
    {
        _db = db;
        _userManager = userManager;
        _excelExport = excelExport;
        _congKhai = congKhai;
    }

    private const string VaiTroDuocSua = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri},{VaiTro.QuanLy}";

    /// <summary>Địa chỉ cũ /dashboard -> chuyển về Trang chủ "/".</summary>
    [HttpGet("")]
    public IActionResult DiaChiCu(DateOnly? tuanBatDau) => RedirectToActionPermanent(nameof(Index), new { tuanBatDau });

    [HttpGet("~/")]
    public async Task<IActionResult> Index(DateOnly? tuanBatDau)
    {
        var vm = new DashboardViewModel
        {
            TuanBatDau = tuanBatDau ?? ThuHaiDauTuan(DateOnly.FromDateTime(DateTime.Today)),
            DsMonAn = await _db.MonAns.Where(x => x.IsActive).OrderBy(x => x.TenMon).ToListAsync(),
        };

        var dsKhoi = await _db.KhoiHocs.OrderBy(x => x.ThuTu).ToListAsync();

        // Thực đơn dùng chung cho mọi cơ sở -> chỉ cần đọc dữ liệu từ 1 cơ sở
        // đại diện (cơ sở active đầu tiên theo tên) để hiển thị lên lưới.
        var coSoDaiDien = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).FirstOrDefaultAsync();

        // Ô tick "Thứ 7 nghỉ": tick sẵn nếu Thứ 7 của tuần này đang là ngày nghỉ.
        if (coSoDaiDien is not null)
        {
            var thuBay = vm.TuanBatDau.AddDays(5);
            vm.ThuBayNghi = await _db.NgayHocs.AnyAsync(n =>
                n.CoSoId == coSoDaiDien.Id && n.Ngay == thuBay && n.TrangThai != TrangThaiNgay.HocBinhThuong);
        }

        foreach (var khoi in dsKhoi)
        {
            var dsBua = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoi.Id).OrderBy(b => b.ThuTu).ToListAsync();
            var grid = new DashboardKhoiGridVm
            {
                KhoiId = khoi.Id,
                TenKhoi = khoi.TenKhoi,
                CacBua = dsBua.Select(b => new DashboardBuaRowVm { BuaId = b.Id, TenBua = b.TenBua, NhomBuaAnId = b.NhomBuaAnId }).ToList(),
            };

            if (coSoDaiDien is not null)
            {
                for (var i = 0; i < 6; i++)
                {
                    var ngay = vm.TuanBatDau.AddDays(i);
                    var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSoDaiDien.Id && n.Ngay == ngay);
                    if (ngayHoc is null) continue;

                    var thucDons = await _db.ThucDons
                        .Where(t => t.CoSoId == coSoDaiDien.Id && t.KhoiId == khoi.Id && t.NgayHocId == ngayHoc.Id)
                        .ToListAsync();

                    foreach (var td in thucDons)
                    {
                        var dong = grid.CacBua.FirstOrDefault(b => b.BuaId == td.LoaiBuaAnId);
                        if (dong is not null) dong.MonTheoNgay[i] = td.MonAnId;
                    }
                }
            }

            vm.CacKhoi.Add(grid);
        }

        // Phụ sáng / Phụ chiều (bảng riêng cong_khai_phu, dùng chung 2 khối) — hiện trên bảng giống trang phụ huynh.
        var phu = await _congKhai.LayPhuAsync(vm.TuanBatDau);
        for (var i = 0; i < 6; i++)
        {
            if (!phu.TryGetValue(i, out var p)) continue;
            vm.PhuDaLuu[i] = true;
            vm.PhuSang[i] = p.Sang;
            vm.PhuChieu[i] = p.Chieu;
        }
        vm.PhuChieuMacDinh = _congKhai.PhuChieuMacDinh;
        vm.DsPhuSang = await _db.MonAns.AsNoTracking().Where(m => m.IsActive && m.NhomBuaAn != null && m.NhomBuaAn.TenNhom == "Phụ sáng").OrderBy(m => m.TenMon).Select(m => m.TenMon).ToListAsync();
        vm.DsPhuChieu = await _db.MonAns.AsNoTracking().Where(m => m.IsActive && m.NhomBuaAn != null && m.NhomBuaAn.TenNhom == "Phụ chiều").OrderBy(m => m.TenMon).Select(m => m.TenMon).ToListAsync();

        var idDangDung = vm.CacKhoi.SelectMany(k => k.CacBua).SelectMany(b => b.MonTheoNgay).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (idDangDung.Count > 0)
            vm.MonDangDung = await _db.MonAns.AsNoTracking().Where(m => idDangDung.Contains(m.Id)).ToListAsync();

        return View(vm);
    }

    /// <summary>Tick / bỏ tick "Thứ 7 nghỉ": đặt Thứ 7 của tuần (cho MỌI cơ sở) là ngày nghỉ hoặc học bình thường.
    /// Không xóa món đã chọn — bỏ tick là thực đơn Thứ 7 hiện lại. Excel xuất ra ghi Thứ 7 là ngày nghỉ.</summary>
    [HttpPost("thu-bay-nghi")]
    [Authorize(Roles = VaiTroDuocSua)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ThuBayNghi(DateOnly tuanBatDau, bool nghi)
    {
        if (tuanBatDau.DayOfWeek != DayOfWeek.Monday)
            return BadRequest(new { ok = false, message = "Tuần phải bắt đầu từ Thứ 2." });

        var thuBay = tuanBatDau.AddDays(5);
        var dsCoSo = await _db.CoSos.Where(x => x.IsActive).ToListAsync();
        foreach (var coSo in dsCoSo)
        {
            var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == thuBay);
            if (ngayHoc is null)
            {
                if (!nghi) continue;   // bỏ nghỉ mà chưa có bản ghi ngày -> không cần tạo
                ngayHoc = new NgayHoc { CoSoId = coSo.Id, Ngay = thuBay };
                _db.NgayHocs.Add(ngayHoc);
            }
            ngayHoc.TrangThai = nghi ? TrangThaiNgay.NghiKhac : TrangThaiNgay.HocBinhThuong;
            ngayHoc.GhiChu = nghi ? "Nghỉ thứ bảy" : null;
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true, nghi });
    }

    [HttpPost("luu")]
    [Authorize(Roles = VaiTroDuocSua)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Luu(DateOnly tuanBatDau, string luoiJson, bool guiDuyet, bool xuatExcel = false, string? moChiTietNgay = null,
        bool congKhai = false, string? cacNgay = null, string? phuJson = null)
    {
        // Vai trò Quản lý không được vào Chi tiết - Chi tiền chợ / bảng kê chợ -> bỏ 2 hành động đó nếu bị gọi trực tiếp.
        if (User.IsInRole(VaiTro.QuanLy)) { xuatExcel = false; moChiTietNgay = null; }

        // cacNgay: "0,2,3" (0 = Thứ 2 ... 5 = Thứ 7). Rỗng = cả tuần. Có giá trị = CHỈ lưu (và công khai) các ngày đó.
        HashSet<int>? ngayChon = null;
        if (!string.IsNullOrWhiteSpace(cacNgay))
        {
            ngayChon = cacNgay.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? n : -1).Where(n => n is >= 0 and <= 5).ToHashSet();
            if (ngayChon.Count == 0 || ngayChon.Count == 6) ngayChon = null;
        }

        // luoiJson: { "khoiId": { "0": {"buaId": monAnId|null, ...}, ..., "5": {...} }, "khoiId2": {...} }
        var luoi = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, int?>>>>(luoiJson) ?? new();
        var nguoiDung = await _userManager.GetUserAsync(User);
        var nguoiLap = nguoiDung?.HoTen ?? nguoiDung?.UserName;
        var trangThaiMoi = guiDuyet ? TrangThaiThucDon.ChoDuyet : TrangThaiThucDon.Nhap;

        // Thực đơn dùng chung cho mọi cơ sở -> ghi GIỐNG HỆT cho tất cả cơ sở active.
        var dsCoSo = await _db.CoSos.Where(x => x.IsActive).ToListAsync();

        foreach (var (khoiIdStr, monTheoNgay) in luoi)
        {
            if (!int.TryParse(khoiIdStr, out var khoiId)) continue;
            var dsBua = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiId).ToListAsync();

            for (var i = 0; i < 6; i++)
            {
                if (ngayChon is not null && !ngayChon.Contains(i)) continue;
                if (!monTheoNgay.TryGetValue(i.ToString(), out var monTrongNgay)) continue;
                var ngay = tuanBatDau.AddDays(i);

                foreach (var coSo in dsCoSo)
                {
                    var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == ngay);
                    // Thứ 7 đang đặt là ngày nghỉ -> không ghi món cho Thứ 7 (món cũ, nếu có, được giữ nguyên).
                    if (i == 5 && ngayHoc is not null && ngayHoc.TrangThai != TrangThaiNgay.HocBinhThuong) continue;
                    if (ngayHoc is null)
                    {
                        ngayHoc = new NgayHoc { CoSoId = coSo.Id, Ngay = ngay, TrangThai = TrangThaiNgay.HocBinhThuong };
                        _db.NgayHocs.Add(ngayHoc);
                        await _db.SaveChangesAsync();
                    }

                    foreach (var bua in dsBua)
                    {
                        var monId = monTrongNgay.TryGetValue(bua.Id.ToString(), out var m) ? m : null;

                        var thucDon = await _db.ThucDons.FirstOrDefaultAsync(t =>
                            t.CoSoId == coSo.Id && t.KhoiId == khoiId && t.NgayHocId == ngayHoc.Id && t.LoaiBuaAnId == bua.Id);

                        if (thucDon is null)
                        {
                            _db.ThucDons.Add(new ThucDonEntity
                            {
                                CoSoId = coSo.Id,
                                KhoiId = khoiId,
                                NgayHocId = ngayHoc.Id,
                                LoaiBuaAnId = bua.Id,
                                MonAnId = monId,
                                TrangThai = trangThaiMoi,
                                NguoiLap = nguoiLap,
                                NgayLap = DateOnly.FromDateTime(DateTime.Today)
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
            }
        }

        try
        {
            await _db.SaveChangesAsync();
            // Số suất ăn (+ tiền ăn 1 cháu) của tuần mới lấy theo tuần gần nhất có dữ liệu — chỉ điền ngày CHƯA có số, không ghi đè.
            await SaoSoSuatTuTuanGanNhatAsync(tuanBatDau, dsCoSo);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Hiện rõ nguyên nhân thay vì trang lỗi chung.
            var chiTiet = ex.InnerException?.Message ?? ex.Message;
            if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return StatusCode(500, new { ok = false, message = "Không lưu được thực đơn: " + chiTiet });
            TempData["ThongBao"] = "Không lưu được thực đơn: " + chiTiet;
            return RedirectToAction(nameof(Index), new { tuanBatDau });
        }

        // Phụ sáng / Phụ chiều đã sửa trên Trang chủ (chỉ các ngày bị đổi, và nằm trong phạm vi lưu) — ghi TRƯỚC khi công khai.
        if (!string.IsNullOrWhiteSpace(phuJson))
        {
            try
            {
                var phuMoi = JsonSerializer.Deserialize<Dictionary<string, PhuNgayDto>>(phuJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                var theoNgay = new Dictionary<int, (string, string)>();
                foreach (var (k, v) in phuMoi)
                {
                    if (!int.TryParse(k, out var i) || i < 0 || i > 5) continue;
                    if (ngayChon is not null && !ngayChon.Contains(i)) continue;
                    theoNgay[i] = (v.Sang ?? "", v.Chieu ?? "");
                }
                if (theoNgay.Count > 0) await _congKhai.LuuPhuTheoNgayAsync(tuanBatDau, theoNgay);
            }
            catch (JsonException) { /* dữ liệu phụ hỏng -> bỏ qua, món chính đã lưu */ }
        }

        // "Lưu & công khai": sau khi lưu thành công thì chụp thực đơn vừa lưu vào bản công khai (cả tuần hoặc chỉ các ngày đã chọn).
        string? thongBaoCongKhai = null;
        if (congKhai)
        {
            var (ok, msg) = await _congKhai.CongKhaiAsync(tuanBatDau, nguoiLap, ngayChon);
            thongBaoCongKhai = ok ? msg : "Đã lưu thực đơn nhưng chưa công khai được: " + msg;
        }

        // Nút "Xuất Excel": lưu lưới rồi xuất đúng như trang Lịch sử (bảng kê chợ cả tuần từ hệ thống).
        if (xuatExcel)
        {
            try
            {
                var bytes = await _excelExport.XuatBangKeChoNgayAsync(tuanBatDau);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", LichSuThucDonViewModel.TenFileTuan(tuanBatDau));
            }
        catch (MamNon.ThucDon.Web.Services.ThieuBangGiaException ex)
        {
            if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                return StatusCode(422, new { ok = false, message = ex.Message, items = ex.Dong });
            TempData["ThongBao"] = ex.Message;
            return RedirectToAction(nameof(Index), new { tuanBatDau });
        }
        }
        // Nút "Chi tiết": lưu lưới rồi mở trang Chi tiết ngày.
        if (!string.IsNullOrWhiteSpace(moChiTietNgay) && DateOnly.TryParse(moChiTietNgay, out var ngayMo))
            return RedirectToAction("Index", "ChiTietNgay", new { ngay = ngayMo.ToString("yyyy-MM-dd") });

        var phamViLuu = ngayChon is null ? "cả tuần" : $"từ {tuanBatDau.AddDays(ngayChon.Min()):dd/MM} đến hết {tuanBatDau.AddDays(ngayChon.Max()):dd/MM}";
        TempData["ThongBao"] = thongBaoCongKhai is not null
            ? $"Đã lưu thực đơn {phamViLuu} (áp dụng cho mọi cơ sở). " + thongBaoCongKhai
            : guiDuyet ? "Đã gửi duyệt cả tuần (áp dụng cho mọi cơ sở)." : "Đã lưu nháp cả tuần (áp dụng cho mọi cơ sở).";
        return RedirectToAction(nameof(Index), new { tuanBatDau });
    }

    /// <summary>Với mỗi cơ sở: tuần mới chưa có số suất ăn (bảng dinh_muc_an_ngay) ở ngày nào thì sao chép từ cùng THỨ của tuần gần nhất
    /// phía trước có số suất (đủ các nhóm Ăn sáng / Nhà trẻ / Mẫu giáo + tiền ăn 1 cháu). Ngày đã có số suất thì giữ nguyên.
    /// Thứ 7 đang đặt là ngày nghỉ thì bỏ qua.</summary>
    private async Task SaoSoSuatTuTuanGanNhatAsync(DateOnly tuanBatDau, List<CoSo> dsCoSo)
    {
        foreach (var coSo in dsCoSo)
        {
            var ngayCuaTuan = Enumerable.Range(0, 6).Select(i => tuanBatDau.AddDays(i)).ToList();
            var daCo = (await _db.DinhMucAnNgays
                    .Where(d => d.CoSoId == coSo.Id && d.Ngay >= tuanBatDau && d.Ngay < tuanBatDau.AddDays(7))
                    .Select(d => d.Ngay).Distinct().ToListAsync()).ToHashSet();
            if (daCo.Count == ngayCuaTuan.Count) continue;

            var ngayGanNhat = await _db.DinhMucAnNgays.Where(d => d.CoSoId == coSo.Id && d.Ngay < tuanBatDau)
                .OrderByDescending(d => d.Ngay).Select(d => (DateOnly?)d.Ngay).FirstOrDefaultAsync();
            if (ngayGanNhat is null) continue;
            var thuHaiNguon = ThuHaiDauTuan(ngayGanNhat.Value);
            var nguon = await _db.DinhMucAnNgays
                .Where(d => d.CoSoId == coSo.Id && d.Ngay >= thuHaiNguon && d.Ngay < thuHaiNguon.AddDays(7))
                .AsNoTracking().ToListAsync();

            for (var i = 0; i < 6; i++)
            {
                var ngay = tuanBatDau.AddDays(i);
                if (daCo.Contains(ngay)) continue;
                var ngayHoc = await _db.NgayHocs.AsNoTracking().FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == ngay);
                if (i == 5 && ngayHoc is not null && ngayHoc.TrangThai != TrangThaiNgay.HocBinhThuong) continue;
                foreach (var d in nguon.Where(x => x.Ngay == thuHaiNguon.AddDays(i)))
                {
                    _db.DinhMucAnNgays.Add(new DinhMucAnNgay
                    {
                        CoSoId = coSo.Id,
                        NhomChiPhiId = d.NhomChiPhiId,
                        Ngay = ngay,
                        TienAn1Chau = d.TienAn1Chau,
                        SoSuatAn = d.SoSuatAn,
                    });
                }
            }
        }
    }

    private static DateOnly ThuHaiDauTuan(DateOnly ngay)
    {
        var soNgayLuiVeThuHai = ((int)ngay.DayOfWeek + 6) % 7; // Chủ nhật=0 -> lùi 6, Thứ 2=1 -> lùi 0...
        return ngay.AddDays(-soNgayLuiVeThuHai);
    }
}

/// <summary>Phụ sáng / Phụ chiều của 1 ngày gửi từ Trang chủ.</summary>
public sealed record PhuNgayDto(string? Sang, string? Chieu);
