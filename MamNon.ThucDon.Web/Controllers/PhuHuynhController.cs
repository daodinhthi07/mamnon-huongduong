using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>TRANG CÔNG KHAI cho phụ huynh — link cố định, KHÔNG cần đăng nhập. Chỉ đọc bản chụp đã công khai
/// (bảng cong_khai_*), API chỉ trả tên trường / tuần / ngày / nhóm tuổi / tên bữa / tên món.</summary>
[AllowAnonymous]
[Route("thuc-don-phu-huynh")]
public sealed class PhuHuynhController : Controller
{
    private readonly CongKhaiThucDonService _svc;

    public PhuHuynhController(CongKhaiThucDonService svc) { _svc = svc; }

    [HttpGet("")]
    public IActionResult Index(DateOnly? xemTruoc)
    {
        // Xem trước chỉ dành cho nhân viên đã đăng nhập; phụ huynh có thêm tham số này cũng chỉ thấy bản đã công khai.
        var duocXemTruoc = xemTruoc is not null && User.Identity?.IsAuthenticated == true
            && (User.IsInRole(VaiTro.NhaBep) || User.IsInRole(VaiTro.HieuTruong) || User.IsInRole(VaiTro.QuanTri));
        ViewBag.TenTruong = _svc.TenTruong;
        ViewBag.ApiUrl = duocXemTruoc ? "/cong-khai-thuc-don/xem-truoc" : "/thuc-don-phu-huynh/du-lieu";
        ViewBag.XemTruoc = duocXemTruoc ? xemTruoc!.Value.ToString("yyyy-MM-dd") : "";
        return View();
    }

    [HttpGet("du-lieu")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DuLieu(DateOnly? tuan) => Json(await _svc.DocCongKhaiAsync(tuan));
}
