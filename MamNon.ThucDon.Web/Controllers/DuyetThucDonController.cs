using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

[Authorize(Roles = $"{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("duyet-thuc-don")]
public sealed class DuyetThucDonController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public DuyetThucDonController(ThucDonDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var thucDons = await _db.ThucDons
            .Where(t => t.TrangThai == TrangThaiThucDon.ChoDuyet)
            .Include(t => t.CoSo)
            .Include(t => t.Khoi)
            .Include(t => t.NgayHoc)
            .Include(t => t.LoaiBuaAn)
            .Include(t => t.MonAn)
            .OrderBy(t => t.CoSoId).ThenBy(t => t.KhoiId).ThenBy(t => t.NgayHocId).ThenBy(t => t.LoaiBuaAn.ThuTu)
            .ToListAsync();

        var dsNhom = thucDons
            .GroupBy(t => (t.CoSoId, t.KhoiId, t.NgayHocId))
            .Select(g => new NhomThucDonChoDuyet
            {
                CoSoId = g.Key.CoSoId,
                KhoiId = g.Key.KhoiId,
                NgayHocId = g.Key.NgayHocId,
                TenCoSo = g.First().CoSo.TenCoSo,
                TenKhoi = g.First().Khoi.TenKhoi,
                Ngay = g.First().NgayHoc.Ngay,
                NguoiLap = g.First().NguoiLap,
                NgayLap = g.First().NgayLap,
                CacBua = g.Select(t => (t.LoaiBuaAn.TenBua, t.MonAn?.TenMon)).ToList()
            })
            .OrderBy(n => n.TenCoSo).ThenBy(n => n.Ngay)
            .ToList();

        return View(dsNhom);
    }

    [HttpPost("duyet")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duyet(int coSoId, int khoiId, int ngayHocId)
    {
        var nguoiDung = await _userManager.GetUserAsync(User);
        var tenNguoiDuyet = nguoiDung?.HoTen ?? nguoiDung?.UserName;

        var cacThucDon = await _db.ThucDons
            .Where(t => t.CoSoId == coSoId && t.KhoiId == khoiId && t.NgayHocId == ngayHocId)
            .ToListAsync();

        foreach (var td in cacThucDon)
        {
            td.TrangThai = TrangThaiThucDon.DaDuyet;
            td.NguoiDuyet = tenNguoiDuyet;
            td.NgayDuyet = DateOnly.FromDateTime(DateTime.Today);
            td.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        TempData["ThongBao"] = "Đã duyệt thực đơn.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("tu-choi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TuChoi(int coSoId, int khoiId, int ngayHocId)
    {
        var cacThucDon = await _db.ThucDons
            .Where(t => t.CoSoId == coSoId && t.KhoiId == khoiId && t.NgayHocId == ngayHocId)
            .ToListAsync();

        foreach (var td in cacThucDon)
        {
            td.TrangThai = TrangThaiThucDon.Nhap;
            td.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        TempData["ThongBao"] = "Đã trả về nháp.";
        return RedirectToAction(nameof(Index));
    }
}
