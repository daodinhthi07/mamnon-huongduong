using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Infrastructure.Initializers;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>Quản lý "Quy tắc nguyên liệu cố định": thêm / sửa / xóa số lượng cố định của 1 mặt hàng theo cơ sở + loại ngày,
/// không cần sửa code. Xem <see cref="QuyTacNguyenLieu"/>.</summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.ThuKho},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("quy-tac-nguyen-lieu")]
public sealed class QuyTacNguyenLieuController : Controller
{
    private readonly ThucDonDbContext _db;

    public QuyTacNguyenLieuController(ThucDonDbContext db) { _db = db; }

    private static string TenLoaiNgay(int v) => v == 1 ? "Thứ 2 – Thứ 6" : v == 2 ? "Thứ 7" : "Mọi ngày";

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var ds = await _db.QuyTacNguyenLieus.AsNoTracking().Include(q => q.HangHoa).ThenInclude(h => h.Dvt)
            .OrderBy(q => q.HangHoa.TenHang).ThenBy(q => q.MaCoSo).ThenBy(q => q.LoaiNgay).ToListAsync();
        QuyTacDongVm Map(QuyTacNguyenLieu q) => new()
        {
            Id = q.Id, TenHang = q.HangHoa.TenHang, Dvt = q.HangHoa.Dvt?.TenDvt,
            CoSo = string.IsNullOrWhiteSpace(q.MaCoSo) ? "Mọi cơ sở" : q.MaCoSo!,
            LoaiNgay = TenLoaiNgay(q.LoaiNgay), KhongDung = q.KhongDung,
            SlAnSang = q.SlAnSang, SlNhaTre = q.SlNhaTre, SlMauGiao = q.SlMauGiao, GhiChu = q.GhiChu, IsActive = q.IsActive,
        };
        return View(new QuyTacIndexVm
        {
            MoiNgay = ds.Where(q => q.CheDo == QuyTacNguyenLieu.MoiNgay).Select(Map).ToList(),
            KhiCo = ds.Where(q => q.CheDo == QuyTacNguyenLieu.KhiCoTrongThucDon).Select(Map).ToList(),
        });
    }

    private async Task<QuyTacFormVm> TaoFormAsync(QuyTacFormVm vm)
    {
        vm.DsHangHoa = await _db.HangHoas.Where(h => h.IsActive).Include(h => h.Dvt).OrderBy(h => h.TenHang).ToListAsync();
        vm.DsCoSo = await _db.CoSos.Where(c => c.IsActive).OrderBy(c => c.MaCoSo).ToListAsync();
        return vm;
    }

    [HttpGet("them")]
    public async Task<IActionResult> Them(int cheDo = 0) => View("Form", await TaoFormAsync(new QuyTacFormVm { CheDo = cheDo }));

    [HttpGet("sua/{id:int}")]
    public async Task<IActionResult> Sua(int id)
    {
        var q = await _db.QuyTacNguyenLieus.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return NotFound();
        return View("Form", await TaoFormAsync(new QuyTacFormVm
        {
            Id = q.Id, HangHoaId = q.HangHoaId, LoaiNgay = q.LoaiNgay, CheDo = q.CheDo,
            CoSoChon = (q.MaCoSo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            SlAnSang = q.SlAnSang, SlNhaTre = q.SlNhaTre, SlMauGiao = q.SlMauGiao, KhongDung = q.KhongDung, GhiChu = q.GhiChu, IsActive = q.IsActive,
        }));
    }

    [HttpPost("luu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Luu(QuyTacFormVm vm)
    {
        if (vm.HangHoaId <= 0 || !await _db.HangHoas.AnyAsync(h => h.Id == vm.HangHoaId))
            ModelState.AddModelError(nameof(vm.HangHoaId), "Chọn mặt hàng.");
        if (vm.SlAnSang < 0 || vm.SlNhaTre < 0 || vm.SlMauGiao < 0)
            ModelState.AddModelError(string.Empty, "Số lượng không được âm.");
        if (!ModelState.IsValid) return View("Form", await TaoFormAsync(vm));

        var q = vm.Id > 0 ? await _db.QuyTacNguyenLieus.FirstOrDefaultAsync(x => x.Id == vm.Id) : null;
        if (q is null) { q = new QuyTacNguyenLieu(); _db.QuyTacNguyenLieus.Add(q); }
        q.HangHoaId = vm.HangHoaId;
        q.MaCoSo = vm.CoSoChon.Count == 0 ? null : string.Join(",", vm.CoSoChon.Select(c => c.Trim()).Where(c => c.Length > 0));
        q.LoaiNgay = vm.LoaiNgay is 1 or 2 ? vm.LoaiNgay : 0;
        q.CheDo = vm.CheDo == QuyTacNguyenLieu.KhiCoTrongThucDon ? QuyTacNguyenLieu.KhiCoTrongThucDon : QuyTacNguyenLieu.MoiNgay;
        q.KhongDung = vm.KhongDung;
        q.SlAnSang = vm.KhongDung ? 0 : vm.SlAnSang;
        q.SlNhaTre = vm.KhongDung ? 0 : vm.SlNhaTre;
        q.SlMauGiao = vm.KhongDung ? 0 : vm.SlMauGiao;
        q.GhiChu = string.IsNullOrWhiteSpace(vm.GhiChu) ? null : vm.GhiChu.Trim();
        q.IsActive = vm.IsActive;
        await _db.SaveChangesAsync();
        await QuyTacCache.NapAsync(_db);
        TempData["ThongBao"] = "Đã lưu quy tắc nguyên liệu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("xoa/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Xoa(int id)
    {
        var q = await _db.QuyTacNguyenLieus.FirstOrDefaultAsync(x => x.Id == id);
        if (q is not null)
        {
            _db.QuyTacNguyenLieus.Remove(q);
            await _db.SaveChangesAsync();
            await QuyTacCache.NapAsync(_db);
            TempData["ThongBao"] = "Đã xóa quy tắc.";
        }
        return RedirectToAction(nameof(Index));
    }
}
