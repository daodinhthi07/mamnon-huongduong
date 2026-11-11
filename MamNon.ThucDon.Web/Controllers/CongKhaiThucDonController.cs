using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>Khu quản trị "CÔNG KHAI THỰC ĐƠN": nhân viên xem trước, bấm Công khai / Cập nhật công khai / Gỡ. Phải đăng nhập.</summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri},{VaiTro.QuanLy}")]
[Route("cong-khai-thuc-don")]
public sealed class CongKhaiThucDonController : Controller
{
    private readonly CongKhaiThucDonService _svc;
    private readonly UserManager<ApplicationUser> _userManager;

    public CongKhaiThucDonController(CongKhaiThucDonService svc, UserManager<ApplicationUser> userManager)
    {
        _svc = svc; _userManager = userManager;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? tuan)
    {
        var vm = await _svc.LayIndexAsync(tuan ?? DateOnly.FromDateTime(DateTime.Today));
        return View("~/Views/CongKhai/Index.cshtml", vm);
    }

    /// <summary>Dữ liệu xem trước (dựng từ thực đơn nội bộ) cho trang phụ huynh ở chế độ xem trước.</summary>
    [HttpGet("xem-truoc")]
    public async Task<IActionResult> XemTruoc(DateOnly tuan) => Json(await _svc.XemTruocAsync(tuan));

    /// <summary>Lưu Phụ sáng / Phụ chiều từng ngày (Thứ 2..Thứ 7) của tuần.</summary>
    [HttpPost("phu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuPhu(DateOnly tuan, string?[] sang, string?[] chieu)
    {
        await _svc.LuuPhuAsync(tuan, sang ?? Array.Empty<string?>(), chieu ?? Array.Empty<string?>());
        TempData["ThongBao"] = "Đã lưu Phụ sáng / Phụ chiều. Nhớ bấm \"Cập nhật công khai\" để phụ huynh thấy thay đổi.";
        return RedirectToAction(nameof(Index), new { tuan = tuan.ToString("yyyy-MM-dd") });
    }

    [HttpPost("cong-khai")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CongKhai(DateOnly tuan)
    {
        var u = await _userManager.GetUserAsync(User);
        var (ok, msg) = await _svc.CongKhaiAsync(tuan, u?.HoTen ?? u?.UserName);
        TempData["ThongBao"] = ok ? msg : "Không công khai được: " + msg;
        return RedirectToAction(nameof(Index), new { tuan = tuan.ToString("yyyy-MM-dd") });
    }

    [HttpPost("go")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Go(DateOnly tuan)
    {
        TempData["ThongBao"] = await _svc.GoCongKhaiAsync(tuan) ? "Đã gỡ công khai tuần này." : "Tuần này chưa được công khai.";
        return RedirectToAction(nameof(Index), new { tuan = tuan.ToString("yyyy-MM-dd") });
    }
}
