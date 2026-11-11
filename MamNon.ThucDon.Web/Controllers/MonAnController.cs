using System.Globalization;
using System.Text;
using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// Quản lý danh mục món ăn + công thức nguyên liệu (dinh_luong_nguyen_lieu).
/// Đây là mảnh còn thiếu để "Soạn thực đơn"/"Chi tiết ngày" tính được nguyên liệu
/// cho MÓN MỚI chưa từng xuất hiện trong dữ liệu lịch sử — trước đó chỉ có
/// công cụ dò ngược từ file cũ (Tools/RecipeDatabase), không có cách nhập
/// tay cho món hoàn toàn mới.
/// </summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("mon-an")]
public sealed class MonAnController : Controller
{
    private readonly ThucDonDbContext _db;

    public MonAnController(ThucDonDbContext db)
    {
        _db = db;
    }

    /// <summary>Bỏ dấu tiếng Việt + chữ thường để tìm kiếm không cần gõ dấu ("canh bi dao" tìm ra "Canh bí đao…").</summary>
    private static string BoDau(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var d = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q)
    {
        var ds = await _db.MonAns
            .Include(m => m.DinhLuongNguyenLieus)
            .OrderBy(m => m.TenMon)
            .Select(m => new MonAnRowVm
            {
                Id = m.Id,
                TenMon = m.TenMon,
                TenNhomBuaAn = m.NhomBuaAn != null ? m.NhomBuaAn.TenNhom : null,
                SoNguyenLieu = m.DinhLuongNguyenLieus.Count,
                GhiChu = m.GhiChu,
                IsActive = m.IsActive
            })
            .ToListAsync();

        // Tìm món theo tên hoặc loại món (không cần gõ dấu). Trang cũng có ô lọc tức thì ngay trên trình duyệt.
        var tongSo = ds.Count;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var khoa = BoDau(q);
            ds = ds.Where(m => BoDau(m.TenMon).Contains(khoa) || BoDau(m.TenNhomBuaAn).Contains(khoa)).ToList();
        }
        ViewData["TuKhoa"] = q ?? string.Empty;
        ViewData["TongSo"] = tongSo;

        return View(ds);
    }

    [HttpGet("them-moi")]
    public async Task<IActionResult> Create()
    {
        var vm = new MonAnFormVm
        {
            DsHangHoa = await LayDanhSachHangHoaAsync(),
            DsNhomBuaAn = await LayDanhSachNhomBuaAnAsync()
        };
        return View("Form", vm);
    }

    [HttpPost("them-moi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MonAnFormVm model)
    {
        var dongHopLe = model.CacDong.Where(d => d.HangHoaId is not null && d.SoLuong > 0).ToList();
        if (!ModelState.IsValid || !dongHopLe.Any())
        {
            if (!dongHopLe.Any())
                ModelState.AddModelError(string.Empty, "Cần ít nhất 1 dòng nguyên liệu hợp lệ.");
            model.DsHangHoa = await LayDanhSachHangHoaAsync();
            model.DsNhomBuaAn = await LayDanhSachNhomBuaAnAsync();
            return View("Form", model);
        }

        if (await _db.MonAns.AnyAsync(m => m.TenMon == model.TenMon))
        {
            ModelState.AddModelError(string.Empty, $"Món \"{model.TenMon}\" đã tồn tại — vào danh sách để sửa thay vì tạo trùng.");
            model.DsHangHoa = await LayDanhSachHangHoaAsync();
            model.DsNhomBuaAn = await LayDanhSachNhomBuaAnAsync();
            return View("Form", model);
        }

        var monAn = new MonAn { TenMon = model.TenMon.Trim(), GhiChu = model.GhiChu, NhomBuaAnId = model.NhomBuaAnId };
        foreach (var dong in dongHopLe)
        {
            monAn.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
            {
                HangHoaId = dong.HangHoaId!.Value,
                // Quy đổi: người dùng nhập số lượng cho SoSuatThamChieu suất -> lưu định lượng/1 suất
                DinhLuong = Math.Round(dong.SoLuong / model.SoSuatThamChieu, 6)
            });
        }

        _db.MonAns.Add(monAn);
        await _db.SaveChangesAsync();

        TempData["ThongBao"] = $"Đã tạo món \"{monAn.TenMon}\" với {dongHopLe.Count} nguyên liệu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id, string? coSo = null)
    {
        var monAn = await _db.MonAns
            .Include(m => m.DinhLuongNguyenLieus)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (monAn is null) return NotFound();

        var dsMa = await LayDanhSachMaCoSoAsync();
        var ma = ChuanHoaMaCoSo(coSo, dsMa);   // mặc định TC

        var vm = new MonAnFormVm
        {
            Id = monAn.Id,
            TenMon = monAn.TenMon,
            GhiChu = monAn.GhiChu,
            NhomBuaAnId = monAn.NhomBuaAnId,
            SoSuatThamChieu = 1, // Sửa thì nhập/hiển thị thẳng định lượng/suất, không quy đổi qua suất tham chiếu nữa
            MaCoSo = ma,
            DsMaCoSo = dsMa,
            MonChuan = LaMonChuan(monAn),
            DsHangHoa = await LayDanhSachHangHoaAsync(),
            DsNhomBuaAn = await LayDanhSachNhomBuaAnAsync()
        };

        var chung = monAn.DinhLuongNguyenLieus.OrderBy(d => d.Id)
            .Select(dl => new DongCongThucVm { HangHoaId = dl.HangHoaId, SoLuong = dl.DinhLuong, SoLuongNhaTre = dl.DinhLuongNhaTre })
            .ToList();

        if (LaCoSoChuan(ma))
        {
            vm.CacDong = chung;
        }
        else
        {
            var rieng = await _db.DinhLuongNguyenLieuTheoCoSos.AsNoTracking()
                .Where(x => x.MonAnId == id && x.MaCoSo == ma).OrderBy(x => x.Id).ToListAsync();
            if (rieng.Count > 0)
            {
                vm.CoCongThucRieng = true;
                vm.CacDong = rieng
                    .Select(x => new DongCongThucVm { HangHoaId = x.HangHoaId, SoLuong = x.DinhLuong, SoLuongNhaTre = x.DinhLuongNhaTre })
                    .ToList();
            }
            else
            {
                // Chợ này chưa có công thức riêng -> đang dùng công thức chung. Hiện sẵn công thức chung để nếu muốn tạo công thức riêng thì sửa từ đó.
                vm.CoCongThucRieng = false;
                vm.DungCongThucChung = true;
                vm.CacDong = chung;
            }
        }
        if (!vm.CacDong.Any()) vm.CacDong.Add(new DongCongThucVm());

        return View("Form", vm);
    }

    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MonAnFormVm model)
    {
        var monAn = await _db.MonAns.Include(m => m.DinhLuongNguyenLieus).FirstOrDefaultAsync(m => m.Id == id);
        if (monAn is null) return NotFound();

        var dsMa = await LayDanhSachMaCoSoAsync();
        var ma = ChuanHoaMaCoSo(model.MaCoSo, dsMa);
        var laMonChuan = LaMonChuan(monAn);   // theo dữ liệu trong DB, không tin giá trị form gửi lên
        var xoaRieng = !LaCoSoChuan(ma) && model.DungCongThucChung;
        var dongHopLe = (model.CacDong ?? new List<DongCongThucVm>())
            .Where(d => d.HangHoaId is not null && d.SoLuong > 0).ToList();

        if (laMonChuan && !model.XacNhanSuaMonChuan)
            ModelState.AddModelError(string.Empty, "Món này đang có ghi chú \"" + GhiChuMonChuan + "\" nên không được sửa. Tick ô xác nhận phía dưới nếu thật sự muốn sửa.");
        if (!xoaRieng && !dongHopLe.Any())
            ModelState.AddModelError(string.Empty, "Cần ít nhất 1 dòng nguyên liệu hợp lệ.");

        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.MaCoSo = ma;
            model.DsMaCoSo = dsMa;
            model.MonChuan = laMonChuan;
            if (model.CacDong is null || !model.CacDong.Any()) model.CacDong = new List<DongCongThucVm> { new DongCongThucVm() };
            model.DsHangHoa = await LayDanhSachHangHoaAsync();
            model.DsNhomBuaAn = await LayDanhSachNhomBuaAnAsync();
            return View("Form", model);
        }

        monAn.TenMon = model.TenMon.Trim();
        monAn.GhiChu = model.GhiChu;
        monAn.NhomBuaAnId = model.NhomBuaAnId;

        string thongBao;
        if (xoaRieng)
        {
            // PH / ĐT: bỏ công thức riêng -> từ nay chợ này dùng công thức chung (TC).
            var cu = await _db.DinhLuongNguyenLieuTheoCoSos.Where(x => x.MonAnId == id && x.MaCoSo == ma).ToListAsync();
            _db.DinhLuongNguyenLieuTheoCoSos.RemoveRange(cu);
            thongBao = $"Đã cập nhật món \"{monAn.TenMon}\": cơ sở {ma} dùng công thức chung (TC).";
        }
        else
        {
            var dong = GopDong(dongHopLe, model.SoSuatThamChieu);

            // Cơ sở đang sửa + các cơ sở được tick "Áp dụng công thức này cho cơ sở khác".
            var dich = new List<string> { ma };
            foreach (var c in model.ApDungChoCoSo ?? new List<string>())
            {
                var m2 = TimMaCoSo(c, dsMa);
                if (m2 is not null && !dich.Contains(m2, StringComparer.OrdinalIgnoreCase)) dich.Add(m2);
            }
            foreach (var d in dich) await GhiCongThucAsync(monAn, d, dong, ma);

            thongBao = dich.Count == 1
                ? $"Đã cập nhật món \"{monAn.TenMon}\" cho cơ sở {ma}."
                : $"Đã cập nhật món \"{monAn.TenMon}\" và áp dụng cho: {string.Join(", ", dich)}.";
        }

        await _db.SaveChangesAsync();
        TempData["ThongBao"] = thongBao;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/khoa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var monAn = await _db.MonAns.FindAsync(id);
        if (monAn is not null)
        {
            monAn.IsActive = !monAn.IsActive;
            if (!monAn.IsActive)
            {
                // Ẩn món: các thực đơn ĐÃ LƯU vẫn trỏ tới món này nên xuất Excel vẫn lấy công thức của món ẩn.
                // => chuyển sang món ĐANG DÙNG cùng nhóm bữa, cùng bộ từ (không phân biệt thứ tự/dấu phẩy).
                var khoa = KhoaTuKhoa(monAn.TenMon);
                var thayThe = (await _db.MonAns.Where(m => m.IsActive && m.Id != monAn.Id && m.NhomBuaAnId == monAn.NhomBuaAnId).ToListAsync())
                    .FirstOrDefault(m => KhoaTuKhoa(m.TenMon) == khoa);
                var dsTd = await _db.ThucDons.Where(t => t.MonAnId == monAn.Id).ToListAsync();
                if (thayThe is not null)
                {
                    foreach (var t in dsTd) t.MonAnId = thayThe.Id;
                    TempData["ThongBao"] = $"Đã ẩn \"{monAn.TenMon}\"; {dsTd.Count} ô thực đơn đã chuyển sang \"{thayThe.TenMon}\".";
                }
                else if (dsTd.Count > 0)
                    TempData["ThongBao"] = $"Đã ẩn \"{monAn.TenMon}\" nhưng còn {dsTd.Count} ô thực đơn đang dùng món này (không có món đang dùng trùng tên để thay). Hãy chọn lại món cho các ô đó.";
            }
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Khóa so khớp tên món: tách từ, bỏ dấu câu, sắp xếp — "Canh mướp, rau mồng tơi" = "Canh rau mồng tơi, mướp".</summary>
    private static string KhoaTuKhoa(string? ten) =>
        string.Join(' ', System.Text.RegularExpressions.Regex.Split((ten ?? "").ToLowerInvariant(), @"[\s,;.]+")
            .Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal));

    // ================= Công thức theo cơ sở (TC = bảng chung; PH / ĐT = bảng riêng từng chợ) =================

    private const string GhiChuMonChuan = "Món ăn nguyên liệu chuẩn";

    private static bool LaMonChuan(MonAn monAn) =>
        string.Equals((monAn.GhiChu ?? "").Trim(), GhiChuMonChuan, StringComparison.OrdinalIgnoreCase);

    private static bool LaCoSoChuan(string ma) => string.Equals(ma, "TC", StringComparison.OrdinalIgnoreCase);

    /// <summary>Danh sách mã cơ sở cho dropdown — TC luôn đứng đầu (mặc định).</summary>
    private async Task<List<string>> LayDanhSachMaCoSoAsync()
    {
        var ds = await _db.CoSos.AsNoTracking().OrderBy(c => c.Id).Select(c => c.MaCoSo).ToListAsync();
        ds = ds.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToList();
        if (!ds.Contains("TC", StringComparer.OrdinalIgnoreCase)) ds.Insert(0, "TC");
        return ds.OrderBy(x => LaCoSoChuan(x) ? 0 : 1).ToList();
    }

    /// <summary>Tìm mã cơ sở hợp lệ trong danh sách (không phân biệt hoa thường; "DT" = "ĐT"). Không có -> null.</summary>
    private static string? TimMaCoSo(string? ma, IReadOnlyList<string> ds)
    {
        var m = (ma ?? "").Trim();
        if (m.Equals("DT", StringComparison.OrdinalIgnoreCase)) m = "ĐT";
        return ds.FirstOrDefault(x => x.Equals(m, StringComparison.OrdinalIgnoreCase));
    }

    private static string ChuanHoaMaCoSo(string? ma, IReadOnlyList<string> ds) => TimMaCoSo(ma, ds) ?? "TC";

    private sealed record DongGop(int HangHoaId, decimal DinhLuong, decimal? NhaTre);

    /// <summary>Gộp các dòng trùng nguyên liệu, quy đổi về định lượng/1 suất. Nhà trẻ bằng Mẫu giáo -> null (dùng chung).</summary>
    private static List<DongGop> GopDong(IEnumerable<DongCongThucVm> dong, int soSuatThamChieu)
    {
        var chia = Math.Max(soSuatThamChieu, 1);
        var kq = new List<DongGop>();
        foreach (var g in dong.GroupBy(d => d.HangHoaId!.Value))
        {
            var mg = Math.Round(g.Sum(x => x.SoLuong) / chia, 6);
            decimal? nt = null;
            if (g.Any(x => x.SoLuongNhaTre.HasValue))
            {
                var ntGop = Math.Round(g.Sum(x => x.SoLuongNhaTre ?? x.SoLuong) / chia, 6);
                if (ntGop != mg) nt = ntGop;
            }
            kq.Add(new DongGop(g.Key, mg, nt));
        }
        return kq;
    }

    /// <summary>Ghi công thức <paramref name="dong"/> cho cơ sở <paramref name="ma"/>. SỬA TẠI CHỖ (giữ nguyên các dòng cũ cùng nguyên liệu)
    /// nên không làm mất cột Nhà trẻ / cờ nguyên liệu chính; chỉ thêm dòng mới, xóa dòng không còn trong công thức.
    /// TC -> bảng chung; PH / ĐT -> bảng riêng (thay TOÀN BỘ công thức riêng của chợ đó).</summary>
    private async Task GhiCongThucAsync(MonAn monAn, string ma, List<DongGop> dong, string nguon)
    {
        var conLai = dong.Select(d => d.HangHoaId).ToHashSet();

        if (LaCoSoChuan(ma))
        {
            foreach (var thua in monAn.DinhLuongNguyenLieus.Where(x => !conLai.Contains(x.HangHoaId)).ToList())
                _db.DinhLuongNguyenLieus.Remove(thua);
            foreach (var g in monAn.DinhLuongNguyenLieus.Where(x => conLai.Contains(x.HangHoaId)).GroupBy(x => x.HangHoaId).ToList())
                foreach (var trung in g.Skip(1).ToList()) _db.DinhLuongNguyenLieus.Remove(trung);

            foreach (var d in dong)
            {
                var co = monAn.DinhLuongNguyenLieus.FirstOrDefault(x => x.HangHoaId == d.HangHoaId);
                if (co is null)
                    monAn.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu { HangHoaId = d.HangHoaId, DinhLuong = d.DinhLuong, DinhLuongNhaTre = d.NhaTre });
                else
                {
                    co.DinhLuong = d.DinhLuong;
                    co.DinhLuongNhaTre = d.NhaTre;
                }
            }
            return;
        }

        var hienCo = await _db.DinhLuongNguyenLieuTheoCoSos.Where(x => x.MonAnId == monAn.Id && x.MaCoSo == ma).ToListAsync();
        foreach (var thua in hienCo.Where(x => !conLai.Contains(x.HangHoaId)).ToList())
            _db.DinhLuongNguyenLieuTheoCoSos.Remove(thua);
        foreach (var g in hienCo.Where(x => conLai.Contains(x.HangHoaId)).GroupBy(x => x.HangHoaId).ToList())
            foreach (var trung in g.Skip(1).ToList()) _db.DinhLuongNguyenLieuTheoCoSos.Remove(trung);

        var ghiChu = (string.Equals(nguon, ma, StringComparison.OrdinalIgnoreCase) ? "Sửa trên trang món ăn" : $"Sao chép từ {nguon}")
                     + $" ngày {DateTime.Now:dd/MM/yyyy}";
        foreach (var d in dong)
        {
            var co = hienCo.FirstOrDefault(x => x.HangHoaId == d.HangHoaId);
            if (co is null)
                _db.DinhLuongNguyenLieuTheoCoSos.Add(new DinhLuongNguyenLieuTheoCoSo
                {
                    MonAnId = monAn.Id, HangHoaId = d.HangHoaId, MaCoSo = ma,
                    DinhLuong = d.DinhLuong, DinhLuongNhaTre = d.NhaTre, GhiChu = ghiChu
                });
            else
            {
                co.DinhLuong = d.DinhLuong;
                co.DinhLuongNhaTre = d.NhaTre;
                co.GhiChu = ghiChu;
            }
        }
    }

    private async Task<List<HangHoa>> LayDanhSachHangHoaAsync() =>
        await _db.HangHoas.Where(h => h.IsActive).Include(h => h.Dvt).OrderBy(h => h.TenHang).ToListAsync();

    private async Task<List<NhomBuaAn>> LayDanhSachNhomBuaAnAsync() =>
        await _db.NhomBuaAns.OrderBy(n => n.ThuTu).ToListAsync();
}
