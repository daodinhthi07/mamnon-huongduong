using System.Text.Json;
using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>Nhập thực đơn từ file Excel tháng (mỗi sheet = 1 tuần, mẫu "THỰC ĐƠN CHO TRẺ"): đọc -> xem trước/đối chiếu món -> ghi vào thực đơn từng ngày.
/// Chỉ ghi bảng thực đơn (thuc_don/ngay_hoc) + tạo món mới nếu người dùng chọn; không đụng giá, định mức, nguyên liệu.</summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri},{VaiTro.QuanLy}")]
[Route("nhap-thuc-don-excel")]
public sealed class NhapThucDonExcelController : Controller
{
    private readonly NhapThucDonExcelService _svc;
    private readonly ThucDonDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CongKhaiThucDonService _congKhai;

    public NhapThucDonExcelController(NhapThucDonExcelService svc, ThucDonDbContext db, UserManager<ApplicationUser> userManager, CongKhaiThucDonService congKhai)
    {
        _svc = svc; _db = db; _userManager = userManager; _congKhai = congKhai;
    }

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("xem")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Xem(IFormFile? file)
    {
        if (file is null || file.Length == 0) { TempData["ThongBao"] = "Chưa chọn file Excel."; return RedirectToAction(nameof(Index)); }
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) { TempData["ThongBao"] = "Chỉ nhận file .xlsx."; return RedirectToAction(nameof(Index)); }
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var kq = await _svc.DocAsync(ms);
        ViewBag.TenFile = file.FileName;
        ViewBag.Json = JsonSerializer.Serialize(kq.Tuan);
        return View(kq);
    }

    [HttpPost("ap-dung")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApDung(string duLieuJson, string[]? keys, string[]? chons, bool ghiDe, string[]? tuanChon, string[]? moLaiNgay)
    {
        List<NhapTuan>? ds;
        try { ds = JsonSerializer.Deserialize<List<NhapTuan>>(duLieuJson ?? ""); }
        catch { ds = null; }
        if (ds is null || ds.Count == 0) { TempData["ThongBao"] = "Dữ liệu nhập không hợp lệ, hãy tải file lên lại."; return RedirectToAction(nameof(Index)); }

        var chonTuan = (tuanChon ?? Array.Empty<string>()).ToHashSet();
        ds = ds.Where(t => t.BatDau.DayOfWeek == DayOfWeek.Monday && chonTuan.Contains(t.BatDau.ToString("yyyy-MM-dd"))).ToList();
        if (ds.Count == 0) { TempData["ThongBao"] = "Chưa chọn tuần nào để nhập."; return RedirectToAction(nameof(Index)); }

        var lua = new Dictionary<string, string>();
        for (var i = 0; i < Math.Min(keys?.Length ?? 0, chons?.Length ?? 0); i++) lua[keys![i]] = chons![i];

        var nguoiDung = await _userManager.GetUserAsync(User);
        var nguoiLap = nguoiDung?.HoTen ?? nguoiDung?.UserName;

        // 1) Khóa món -> id (món đã có + món người dùng chọn / tạo mới)
        var khoaMon = await _svc.DungKhoaAsync();
        var hopLeLoai = (await _db.LoaiBuaAns.AsNoTracking().ToListAsync()).ToDictionary(b => b.Id);
        var soMoi = 0;
        foreach (var o in ds.SelectMany(t => t.O))
        {
            if (!hopLeLoai.TryGetValue(o.LoaiBuaAnId, out var bua) || bua.KhoiId != o.KhoiId) continue;
            o.NhomId = bua.NhomBuaAnId;                    // không tin nhóm trong form
            var key = NhapThucDonExcelService.KhoaNhom(o.NhomId, o.Ten);
            if (khoaMon.ContainsKey(key)) continue;
            var chon = lua.TryGetValue(key, out var c) ? c : "moi";
            if (chon == "bo") continue;
            if (int.TryParse(chon, out var idMon) && await _db.MonAns.AnyAsync(m => m.Id == idMon)) { khoaMon[key] = idMon; continue; }
            // Bảng mon_an có chỉ mục UNIQUE theo TÊN (không phân nhóm bữa) -> tìm theo tên ở mọi nhóm / kể cả món đã ẩn, tránh tạo trùng.
            var daCoTen = await _db.MonAns.FirstOrDefaultAsync(m => m.TenMon == o.Ten);
            if (daCoTen is not null) { khoaMon[key] = daCoTen.Id; continue; }
            var moi = new MonAn { TenMon = o.Ten, NhomBuaAnId = o.NhomId, IsActive = true, GhiChu = "Tạo khi nhập thực đơn từ Excel — chưa có công thức nguyên liệu" };
            _db.MonAns.Add(moi);
            await _db.SaveChangesAsync();
            khoaMon[key] = moi.Id; soMoi++;
        }

        // 2) Ghi thực đơn cho mọi cơ sở active (thực đơn dùng chung)
        var dsCoSo = await _db.CoSos.Where(x => x.IsActive).ToListAsync();
        var tu = ds.Min(t => t.BatDau); var den = ds.Max(t => t.BatDau).AddDays(6);
        var ngayHocs = await _db.NgayHocs.Where(n => n.Ngay >= tu && n.Ngay <= den).ToListAsync();
        var thucDons = await _db.ThucDons.Where(t => t.NgayHoc.Ngay >= tu && t.NgayHoc.Ngay <= den).ToListAsync();
        var tdMap = thucDons.GroupBy(t => (t.CoSoId, t.KhoiId, t.NgayHocId, t.LoaiBuaAnId)).ToDictionary(g => g.Key, g => g.First());
        int soGhi = 0, soGiu = 0;

        // Ngày nghỉ trong hệ thống nhưng file có món: chỉ mở lại những ngày người dùng TICK ở trang xem trước
        var moLai = new HashSet<DateOnly>();
        foreach (var chuoi in moLaiNgay ?? Array.Empty<string>())
            if (DateOnly.TryParse(chuoi, out var dMo) && ds.Any(t => dMo >= t.BatDau && dMo <= t.BatDau.AddDays(5))) moLai.Add(dMo);
        var soNgayMoLai = 0;
        foreach (var nhMo in ngayHocs.Where(n => moLai.Contains(n.Ngay) && n.TrangThai != TrangThaiNgay.HocBinhThuong))
        {
            nhMo.TrangThai = TrangThaiNgay.HocBinhThuong; nhMo.GhiChu = null; soNgayMoLai++;
        }
        var thuBayBiBoQua = new SortedSet<DateOnly>();   // Thứ 7 có món trong file nhưng hệ thống đang để NGHỈ -> không ghi (trước đây im lặng)

        foreach (var tuan in ds)
        {
            foreach (var coSo in dsCoSo)
            {
                NgayHoc LayNgay(DateOnly ngay)
                {
                    var nh = ngayHocs.FirstOrDefault(n => n.CoSoId == coSo.Id && n.Ngay == ngay);
                    if (nh is null)
                    {
                        nh = new NgayHoc { CoSoId = coSo.Id, Ngay = ngay, TrangThai = TrangThaiNgay.HocBinhThuong };
                        _db.NgayHocs.Add(nh);
                        _db.SaveChanges();
                        ngayHocs.Add(nh);
                    }
                    return nh;
                }

                if (tuan.ThuBayNghi)
                {
                    var t7 = LayNgay(tuan.BatDau.AddDays(5));
                    t7.TrangThai = TrangThaiNgay.NghiKhac; t7.GhiChu = "Nghỉ thứ bảy";
                }

                // Ngày nghỉ cả ngày ghi trong file (VD "Nghỉ Lễ 2-9") -> đánh dấu ngày nghỉ trong hệ thống.
                foreach (var nghi in tuan.NgayNghi.Where(n => n.Ngay is >= 0 and <= 5))
                {
                    var nhNghi = LayNgay(tuan.BatDau.AddDays(nghi.Ngay));
                    var laLe = NhapThucDonExcelService.BoDau(nghi.GhiChu).ToLowerInvariant().StartsWith("nghi le");
                    nhNghi.TrangThai = laLe ? TrangThaiNgay.NghiLe : TrangThaiNgay.NghiKhac;
                    nhNghi.GhiChu = nghi.GhiChu;
                }

                foreach (var o in tuan.O)
                {
                    if (o.Ngay < 0 || o.Ngay > 5) continue;
                    if (!hopLeLoai.TryGetValue(o.LoaiBuaAnId, out var bua) || bua.KhoiId != o.KhoiId) continue;
                    if (!khoaMon.TryGetValue(NhapThucDonExcelService.KhoaNhom(o.NhomId, o.Ten), out var monId)) continue;
                    var nh = LayNgay(tuan.BatDau.AddDays(o.Ngay));
                    if (nh.TrangThai != TrangThaiNgay.HocBinhThuong)
                    {
                        if (o.Ngay == 5 && nh.TrangThai == TrangThaiNgay.NghiKhac) thuBayBiBoQua.Add(tuan.BatDau.AddDays(5));
                        continue;   // NGÀY NGHỈ (lễ / nghỉ khác) -> không ghi món
                    }

                    var k = (coSo.Id, o.KhoiId, nh.Id, o.LoaiBuaAnId);
                    if (!tdMap.TryGetValue(k, out var td))
                    {
                        td = new ThucDonEntity
                        {
                            CoSoId = coSo.Id, KhoiId = o.KhoiId, NgayHocId = nh.Id, LoaiBuaAnId = o.LoaiBuaAnId,
                            MonAnId = monId, TrangThai = TrangThaiThucDon.Nhap, NguoiLap = nguoiLap,
                            NgayLap = DateOnly.FromDateTime(DateTime.Today)
                        };
                        _db.ThucDons.Add(td); tdMap[k] = td; soGhi++;
                    }
                    else if (td.MonAnId is null || ghiDe)
                    {
                        if (td.MonAnId != monId) { td.MonAnId = monId; td.TrangThai = TrangThaiThucDon.Nhap; td.UpdatedAt = DateTime.UtcNow; soGhi++; }
                    }
                    else soGiu++;
                }
            }
        }

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            TempData["ThongBao"] = "Không lưu được: " + (ex.InnerException?.Message ?? ex.Message);
            return RedirectToAction(nameof(Index));
        }

        // Ngày nghỉ (lễ / nghỉ khác — do file ghi hoặc hệ thống đã đặt) KHÔNG ghi gì, kể cả phụ sáng / phụ chiều.
        var ngayNghiHeThong = (await _db.NgayHocs.AsNoTracking()
            .Where(n => n.Ngay >= tu && n.Ngay <= den && n.TrangThai != TrangThaiNgay.HocBinhThuong)
            .Select(n => n.Ngay).ToListAsync()).ToHashSet();

        // Phụ sáng / Phụ chiều (trang công khai): chỉ ghi đè các ngày file có dữ liệu
        foreach (var tuan in ds.Where(t => t.Phu.Count > 0))
        {
            var cu = await _congKhai.LayPhuAsync(tuan.BatDau);
            var sang = new string?[6]; var chieu = new string?[6];
            for (var i = 0; i < 6; i++) { cu.TryGetValue(i, out var c0); sang[i] = c0.Sang; chieu[i] = c0.Chieu; }
            foreach (var p in tuan.Phu.Where(p => p.Ngay is >= 0 and <= 5 && !ngayNghiHeThong.Contains(tuan.BatDau.AddDays(p.Ngay))))
            {
                if (p.Sang.Length > 0) sang[p.Ngay] = p.Sang;
                if (p.Chieu.Length > 0) chieu[p.Ngay] = p.Chieu;
            }
            await _congKhai.LuuPhuAsync(tuan.BatDau, sang, chieu);
        }

        var coSoDem = Math.Max(1, dsCoSo.Count);
        TempData["ThongBao"] = $"Đã nhập {ds.Count} tuần: ghi {soGhi / coSoDem} ô món (áp dụng cho {coSoDem} cơ sở), tạo {soMoi} món mới" +
            (soGiu > 0 ? $", giữ nguyên {soGiu / coSoDem} ô đã có món (không ghi đè)" : "") + ". Thực đơn ở trạng thái Nháp — mở từng tuần để kiểm tra rồi gửi duyệt.";
        if (soNgayMoLai > 0)
            TempData["ThongBao"] += $" Đã mở lại {moLai.Count} ngày nghỉ thành \"Học bình thường\" theo lựa chọn của bạn.";
        if (thuBayBiBoQua.Count > 0)
            TempData["ThongBao"] += " ⚠ Thứ 7 " + string.Join(", ", thuBayBiBoQua.Select(d => d.ToString("dd/MM"))) +
                " có món trong file nhưng đang là ngày NGHỈ trong hệ thống nên KHÔNG được ghi (đổi ngày đó sang \"Học bình thường\" rồi nhập lại nếu muốn lấy).";
        TempData["TuanDaNhap"] = string.Join(",", ds.Select(t => t.BatDau.ToString("yyyy-MM-dd")));
        return RedirectToAction(nameof(Index));
    }
}
