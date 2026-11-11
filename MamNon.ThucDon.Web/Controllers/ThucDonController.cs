using System.Text.Json;
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

[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("soan-thuc-don")]
public sealed class ThucDonController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NguyenLieuCalculator _nguyenLieuCalc;
    private readonly HangMucNganSachCalculator _hangMucCalc;
    private readonly NganSachNgayCalculator _nganSachCalc;

    public ThucDonController(
        ThucDonDbContext db,
        UserManager<ApplicationUser> userManager,
        NguyenLieuCalculator nguyenLieuCalc,
        HangMucNganSachCalculator hangMucCalc,
        NganSachNgayCalculator nganSachCalc)
    {
        _db = db;
        _userManager = userManager;
        _nguyenLieuCalc = nguyenLieuCalc;
        _hangMucCalc = hangMucCalc;
        _nganSachCalc = nganSachCalc;
    }

    [HttpGet("")]
    public async Task<IActionResult> Soan(int? coSoId, int? khoiId, DateOnly? ngay)
    {
        var vm = new SoanThucDonViewModel
        {
            CoSoId = coSoId,
            KhoiId = khoiId,
            Ngay = ngay ?? DateOnly.FromDateTime(DateTime.Today)
        };

        vm.DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync();
        vm.DsKhoi = await _db.KhoiHocs.OrderBy(x => x.ThuTu).ToListAsync();
        vm.DsNhomChiPhi = await _db.NhomChiPhis.ToListAsync();
        vm.DsMonAn = await _db.MonAns
            .Where(x => x.IsActive)
            .Include(x => x.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
            .OrderBy(x => x.TenMon)
            .ToListAsync();

        if (khoiId is not null)
        {
            vm.DsBuaTheoKhoi = await _db.LoaiBuaAns
                .Where(b => b.KhoiId == khoiId)
                .Include(b => b.HangMuc)
                .OrderBy(b => b.ThuTu)
                .ToListAsync();
        }

        if (coSoId is not null)
        {
            var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSoId && n.Ngay == vm.Ngay);
            if (ngayHoc is not null && khoiId is not null)
            {
                var cacThucDon = await _db.ThucDons
                    .Where(t => t.CoSoId == coSoId && t.KhoiId == khoiId && t.NgayHocId == ngayHoc.Id)
                    .ToListAsync();
                vm.MonDaChonTheoBua = cacThucDon.ToDictionary(t => t.LoaiBuaAnId, t => t.MonAnId);
            }

            if (ngayHoc is not null)
            {
                var dinhMuc = await _db.DinhMucAnNgays
                    .FirstOrDefaultAsync(d => d.CoSoId == coSoId && d.Ngay == vm.Ngay);
                if (dinhMuc is not null)
                {
                    vm.NhomChiPhiId = dinhMuc.NhomChiPhiId;
                    vm.SoSuatAn = dinhMuc.SoSuatAn;
                    vm.TienAn1Chau = dinhMuc.TienAn1Chau;
                }
            }
        }

        return View(vm);
    }

    /// <summary>AJAX: lấy danh sách bữa theo khối, dùng khi người dùng đổi khối mà không load lại trang.</summary>
    [HttpGet("bua-theo-khoi")]
    public async Task<IActionResult> BuaTheoKhoi(int khoiId)
    {
        var ds = await _db.LoaiBuaAns
            .Where(b => b.KhoiId == khoiId)
            .Include(b => b.HangMuc)
            .OrderBy(b => b.ThuTu)
            .Select(b => new { id = b.Id, tenBua = b.TenBua, hangMucId = b.HangMucId, nhomBuaAnId = b.NhomBuaAnId })
            .ToListAsync();

        return Json(ds);
    }

    /// <summary>
    /// AJAX: chọn NGẪU NHIÊN 1 món cho mỗi bữa của khối — dùng cho nút "Chọn
    /// ngẫu nhiên" ở trang Chi tiết ngày. Trả về {buaId: monAnId}, JS tự điền vào các ô
    /// chọn món rồi gọi lại /tinh-toan như khi chọn tay — người dùng vẫn sửa
    /// tay từng ô sau đó nếu không ưng ý, không có gì bị khoá cứng.
    /// </summary>
    [HttpGet("mon-ngau-nhien")]
    public async Task<IActionResult> MonNgauNhien(int khoiId)
    {
        var dsBua = await _db.LoaiBuaAns.Where(b => b.KhoiId == khoiId).ToListAsync();

        // Gom món theo đúng nhóm bữa (Sáng/Canh/Mặn/Xào/Tráng miệng/Xế/...) — chỉ random
        // TRONG CÙNG NHÓM với từng bữa, không còn random lung tung như trước (tránh gán
        // "Nước cam" vào bữa "MẶN").
        var monTheoNhom = (await _db.MonAns
                .Where(m => m.IsActive && m.NhomBuaAnId != null)
                .Select(m => new { m.Id, m.NhomBuaAnId })
                .ToListAsync())
            .GroupBy(m => m.NhomBuaAnId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());

        var rnd = Random.Shared;
        var ketQua = new Dictionary<int, int?>();
        // Tránh 2 bữa CÙNG NHÓM (hiếm khi xảy ra, nhưng để chắc) bị random trúng
        // chung 1 món trong cùng 1 lượt bấm.
        var daDungTheoNhom = new Dictionary<int, HashSet<int>>();

        foreach (var bua in dsBua)
        {
            if (bua.NhomBuaAnId is null || !monTheoNhom.TryGetValue(bua.NhomBuaAnId.Value, out var dsMonPhuHop) || dsMonPhuHop.Count == 0)
            {
                ketQua[bua.Id] = null; // Chưa có món nào được gán vào đúng nhóm bữa này
                continue;
            }

            if (!daDungTheoNhom.TryGetValue(bua.NhomBuaAnId.Value, out var daDung))
            {
                daDung = new HashSet<int>();
                daDungTheoNhom[bua.NhomBuaAnId.Value] = daDung;
            }

            var ungVien = dsMonPhuHop.Where(id => !daDung.Contains(id)).ToList();
            if (!ungVien.Any()) ungVien = dsMonPhuHop; // hết món chưa dùng thì cho phép lặp lại

            var monChon = ungVien[rnd.Next(ungVien.Count)];
            daDung.Add(monChon);
            ketQua[bua.Id] = monChon;
        }

        return Json(ketQua);
    }

    /// <summary>AJAX: tính lại nguyên liệu/hạng mục/ngân sách mỗi khi người dùng đổi lựa chọn ở trang Soạn thực đơn.</summary>
    [HttpPost("tinh-toan")]
    public async Task<IActionResult> TinhToan([FromBody] TinhToanRequest req)
    {
        if (req.KhoiId is null)
        {
            return Json(new TinhToanResponse());
        }

        var dsBua = await _db.LoaiBuaAns
            .Where(b => b.KhoiId == req.KhoiId)
            .Include(b => b.HangMuc)
            .ToListAsync();

        var monIds = req.MonDaChonTheoBua.Values.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var dsMon = await _db.MonAns
            .Where(m => monIds.Contains(m.Id))
            .Include(m => m.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
            .ToListAsync();

        var thucDonTam = dsBua.Select(bua =>
        {
            var monId = req.MonDaChonTheoBua.TryGetValue(bua.Id, out var m) ? m : null;
            return new ThucDonEntity
            {
                LoaiBuaAnId = bua.Id,
                LoaiBuaAn = bua,
                MonAnId = monId,
                MonAn = monId is null ? null : dsMon.FirstOrDefault(x => x.Id == monId)
            };
        }).ToList();

        var ketQuaNguyenLieu = _nguyenLieuCalc.TinhNhuCauNguyenLieu(thucDonTam.Select(td => (td, req.SoSuatAn)));
        var tongTien = ketQuaNguyenLieu.Sum(k => k.ThanhTien);

        var dinhMucList = await _db.DinhMucTyLeNganSachs
            .Where(d => d.KhoiId == req.KhoiId && d.NgayApDungTu <= req.Ngay)
            .ToListAsync();
        var dinhMucTheoHangMuc = dinhMucList
            .GroupBy(d => d.HangMucId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.NgayApDungTu).First().TienDinhMuc);

        var ketQuaHangMuc = _hangMucCalc.TinhChiPhiTheoHangMuc(thucDonTam, dinhMucTheoHangMuc);

        NganSachDto? nganSachDto = null;
        if (req.NhomChiPhiId is not null && req.TienAn1Chau > 0)
        {
            var nhomChiPhi = await _db.NhomChiPhis.FindAsync(req.NhomChiPhiId.Value);
            if (nhomChiPhi is not null)
            {
                var dinhMucNgay = new DinhMucAnNgay
                {
                    NhomChiPhiId = req.NhomChiPhiId.Value,
                    NhomChiPhi = nhomChiPhi,
                    TienAn1Chau = req.TienAn1Chau,
                    SoSuatAn = req.SoSuatAn
                };
                var ketQuaNganSach = _nganSachCalc.TinhNganSach(dinhMucNgay, tongTien);
                nganSachDto = new NganSachDto(ketQuaNganSach.SoTienDuocPhepChi, ketQuaNganSach.TongChiThucTe, ketQuaNganSach.ChenhLech);
            }
        }

        var response = new TinhToanResponse
        {
            NguyenLieu = ketQuaNguyenLieu.Select(k => new NguyenLieuDto(k.HangHoa.TenHang, k.HangHoa.Dvt?.TenDvt, k.TongCanDung, k.ThanhTien)).ToList(),
            TongTienNguyenLieu = tongTien,
            HangMuc = ketQuaHangMuc.Select(k => new HangMucDto(k.HangMuc.TenHangMuc, k.ChiPhiThucTeTrenSuat, k.DinhMucTrenSuat, k.ChenhLechTrenSuat, k.VuotDinhMuc)).ToList(),
            NganSach = nganSachDto
        };

        return Json(response);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuThucDon(LuuThucDonFormModel model)
    {
        var monDaChon = JsonSerializer.Deserialize<Dictionary<string, int?>>(model.MonDaChonJson) ?? new();

        var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == model.CoSoId && n.Ngay == model.Ngay);
        if (ngayHoc is null)
        {
            ngayHoc = new NgayHoc { CoSoId = model.CoSoId, Ngay = model.Ngay, TrangThai = TrangThaiNgay.HocBinhThuong };
            _db.NgayHocs.Add(ngayHoc);
            await _db.SaveChangesAsync();
        }

        var nguoiDung = await _userManager.GetUserAsync(User);
        var nguoiLap = nguoiDung?.HoTen ?? nguoiDung?.UserName;
        var trangThaiMoi = model.GuiDuyet ? TrangThaiThucDon.ChoDuyet : TrangThaiThucDon.Nhap;

        var dsBua = await _db.LoaiBuaAns.Where(b => b.KhoiId == model.KhoiId).ToListAsync();
        foreach (var bua in dsBua)
        {
            var monId = monDaChon.TryGetValue(bua.Id.ToString(), out var m) ? m : null;

            var thucDon = await _db.ThucDons.FirstOrDefaultAsync(t =>
                t.CoSoId == model.CoSoId && t.KhoiId == model.KhoiId &&
                t.NgayHocId == ngayHoc.Id && t.LoaiBuaAnId == bua.Id);

            if (thucDon is null)
            {
                _db.ThucDons.Add(new ThucDonEntity
                {
                    CoSoId = model.CoSoId,
                    KhoiId = model.KhoiId,
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

        if (model.NhomChiPhiId is not null && model.TienAn1Chau > 0)
        {
            var dinhMucNgay = await _db.DinhMucAnNgays.FirstOrDefaultAsync(d =>
                d.CoSoId == model.CoSoId && d.NhomChiPhiId == model.NhomChiPhiId && d.Ngay == model.Ngay);

            if (dinhMucNgay is null)
            {
                _db.DinhMucAnNgays.Add(new DinhMucAnNgay
                {
                    CoSoId = model.CoSoId,
                    NhomChiPhiId = model.NhomChiPhiId.Value,
                    Ngay = model.Ngay,
                    TienAn1Chau = model.TienAn1Chau,
                    SoSuatAn = model.SoSuatAn
                });
            }
            else
            {
                dinhMucNgay.TienAn1Chau = model.TienAn1Chau;
                dinhMucNgay.SoSuatAn = model.SoSuatAn;
            }
        }

        await _db.SaveChangesAsync();

        TempData["ThongBao"] = model.GuiDuyet ? "Đã gửi duyệt — chờ Hiệu trưởng xác nhận." : "Đã lưu nháp.";
        return RedirectToAction(nameof(Soan), new { coSoId = model.CoSoId, khoiId = model.KhoiId, ngay = model.Ngay });
    }
}
