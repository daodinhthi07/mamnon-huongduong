using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.ThuKho},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("kho")]
public sealed class KhoController : Controller
{
    private readonly ThucDonDbContext _db;

    public KhoController(ThucDonDbContext db)
    {
        _db = db;
    }

    // -------------------- NHẬP KHO --------------------

    [HttpGet("nhap-kho")]
    public async Task<IActionResult> NhapKho()
    {
        var vm = new NhapKhoViewModel
        {
            DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync(),
            DsHangHoa = await _db.HangHoas.Where(x => x.IsActive).Include(x => x.Dvt).OrderBy(x => x.TenHang).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost("nhap-kho")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NhapKho(NhapKhoViewModel model)
    {
        var dongHopLe = model.CacDong.Where(d => d.HangHoaId is not null && d.SoLuong > 0).ToList();
        if (model.CoSoId is null || !dongHopLe.Any())
        {
            ModelState.AddModelError(string.Empty, "Vui lòng chọn cơ sở và ít nhất 1 dòng hàng hợp lệ.");
            model.DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync();
            model.DsHangHoa = await _db.HangHoas.Where(x => x.IsActive).Include(x => x.Dvt).OrderBy(x => x.TenHang).ToListAsync();
            return View(model);
        }

        var phieu = new PhieuNhapKho
        {
            CoSoId = model.CoSoId.Value,
            NgayNhap = model.NgayNhap,
            NguoiLap = string.IsNullOrWhiteSpace(model.NguoiLap) ? null : model.NguoiLap
        };

        foreach (var dong in dongHopLe)
        {
            phieu.ChiTiets.Add(new ChiTietNhapKho
            {
                HangHoaId = dong.HangHoaId!.Value,
                SoLuong = dong.SoLuong,
                DonGia = dong.DonGia
            });

            var hangHoa = await _db.HangHoas.FindAsync(dong.HangHoaId!.Value);
            if (hangHoa is not null)
            {
                hangHoa.DonGiaGanNhat = dong.DonGia;
            }
        }

        _db.PhieuNhapKhos.Add(phieu);
        await _db.SaveChangesAsync();

        TempData["ThongBao"] = "Đã lưu phiếu nhập kho.";
        return RedirectToAction(nameof(NhapKho));
    }

    // -------------------- XUẤT KHO --------------------

    [HttpGet("xuat-kho")]
    public async Task<IActionResult> XuatKho()
    {
        var vm = new XuatKhoViewModel
        {
            DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync(),
            DsHangHoa = await _db.HangHoas.Where(x => x.IsActive).Include(x => x.Dvt).OrderBy(x => x.TenHang).ToListAsync(),
            DsNhomChiPhi = await _db.NhomChiPhis.ToListAsync()
        };
        return View(vm);
    }

    [HttpPost("xuat-kho")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> XuatKho(XuatKhoViewModel model)
    {
        var dongHopLe = model.CacDong.Where(d => d.HangHoaId is not null && d.SoLuong > 0).ToList();
        if (model.CoSoId is null || !dongHopLe.Any())
        {
            ModelState.AddModelError(string.Empty, "Vui lòng chọn cơ sở và ít nhất 1 dòng hàng hợp lệ.");
            model.DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync();
            model.DsHangHoa = await _db.HangHoas.Where(x => x.IsActive).Include(x => x.Dvt).OrderBy(x => x.TenHang).ToListAsync();
            model.DsNhomChiPhi = await _db.NhomChiPhis.ToListAsync();
            return View(model);
        }

        var phieu = new PhieuXuatKho
        {
            CoSoId = model.CoSoId.Value,
            NgayXuat = model.NgayXuat,
            LoaiXuat = model.LoaiXuat,
            NguoiLap = string.IsNullOrWhiteSpace(model.NguoiLap) ? null : model.NguoiLap
        };

        foreach (var dong in dongHopLe)
        {
            phieu.ChiTiets.Add(new ChiTietXuatKho
            {
                HangHoaId = dong.HangHoaId!.Value,
                NhomChiPhiId = dong.NhomChiPhiId,
                SoLuong = dong.SoLuong,
                DonGia = dong.DonGia
            });

            var hangHoa = await _db.HangHoas.FindAsync(dong.HangHoaId!.Value);
            if (hangHoa is not null)
            {
                hangHoa.DonGiaGanNhat = dong.DonGia;
            }
        }

        _db.PhieuXuatKhos.Add(phieu);
        await _db.SaveChangesAsync();

        TempData["ThongBao"] = "Đã lưu phiếu xuất kho.";
        return RedirectToAction(nameof(XuatKho));
    }

    /// <summary>AJAX: đối chiếu ngân sách ngày cho 1 cơ sở/ngày — dùng ở trang Xuất kho.</summary>
    [HttpGet("dinh-muc-ngay")]
    public async Task<IActionResult> DinhMucNgay(int coSoId, DateOnly ngay)
    {
        var ds = await _db.DinhMucAnNgays
            .Include(d => d.NhomChiPhi)
            .Where(d => d.CoSoId == coSoId && d.Ngay == ngay)
            .Select(d => new { nhomChiPhiId = d.NhomChiPhiId, tenNhom = d.NhomChiPhi.TenNhom, soTienDuocPhepChi = d.TienDuocPhepChi })
            .ToListAsync();

        return Json(ds);
    }

    // -------------------- ĐỊNH MỨC GIA VỊ HẰNG NGÀY (cố định cho mọi ngày) --------------------

    /// <summary>Định lượng/suất cố định, áp dụng cho MỌI ngày chưa lập phiếu xuất kho
    /// "Gia vị hằng ngày" thật — xem ChiTietNgayTinhToanService.LayGiaViThatAsync. Liệt kê
    /// TẤT CẢ hàng hoá đang phân loại 'Gia vị' (kể cả hàng chưa có định mức, để tiện thêm
    /// mới ngay tại đây, không cần trang riêng để "thêm dòng").</summary>
    [HttpGet("dinh-muc-gia-vi")]
    public async Task<IActionResult> DinhMucGiaVi()
    {
        var vm = await TaoDinhMucGiaViViewModelAsync();
        return View(vm);
    }

    [HttpPost("dinh-muc-gia-vi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DinhMucGiaVi(List<DongDinhMucGiaViVm> cacDong)
    {
        var nhomTheoTen = await _db.NhomChiPhis.ToDictionaryAsync(n => n.TenNhom, n => n.Id);
        var hienCo = await _db.DinhMucGiaViChuans.ToDictionaryAsync(d => (d.HangHoaId, d.NhomChiPhiId));

        void Upsert(int hangHoaId, string tenNhom, decimal giaTri)
        {
            if (!nhomTheoTen.TryGetValue(tenNhom, out var nhomId)) return;
            if (hienCo.TryGetValue((hangHoaId, nhomId), out var dong))
            {
                dong.DinhLuong1Suat = giaTri;
            }
            else
            {
                _db.DinhMucGiaViChuans.Add(new DinhMucGiaViChuan { HangHoaId = hangHoaId, NhomChiPhiId = nhomId, DinhLuong1Suat = giaTri });
            }
        }

        foreach (var dong in cacDong)
        {
            Upsert(dong.HangHoaId, "Ăn sáng", dong.AnSang);
            Upsert(dong.HangHoaId, "Ăn sáng - Nhà trẻ", dong.AnSangNhaTre);
            Upsert(dong.HangHoaId, "Nhà trẻ", dong.NhaTre);
            Upsert(dong.HangHoaId, "Mẫu giáo", dong.MauGiao);
        }

        await _db.SaveChangesAsync();
        TempData["ThongBao"] = "Đã lưu định mức gia vị hằng ngày.";
        return RedirectToAction(nameof(DinhMucGiaVi));
    }

    private async Task<DinhMucGiaViViewModel> TaoDinhMucGiaViViewModelAsync()
    {
        var dsHangHoa = await _db.HangHoas
            .Where(h => h.LoaiHang == LoaiHangHoa.GiaVi)
            .Include(h => h.Dvt)
            .OrderBy(h => h.TenHang)
            .ToListAsync();

        var dinhMuc = await _db.DinhMucGiaViChuans.Include(d => d.NhomChiPhi).ToListAsync();
        var tra = dinhMuc.ToLookup(d => d.HangHoaId);

        decimal Lay(IEnumerable<DinhMucGiaViChuan> ds, string tenNhom)
            => ds.FirstOrDefault(d => d.NhomChiPhi.TenNhom == tenNhom)?.DinhLuong1Suat ?? 0;

        return new DinhMucGiaViViewModel
        {
            CacDong = dsHangHoa.Select(h =>
            {
                var cua = tra[h.Id];
                return new DongDinhMucGiaViVm
                {
                    HangHoaId = h.Id,
                    TenHang = h.TenHang,
                    Dvt = h.Dvt?.TenDvt,
                    AnSang = Lay(cua, "Ăn sáng"),
                    AnSangNhaTre = Lay(cua, "Ăn sáng - Nhà trẻ"),
                    NhaTre = Lay(cua, "Nhà trẻ"),
                    MauGiao = Lay(cua, "Mẫu giáo"),
                };
            }).ToList(),
        };
    }
}
