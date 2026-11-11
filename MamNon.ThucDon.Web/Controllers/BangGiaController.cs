using System.Text.Json;
using MamNon.ThucDon.Web.Identity;
using Microsoft.AspNetCore.Http;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Services;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// Bảng giá hàng hoá: xem / tìm, thêm mặt hàng, sửa trực tiếp tên + ĐVT + giá + ghi chú ngay trên bảng, tải bảng giá
/// xuống Excel và nhập từ Excel theo kiểu XÓA HẾT rồi nạp lại từ file (có bước xem trước trước khi áp dụng). Giá lưu ở hang_hoa.don_gia_gan_nhat — chính là giá mọi nơi đang dùng để tính tiền
/// (Chi tiết ngày, xuất Excel, ...). Phiếu kho đã lưu giữ nguyên giá của lúc lập phiếu.
/// </summary>
[Authorize(Policy = ChinhSach.KhongPhaiQuanLy)]
[Route("bang-gia")]
public sealed class BangGiaController : Controller
{
    // Mọi vai trò (kể cả Nhà bếp) đều được chỉnh sửa bảng giá: sửa trực tiếp, thêm mặt hàng, upload / nhập Excel.
    private const string VaiTroSua = $"{VaiTro.NhaBep},{VaiTro.ThuKho},{VaiTro.KeToan},{VaiTro.HieuTruong},{VaiTro.QuanTri}";

    private readonly ThucDonDbContext _db;

    public BangGiaController(ThucDonDbContext db)
    {
        _db = db;
    }

    private bool CoQuyenSua => User.IsInRole(VaiTro.NhaBep) || User.IsInRole(VaiTro.ThuKho) || User.IsInRole(VaiTro.KeToan)
                               || User.IsInRole(VaiTro.HieuTruong) || User.IsInRole(VaiTro.QuanTri);

    private static string TenLoai(LoaiHangHoa l) =>
        l == LoaiHangHoa.GiaVi ? "Gia vị" : l == LoaiHangHoa.Khac ? "Khác" : "Thực phẩm tươi";

    private static LoaiHangHoa LoaiTuChuoi(string? s) =>
        s == "Gia vị" ? LoaiHangHoa.GiaVi : s == "Khác" ? LoaiHangHoa.Khac : LoaiHangHoa.ThucPhamTuoi;

    private async Task<List<BangGiaRowVm>> LayTatCaAsync()
    {
        var ds = await _db.HangHoas.AsNoTracking().Include(h => h.Dvt).OrderBy(h => h.TenHang).ToListAsync();
        var ghiChus = await BangGiaGhiChu.LayTatCaAsync(_db);
        return ds.Select(h => new BangGiaRowVm
        {
            Id = h.Id,
            MaHang = h.MaHang,
            TenHang = h.TenHang,
            Dvt = h.Dvt?.TenDvt ?? string.Empty,
            LoaiHang = TenLoai(h.LoaiHang),
            DonGia = h.DonGiaGanNhat ?? 0m,
            GhiChu = ghiChus.GetValueOrDefault(h.Id),
            IsActive = h.IsActive,
        }).ToList();
    }

    private static string Gon(string? s) =>
        string.Join(" ", (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? GhiChuHoacNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ------------------------------------------------------------------ Danh sách
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? loai, bool hienAn = false)
    {
        var tatCa = await LayTatCaAsync();
        IEnumerable<BangGiaRowVm> ds = tatCa;
        if (!hienAn) ds = ds.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(loai)) ds = ds.Where(x => x.LoaiHang == loai);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var khoa = BangGiaExcel.BoDau(q);
            ds = ds.Where(x => BangGiaExcel.BoDau(x.TenHang).Contains(khoa)
                               || BangGiaExcel.BoDau(x.MaHang).Contains(khoa));
        }
        ViewData["TuKhoa"] = q ?? string.Empty;
        ViewData["Loai"] = loai ?? string.Empty;
        ViewData["HienAn"] = hienAn;
        ViewData["TongSo"] = tatCa.Count;
        ViewData["CoQuyenSua"] = CoQuyenSua;
        ViewData["DsDvt"] = await _db.DonViTinhs.AsNoTracking().OrderBy(d => d.TenDvt).Select(d => d.TenDvt).ToListAsync();
        return View(ds.ToList());
    }

    // ------------------------------------------------------------------ Thêm / sửa
    private async Task<BangGiaFormVm> TaoFormAsync(BangGiaFormVm? vm = null)
    {
        vm ??= new BangGiaFormVm();
        vm.DsDvt = await _db.DonViTinhs.AsNoTracking().OrderBy(d => d.TenDvt)
            .Select(d => new DvtOptionVm(d.Id, d.TenDvt)).ToListAsync();
        return vm;
    }

    [Authorize(Roles = VaiTroSua)]
    [HttpGet("them-moi")]
    public async Task<IActionResult> Create() => View("Form", await TaoFormAsync());

    [Authorize(Roles = VaiTroSua)]
    [HttpPost("them-moi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BangGiaFormVm model)
    {
        var dvtId = await KiemTraAsync(model, null);
        if (!ModelState.IsValid || dvtId is null) return View("Form", await TaoFormAsync(model));

        var moi = new HangHoa
        {
            MaHang = string.IsNullOrWhiteSpace(model.MaHang) ? null : model.MaHang.Trim(),
            TenHang = string.Join(" ", model.TenHang.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
            DvtId = dvtId.Value,
            LoaiHang = LoaiTuChuoi(model.LoaiHang),
            DonGiaGanNhat = model.DonGia,
            IsActive = model.IsActive,
        };
        _db.HangHoas.Add(moi);
        await _db.SaveChangesAsync();
        await BangGiaGhiChu.DatAsync(_db, moi.Id, model.GhiChu);
        TempData["ThongBao"] = $"Đã thêm mặt hàng \"{model.TenHang.Trim()}\" vào bảng giá.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = VaiTroSua)]
    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id)
    {
        var h = await _db.HangHoas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (h is null) return NotFound();
        return View("Form", await TaoFormAsync(new BangGiaFormVm
        {
            Id = h.Id,
            MaHang = h.MaHang,
            TenHang = h.TenHang,
            DvtId = h.DvtId,
            LoaiHang = TenLoai(h.LoaiHang),
            DonGia = h.DonGiaGanNhat ?? 0m,
            GhiChu = (await BangGiaGhiChu.LayTatCaAsync(_db)).GetValueOrDefault(h.Id),
            IsActive = h.IsActive,
        }));
    }

    [Authorize(Roles = VaiTroSua)]
    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BangGiaFormVm model)
    {
        var h = await _db.HangHoas.FirstOrDefaultAsync(x => x.Id == id);
        if (h is null) return NotFound();
        model.Id = id;
        var dvtId = await KiemTraAsync(model, id);
        if (!ModelState.IsValid || dvtId is null) return View("Form", await TaoFormAsync(model));

        h.MaHang = string.IsNullOrWhiteSpace(model.MaHang) ? null : model.MaHang.Trim();
        h.TenHang = string.Join(" ", model.TenHang.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        h.DvtId = dvtId.Value;
        h.LoaiHang = LoaiTuChuoi(model.LoaiHang);
        h.DonGiaGanNhat = model.DonGia;
        h.IsActive = model.IsActive;
        await _db.SaveChangesAsync();
        await BangGiaGhiChu.DatAsync(_db, h.Id, model.GhiChu);
        TempData["ThongBao"] = $"Đã lưu mặt hàng \"{h.TenHang}\".";
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ Sửa nhanh ngay trên bảng
    /// <summary>Lưu 1 dòng sửa trực tiếp trên bảng giá: tên hàng, ĐVT (gõ tên; chưa có thì tạo mới), đơn giá, ghi chú.</summary>
    [Authorize(Roles = VaiTroSua)]
    [HttpPost("{id:int}/luu-nhanh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuNhanh(int id, [FromForm] LuuNhanhVm m)
    {
        var h = await _db.HangHoas.Include(x => x.Dvt).FirstOrDefaultAsync(x => x.Id == id);
        if (h is null) return NotFound(new { ok = false, message = "Không tìm thấy mặt hàng (có thể đã bị xóa khi nhập Excel). Hãy tải lại trang." });

        var ten = Gon(m.TenHang);
        if (ten.Length == 0) return BadRequest(new { ok = false, message = "Tên hàng không được để trống." });
        var tenDvt = Gon(m.Dvt);
        if (tenDvt.Length == 0) return BadRequest(new { ok = false, message = "Cần nhập ĐVT." });

        var giaText = (m.DonGia ?? "").Trim().Replace(',', '.');
        if (giaText.Length == 0) giaText = "0";
        if (!decimal.TryParse(giaText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var gia) || gia < 0)
            return BadRequest(new { ok = false, message = "Giá không hợp lệ (phải là số, không âm)." });

        var khoa = BangGiaExcel.ChuanTen(ten);
        var cacTen = await _db.HangHoas.AsNoTracking().Select(x => new { x.Id, x.TenHang }).ToListAsync();
        if (cacTen.Any(x => x.Id != id && BangGiaExcel.ChuanTen(x.TenHang) == khoa))
            return BadRequest(new { ok = false, message = $"Đã có mặt hàng khác tên \"{ten}\"." });

        var khoaDvt = BangGiaExcel.ChuanTen(tenDvt);
        var cacDvt = await _db.DonViTinhs.ToListAsync();
        var dvt = cacDvt.FirstOrDefault(d => BangGiaExcel.ChuanTen(d.TenDvt) == khoaDvt);
        if (dvt is null)
        {
            dvt = new DonViTinh { TenDvt = tenDvt };
            _db.DonViTinhs.Add(dvt);
        }

        h.TenHang = ten;
        h.Dvt = dvt;
        h.DonGiaGanNhat = gia;
        await _db.SaveChangesAsync();
        var ghiChu = GhiChuHoacNull(m.GhiChu);
        await BangGiaGhiChu.DatAsync(_db, h.Id, ghiChu);
        return Json(new { ok = true, tenHang = h.TenHang, dvt = dvt.TenDvt, donGia = h.DonGiaGanNhat, ghiChu });
    }

    /// <summary>Kiểm tra form; trả về DvtId cần dùng (tạo ĐVT mới nếu người dùng nhập), hoặc null nếu có lỗi.</summary>
    private async Task<int?> KiemTraAsync(BangGiaFormVm m, int? idDangSua)
    {
        if (string.IsNullOrWhiteSpace(m.TenHang)) ModelState.AddModelError(string.Empty, "Cần nhập tên hàng.");
        if (m.DonGia < 0) ModelState.AddModelError(string.Empty, "Đơn giá không được âm.");

        if (!string.IsNullOrWhiteSpace(m.TenHang))
        {
            var khoa = BangGiaExcel.ChuanTen(m.TenHang);
            var cacTen = await _db.HangHoas.AsNoTracking().Select(h => new { h.Id, h.TenHang }).ToListAsync();
            if (cacTen.Any(h => h.Id != idDangSua && BangGiaExcel.ChuanTen(h.TenHang) == khoa))
                ModelState.AddModelError(string.Empty, $"Đã có mặt hàng tên \"{m.TenHang.Trim()}\" — vào danh sách để sửa thay vì thêm trùng.");
        }

        int? dvtId = null;
        var cacDvt = await _db.DonViTinhs.ToListAsync();
        if (!string.IsNullOrWhiteSpace(m.DvtMoi))
        {
            var khoaDvt = BangGiaExcel.ChuanTen(m.DvtMoi);
            var co = cacDvt.FirstOrDefault(d => BangGiaExcel.ChuanTen(d.TenDvt) == khoaDvt);
            if (co is not null) dvtId = co.Id;
            else if (ModelState.IsValid)
            {
                var moi = new DonViTinh { TenDvt = m.DvtMoi.Trim() };
                _db.DonViTinhs.Add(moi);
                await _db.SaveChangesAsync();
                dvtId = moi.Id;
            }
        }
        else if (m.DvtId is not null && cacDvt.Any(d => d.Id == m.DvtId))
        {
            dvtId = m.DvtId;
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Cần chọn đơn vị tính (hoặc nhập ĐVT mới).");
        }
        return dvtId;
    }

    // ------------------------------------------------------------------ Tải xuống
    [HttpGet("tai-xuong")]
    public async Task<IActionResult> TaiXuong()
    {
        var ds = (await LayTatCaAsync()).OrderBy(x => x.LoaiHang).ThenBy(x => x.TenHang);
        var bytes = BangGiaExcel.Xuat(ds);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"bang-gia-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ------------------------------------------------------------------ Nhập từ Excel (XÓA HẾT rồi nạp lại)
    [Authorize(Roles = VaiTroSua)]
    [HttpGet("nhap-excel")]
    public IActionResult NhapExcel() => View(new NhapExcelVm());

    private sealed class MucKeHoach
    {
        public DongXemTruocVm Vm { get; init; } = new();
        public DongBangGiaExcel Nguon { get; init; } = new();
        public HangHoa? Hang { get; init; }
    }

    private sealed class KeHoachNhap
    {
        public List<MucKeHoach> Muc { get; } = new();
        public List<HangHoa> SeXoa { get; } = new();
        public List<HangHoa> SeGiu { get; } = new();
    }

    /// <summary>Id các mặt hàng đang được dùng ở nơi khác (công thức món, định mức gia vị, số lượng theo Excel mẫu, phiếu kho)
    /// — những mặt hàng này KHÔNG được xóa cứng để không làm hỏng thực đơn / phiếu cũ.</summary>
    private async Task<HashSet<int>> LayIdDangDungAsync()
    {
        var ids = new HashSet<int>();
        var conn = _db.Database.GetDbConnection();
        var daMo = conn.State == System.Data.ConnectionState.Open;
        if (!daMo) await _db.Database.OpenConnectionAsync();
        try
        {
            foreach (var bang in new[] { "dinh_luong_nguyen_lieu", "dinh_muc_gia_vi_chuan", "so_luong_theo_excel", "chi_tiet_nhap_kho", "chi_tiet_xuat_kho" })
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT DISTINCT hang_hoa_id FROM {bang} WHERE hang_hoa_id IS NOT NULL";
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) ids.Add(Convert.ToInt32(rd.GetValue(0)));
            }
        }
        finally
        {
            if (!daMo) await _db.Database.CloseConnectionAsync();
        }
        return ids;
    }

    /// <summary>Lập kế hoạch nạp lại. Khớp mặt hàng theo TÊN (không phân biệt hoa thường/khoảng trắng thừa) để giữ nguyên Id,
    /// nhờ đó công thức món / phiếu cũ vẫn trỏ đúng. Mặt hàng trong file: cập nhật (hoặc thêm mới). Mặt hàng KHÔNG có trong file:
    /// xóa nếu không ai dùng; nếu đang được dùng thì giữ lại nhưng xóa giá + ghi chú.</summary>
    private async Task<KeHoachNhap> LapKeHoachAsync(List<DongBangGiaExcel> dong)
    {
        var hang = await _db.HangHoas.Include(h => h.Dvt).ToListAsync();
        var dangDung = await LayIdDangDungAsync();
        var ghiChuCu = await BangGiaGhiChu.LayTatCaAsync(_db);
        var theoTen = new Dictionary<string, HangHoa>();
        foreach (var h in hang) theoTen.TryAdd(BangGiaExcel.ChuanTen(h.TenHang), h);

        var kh = new KeHoachNhap();
        var daThay = new HashSet<string>();
        var khop = new HashSet<int>();
        foreach (var src in dong)
        {
            var vm = new DongXemTruocVm
            {
                SoDong = src.SoDong, TenHang = src.TenHang, MaHang = src.MaHang, Dvt = src.Dvt,
                GiaMoi = src.DonGia, Note = src.GhiChu,
            };
            var khoa = BangGiaExcel.ChuanTen(src.TenHang);
            if (!daThay.Add(khoa))
            {
                vm.HanhDong = "bo"; vm.GhiChu = "Trùng tên với dòng phía trên trong file — bỏ qua.";
                kh.Muc.Add(new MucKeHoach { Vm = vm, Nguon = src }); continue;
            }

            if (theoTen.TryGetValue(khoa, out var h))
            {
                khop.Add(h.Id);
                var giaCu = h.DonGiaGanNhat ?? 0m;
                vm.GiaCu = giaCu;
                vm.DvtCu = h.Dvt?.TenDvt;
                var coDvt = !string.IsNullOrWhiteSpace(src.Dvt);
                var dvtDoi = coDvt && BangGiaExcel.ChuanTen(src.Dvt) != BangGiaExcel.ChuanTen(h.Dvt?.TenDvt);
                if (!coDvt) vm.Dvt = h.Dvt?.TenDvt;
                var noteDoi = (GhiChuHoacNull(src.GhiChu) ?? "") != (GhiChuHoacNull(ghiChuCu.GetValueOrDefault(h.Id)) ?? "");
                vm.HanhDong = (giaCu == src.DonGia && !dvtDoi && !noteDoi) ? "khongdoi" : "capnhat";
                var luuY = new List<string>();
                if (dvtDoi)
                    luuY.Add($"ĐVT đổi từ \"{h.Dvt?.TenDvt}\" sang \"{src.Dvt}\"" + (dangDung.Contains(h.Id)
                        ? " — mặt hàng đang dùng trong công thức món, định lượng giữ nguyên con số nên hãy kiểm tra lại." : "."));
                if (vm.HanhDong == "capnhat" && giaCu > 0 && (src.DonGia > giaCu * 2m || src.DonGia < giaCu / 2m))
                    luuY.Add("⚠ Giá thay đổi rất lớn — kiểm tra lại có gõ thiếu/thừa số 0 không.");
                vm.GhiChu = luuY.Count == 0 ? null : string.Join(" ", luuY);
                kh.Muc.Add(new MucKeHoach { Vm = vm, Nguon = src, Hang = h });
                continue;
            }

            if (string.IsNullOrWhiteSpace(src.Dvt)) { vm.HanhDong = "bo"; vm.GhiChu = "Mặt hàng mới cần có ĐVT — bỏ qua."; }
            else vm.HanhDong = "them";
            kh.Muc.Add(new MucKeHoach { Vm = vm, Nguon = src });
        }

        foreach (var h in hang.Where(x => !khop.Contains(x.Id)))
            (dangDung.Contains(h.Id) ? kh.SeGiu : kh.SeXoa).Add(h);
        return kh;
    }

    [Authorize(Roles = VaiTroSua)]
    [HttpPost("nhap-excel")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> NhapExcel(IFormFile? file)
    {
        var vm = new NhapExcelVm();
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Chưa chọn file Excel.");
            return View(vm);
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Chỉ nhận file .xlsx (Excel 2007 trở lên).");
            return View(vm);
        }

        List<DongBangGiaExcel> dong; List<string> canhBaoDoc;
        try
        {
            await using var stream = file.OpenReadStream();
            (dong, canhBaoDoc) = BangGiaExcel.Doc(stream);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "Không đọc được file. Hãy kiểm tra đây là file Excel .xlsx hợp lệ (không đặt mật khẩu).");
            return View(vm);
        }

        var kh = await LapKeHoachAsync(dong);
        vm.CoXemTruoc = true;
        vm.TenFile = file.FileName;
        vm.Dong = kh.Muc.Select(k => k.Vm).ToList();
        vm.SeXoa = kh.SeXoa.Select(h => h.TenHang).OrderBy(x => x).ToList();
        vm.SeGiu = kh.SeGiu.Select(h => h.TenHang).OrderBy(x => x).ToList();
        vm.CanhBao = canhBaoDoc;
        vm.DuLieuJson = JsonSerializer.Serialize(dong);
        return View(vm);
    }

    // ------------------------------------------------------------------ Upload thẳng: xóa bảng giá cũ, lấy bảng giá mới
    /// <summary>Upload file Excel ngay trên trang Bảng giá: XÓA toàn bộ bảng giá cũ rồi nạp lại từ file (không qua bước xem trước;
    /// trình duyệt đã hỏi xác nhận trước khi gửi).</summary>
    // Ai đã đăng nhập cũng upload được (class đã có [Authorize]); các chức năng sửa khác vẫn theo vai trò.
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ThongBao"] = "Chưa chọn file Excel.";
            return RedirectToAction(nameof(Index));
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ThongBao"] = "Chỉ nhận file .xlsx (Excel 2007 trở lên). Bảng giá chưa bị thay đổi.";
            return RedirectToAction(nameof(Index));
        }

        List<DongBangGiaExcel> dong; List<string> canhBaoDoc;
        try
        {
            await using var stream = file.OpenReadStream();
            (dong, canhBaoDoc) = BangGiaExcel.Doc(stream);
        }
        catch (InvalidOperationException ex)
        {
            TempData["ThongBao"] = ex.Message + " Bảng giá chưa bị thay đổi.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception)
        {
            TempData["ThongBao"] = "Không đọc được file. Hãy kiểm tra đây là file Excel .xlsx hợp lệ (không đặt mật khẩu). Bảng giá chưa bị thay đổi.";
            return RedirectToAction(nameof(Index));
        }

        var kh = await LapKeHoachAsync(dong);
        var ketQua = await ApDungKeHoachAsync(kh);
        TempData["ThongBao"] = $"Đã upload \"{file.FileName}\": " + ketQua +
                               (canhBaoDoc.Count > 0 ? $" ({canhBaoDoc.Count} dòng trong file bị bỏ qua vì thiếu giá/giá không hợp lệ.)" : "");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Thực hiện kế hoạch: mặt hàng trong file -> cập nhật (khớp theo tên, giữ Id) hoặc thêm mới; không có trong file ->
    /// xóa nếu không ai dùng, còn đang dùng thì giữ lại nhưng chuyển sang "Ngưng dùng" và xóa giá + ghi chú (bảng giá hiển thị
    /// mặc định chỉ còn đúng các mặt hàng của file mới). Toàn bộ trong 1 transaction.</summary>
    private async Task<string> ApDungKeHoachAsync(KeHoachNhap kh)
    {
        var cacDvt = await _db.DonViTinhs.ToListAsync();
        DonViTinh TimHoacTaoDvt(string ten)
        {
            var khoaDvt = BangGiaExcel.ChuanTen(ten);
            var dvt = cacDvt.FirstOrDefault(d => BangGiaExcel.ChuanTen(d.TenDvt) == khoaDvt);
            if (dvt is null)
            {
                dvt = new DonViTinh { TenDvt = Gon(ten) };
                _db.DonViTinhs.Add(dvt);
                cacDvt.Add(dvt);
            }
            return dvt;
        }

        int soCapNhat = 0, soThem = 0;
        var ghiChuMoi = new List<(HangHoa Hang, string? Note)>();
        foreach (var k in kh.Muc)
        {
            if (k.Vm.HanhDong == "bo") continue;
            var src = k.Nguon;
            if (k.Hang is not null)
            {
                var h = k.Hang;
                h.MaHang = string.IsNullOrWhiteSpace(src.MaHang) ? null : src.MaHang.Trim();
                if (!string.IsNullOrWhiteSpace(src.Dvt)) h.Dvt = TimHoacTaoDvt(src.Dvt);
                h.LoaiHang = LoaiTuChuoi(src.LoaiHang);
                h.DonGiaGanNhat = src.DonGia;
                h.IsActive = true;
                ghiChuMoi.Add((h, GhiChuHoacNull(src.GhiChu)));
                soCapNhat++;
            }
            else
            {
                var moi = new HangHoa
                {
                    MaHang = string.IsNullOrWhiteSpace(src.MaHang) ? null : src.MaHang.Trim(),
                    TenHang = src.TenHang,
                    Dvt = TimHoacTaoDvt(src.Dvt!),
                    LoaiHang = LoaiTuChuoi(src.LoaiHang),
                    DonGiaGanNhat = src.DonGia,
                    IsActive = true,
                };
                _db.HangHoas.Add(moi);
                ghiChuMoi.Add((moi, GhiChuHoacNull(src.GhiChu)));
                soThem++;
            }
        }
        foreach (var h in kh.SeXoa) _db.HangHoas.Remove(h);
        foreach (var h in kh.SeGiu) { h.DonGiaGanNhat = null; h.IsActive = false; }

        await BangGiaGhiChu.DamBaoBangAsync(_db);
        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        await BangGiaGhiChu.XoaTatCaAsync(_db);   // xóa hết ghi chú cũ, chỉ giữ ghi chú của file mới
        foreach (var (hang, note) in ghiChuMoi)
            if (note is not null) await BangGiaGhiChu.DatAsync(_db, hang.Id, note);
        await tx.CommitAsync();

        var soGhiChu = ghiChuMoi.Count(x => x.Note is not null);
        return $"đã xóa bảng giá cũ và nạp bảng giá mới: {soCapNhat + soThem} mặt hàng ({soThem} mặt hàng mới, {soGhiChu} có ghi chú); " +
               $"{kh.SeXoa.Count} mặt hàng cũ không còn trong file đã bị xóa" +
               (kh.SeGiu.Count > 0 ? $"; {kh.SeGiu.Count} mặt hàng cũ đang dùng trong công thức món được chuyển sang \"Ngưng dùng\" (tick \"Hiện cả mặt hàng đã ngưng dùng\" để xem)." : ".");
    }

    // Nhập có XEM TRƯỚC (tuỳ chọn): trang Nhập Excel -> xem danh sách xóa/giữ/thêm -> Áp dụng.
    [Authorize(Roles = VaiTroSua)]
    [HttpPost("nhap-excel/ap-dung")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ApDungNhap(string? duLieuJson, bool xacNhan = false)
    {
        List<DongBangGiaExcel>? dong = null;
        try { dong = string.IsNullOrWhiteSpace(duLieuJson) ? null : JsonSerializer.Deserialize<List<DongBangGiaExcel>>(duLieuJson); }
        catch (JsonException) { }
        if (dong is null || dong.Count == 0)
        {
            TempData["ThongBao"] = "Không có dữ liệu để áp dụng — hãy chọn lại file Excel.";
            return RedirectToAction(nameof(NhapExcel));
        }
        if (!xacNhan)
        {
            TempData["ThongBao"] = "Chưa áp dụng: cần tick xác nhận \"xóa toàn bộ bảng giá hiện tại và nạp lại từ file\". Hãy chọn lại file.";
            return RedirectToAction(nameof(NhapExcel));
        }

        // Lập lại kế hoạch theo dữ liệu hiện tại ở thời điểm bấm Áp dụng (không tin kết quả xem trước).
        var kh = await LapKeHoachAsync(dong);
        TempData["ThongBao"] = "Bảng giá: " + (await ApDungKeHoachAsync(kh));
        return RedirectToAction(nameof(Index));
    }
}
