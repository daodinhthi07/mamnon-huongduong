using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Services;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

[Authorize]
[Route("xuat-excel")]
public sealed class XuatExcelController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly ExcelExportService _excelExport;

    public XuatExcelController(ThucDonDbContext db, ExcelExportService excelExport)
    {
        _db = db;
        _excelExport = excelExport;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? coSoId, int? khoiId, DateOnly? ngay)
    {
        var vm = new XuatExcelViewModel
        {
            CoSoId = coSoId,
            KhoiId = khoiId,
            Ngay = ngay ?? DateOnly.FromDateTime(DateTime.Today),
            DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync(),
            DsKhoi = await _db.KhoiHocs.OrderBy(x => x.ThuTu).ToListAsync()
        };
        return View(vm);
    }

    [HttpGet("tai")]
    public async Task<IActionResult> Tai(int coSoId, int khoiId, DateOnly ngay)
    {
        // Dùng chung cách xuất của trang Lịch sử: bảng kê chợ cả tuần (mọi cơ sở), tên file TUẦN n THÁNG m X.
        try
        {
            var bytes = await _excelExport.XuatBangKeChoNgayAsync(ngay);
            var tenFile = LichSuThucDonViewModel.TenFileTuan(ngay);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", tenFile);
        }
        catch (ThieuBangGiaException ex)
        {
            if (LaAjax()) return StatusCode(422, new { ok = false, message = ex.Message, items = ex.Dong });
            TempData["ThongBao"] = ex.Message;
            return RedirectToAction(nameof(Index), new { coSoId, khoiId, ngay });
        }
    }

    /// <summary>Nút "Xuất Excel" ở trang Chi tiết ngày — 1 file/ngày gồm nhiều sheet: "Thực
    /// đơn" + "Chợ {Mã}" cho mỗi cơ sở active, đúng bố cục "BẢNG KÊ THỰC ĐƠN - CHI TIỀN
    /// CHỢ" gốc.</summary>
    [Authorize(Policy = ChinhSach.KhongPhaiQuanLy)]
    [HttpGet("bang-ke-cho")]
    public async Task<IActionResult> BangKeCho(DateOnly ngay)
    {
        try
        {
            var bytes = await _excelExport.XuatBangKeChoNgayAsync(ngay);
            var tenFile = LichSuThucDonViewModel.TenFileTuan(ngay);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", tenFile);
        }
        catch (ThieuBangGiaException ex)
        {
            if (LaAjax()) return StatusCode(422, new { ok = false, message = ex.Message, items = ex.Dong });
            TempData["ThongBao"] = ex.Message;
            return RedirectToAction(nameof(Index), new { ngay });
        }
    }

    /// <summary>Yêu cầu do site.js gửi bằng fetch (vòng quay chờ + hộp thông báo lỗi) -> trả JSON thay vì chuyển trang.</summary>
    private bool LaAjax() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
