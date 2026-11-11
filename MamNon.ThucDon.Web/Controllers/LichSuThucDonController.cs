using System.Globalization;
using System.Text.RegularExpressions;
using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Services;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// Lịch sử thực đơn theo THÁNG: mỗi tháng chia thành các tuần A, B, C, D, E (tuần 1, 2, 3, 4, 5 của tháng), mỗi tuần
/// hiện lưới Thứ 2 -> Thứ 7 cho cả 2 khối; bên dưới thống kê MÓN ĐÃ DÙNG trong tháng (số ngày, ngày nào, lần dùng
/// gần nhất trước tháng) để khi soạn thực đơn mới tránh lặp món. Chỉ ĐỌC dữ liệu — không sửa gì; muốn sửa thì mở tuần
/// đó trong Dashboard. Thực đơn dùng chung cho cả 3 cơ sở nên đọc từ cơ sở đại diện (giống Dashboard).
/// </summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("lich-su-thuc-don")]
public sealed class LichSuThucDonController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly MauTuanExcelService _mau;
    private readonly ThucDonTuMauService _nhap;
    private readonly ExcelExportService _excelExport;

    public LichSuThucDonController(ThucDonDbContext db, MauTuanExcelService mau, ThucDonTuMauService nhap, ExcelExportService excelExport)
    {
        _db = db;
        _mau = mau;
        _nhap = nhap;
        _excelExport = excelExport;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? thang)
    {
        var homNay = DateOnly.FromDateTime(DateTime.Today);
        var nam = homNay.Year;
        var th = homNay.Month;
        if (!string.IsNullOrWhiteSpace(thang)
            && DateOnly.TryParseExact(thang + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            nam = d.Year;
            th = d.Month;
        }

        var vm = new LichSuThucDonViewModel { Nam = nam, Thang = th };
        var cacMau = _mau.LietKe();
        vm.CacMau = cacMau.Select(m => m.Ten + " — " + m.ThuHai.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).ToList();
        vm.GoiYTuanMoi = LichSuThucDonViewModel.ThuHaiCuaTuan(homNay).AddDays(7);
        var cacThuHai = LichSuThucDonViewModel.CacTuanCuaThang(nam, th);

        var coSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).FirstOrDefaultAsync();
        vm.TenCoSoDaiDien = coSo?.TenCoSo;
        if (coSo is null || cacThuHai.Count == 0) return View(vm);

        var dsKhoi = await _db.KhoiHocs.OrderBy(x => x.ThuTu).ToListAsync();
        var dsBua = await _db.LoaiBuaAns.OrderBy(b => b.ThuTu).ToListAsync();

        var tu = cacThuHai.First();
        var den = cacThuHai.Last().AddDays(5);
        var thucDons = await _db.ThucDons
            .Include(t => t.NgayHoc)
            .Include(t => t.MonAn).ThenInclude(m => m!.NhomBuaAn)
            .Where(t => t.CoSoId == coSo.Id && t.NgayHoc.Ngay >= tu && t.NgayHoc.Ngay <= den)
            .ToListAsync();

        // ---------- Từng tuần A, B, C... ----------
        for (var i = 0; i < cacThuHai.Count; i++)
        {
            var batDau = cacThuHai[i];
            var ketThuc = batDau.AddDays(5);
            var cuaTuan = thucDons.Where(t => t.NgayHoc.Ngay >= batDau && t.NgayHoc.Ngay <= ketThuc).ToList();

            var tuan = new LichSuTuanVm
            {
                SoTuan = i + 1,
                ChuCai = (char)('A' + i),
                TuanBatDau = batDau,
                CoThucDon = cuaTuan.Any(t => t.MonAnId != null),
                MauThuHai = cacMau.Where(m => m.ThuHai >= batDau && m.ThuHai <= ketThuc).Select(m => (DateOnly?)m.ThuHai).FirstOrDefault(),
                TrangThai = TinhTrangThai(cuaTuan),
            };

            foreach (var khoi in dsKhoi)
            {
                var luoi = new LichSuKhoiVm { TenKhoi = khoi.TenKhoi };
                foreach (var bua in dsBua.Where(b => b.KhoiId == khoi.Id))
                {
                    var dong = new LichSuBuaRowVm { TenBua = bua.TenBua };
                    for (var k = 0; k < 6; k++)
                    {
                        var ngay = batDau.AddDays(k);
                        var td = cuaTuan.FirstOrDefault(t => t.KhoiId == khoi.Id && t.LoaiBuaAnId == bua.Id && t.NgayHoc.Ngay == ngay);
                        dong.Cac[k].Nghi = cuaTuan.Any(t => t.NgayHoc.Ngay == ngay && t.NgayHoc.TrangThai != TrangThaiNgay.HocBinhThuong);
                        if (td?.MonAn is not null)
                            dong.Cac[k].TenMon = ChiTietNgayTinhToanService.TenMonHienThi(td.MonAn.TenMon);
                    }
                    luoi.CacBua.Add(dong);
                }
                tuan.CacKhoi.Add(luoi);
            }

            vm.CacTuan.Add(tuan);
        }

        // ---------- Món đã dùng trong tháng (gộp mọi khối/bữa; bản "(xế)" gộp với bản cùng tên) ----------
        var dung = new Dictionary<string, LichSuMonDaDungVm>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in thucDons.Where(t => t.MonAn != null).OrderBy(t => t.NgayHoc.Ngay))
        {
            var ngay = td.NgayHoc.Ngay;
            var iTuan = cacThuHai.FindIndex(b => ngay >= b && ngay <= b.AddDays(5));
            if (iTuan < 0) continue;

            var ten = ChiTietNgayTinhToanService.TenMonHienThi(td.MonAn!.TenMon);
            if (!dung.TryGetValue(ten, out var mon))
            {
                mon = new LichSuMonDaDungVm { TenMon = ten, TenNhom = td.MonAn.NhomBuaAn?.TenNhom };
                dung[ten] = mon;
            }
            if (!mon.LanDung.Any(l => l.Ngay == ngay))
                mon.LanDung.Add(new LichSuLanDungVm { Ngay = ngay, Tuan = (char)('A' + iTuan) });
        }

        // Lần dùng gần nhất TRƯỚC tháng này (từ dữ liệu thực đơn đã có trong hệ thống).
        var truoc = await _db.ThucDons
            .Where(t => t.CoSoId == coSo.Id && t.MonAnId != null && t.NgayHoc.Ngay < tu)
            .Select(t => new { TenMon = t.MonAn!.TenMon, Ngay = t.NgayHoc.Ngay })
            .ToListAsync();
        var lanCuoi = truoc
            .GroupBy(x => ChiTietNgayTinhToanService.TenMonHienThi(x.TenMon), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Ngay), StringComparer.OrdinalIgnoreCase);
        foreach (var mon in dung.Values)
            mon.LanCuoiTruocThang = lanCuoi.TryGetValue(mon.TenMon, out var ngayCuoi) ? (DateOnly?)ngayCuoi : null;

        vm.MonDaDung = dung.Values
            .OrderByDescending(m => m.SoNgay).ThenBy(m => m.TenNhom).ThenBy(m => m.TenMon)
            .ToList();

        return View(vm);
    }

    // ===================================================== Áp dụng tuần: xuất Excel y như file mẫu =====================================================

    /// <summary>Tiền ăn 1 cháu mặc định (nhà trẻ + mẫu giáo) khi áp dụng tuần mới; người dùng sửa được từng ngày.</summary>
    private const double TienMacDinh = 29000;

    [HttpGet("ap-dung")]
    public async Task<IActionResult> ApDung(DateOnly nguon, string? thang)
    {
        var mau = _mau.Tim(nguon);
        if (mau == null)
        {
            TempData["LoiMau"] = "Chưa có file Excel mẫu cho tuần này. Hãy tải file mẫu lên ở cuối trang.";
            return RedirectToAction(nameof(Index), new { thang });
        }
        var chiTiet = _mau.DocChiTiet(mau);
        var homNay = DateOnly.FromDateTime(DateTime.Today);
        var vm = new ApDungTuanViewModel
        {
            Nguon = nguon,
            Thang = thang,
            TenMau = mau.Ten,
            TuanMoi = LichSuThucDonViewModel.ThuHaiCuaTuan(homNay).AddDays(7),
            NgayCoThucDon = chiTiet.NgayCoThucDon,
        };
        foreach (var khoa in new[] { "TC", "PH", "ĐT" })
        {
            if (!chiTiet.Suat.TryGetValue(khoa, out var theoNgay)) continue;
            var coSo = new ApDungCoSoVm { Khoa = khoa };
            chiTiet.Tien.TryGetValue(khoa, out var tienTheoNgay);
            foreach (var ngay in chiTiet.NgayCoThucDon)
            {
                if (!theoNgay.TryGetValue(ngay, out var s)) continue;
                double?[]? t = null;
                tienTheoNgay?.TryGetValue(ngay, out t);
                coSo.Ngay.Add(new ApDungNgayVm
                {
                    Ngay = ngay, Sang = s[0], NhaTre = s[1], MauGiao = s[2],
                    TienSang = t?[0],
                    TienNhaTre = t?[1] != null ? TienMacDinh : null,
                    TienMauGiao = t?[2] != null ? TienMacDinh : null,
                });
            }
            // Ngày KHÔNG có trong file mẫu (VD mẫu nghỉ Thứ 7 nhưng tuần mới có học): vẫn hiện hàng để nhập số suất + tiền ăn.
            for (var ngay = 0; ngay <= 5; ngay++)
            {
                if (chiTiet.NgayCoThucDon.Contains(ngay)) continue;
                coSo.Ngay.Add(new ApDungNgayVm { Ngay = ngay, TrongMau = false, TienSang = 10000, TienNhaTre = TienMacDinh, TienMauGiao = TienMacDinh });
            }
            coSo.Ngay = coSo.Ngay.OrderBy(x => x.Ngay).ToList();
            vm.CoSo.Add(coSo);
        }

        // ---- Danh sách món trong mẫu để người dùng chọn ĐỔI (không bắt buộc) ----
        var dsLoai = await _db.LoaiBuaAns.AsNoTracking().ToListAsync();
        vm.DsMon = await _db.MonAns.AsNoTracking()
            .Where(m => m.IsActive && m.DinhLuongNguyenLieus.Any())      // chỉ món đã có định lượng nguyên liệu
            .OrderBy(m => m.TenMon)
            .Select(m => new ApDungMonOption { Id = m.Id, Ten = m.TenMon, NhomId = m.NhomBuaAnId })
            .ToListAsync();
        var thucDonMau = _mau.DocThucDon(mau);
        foreach (var c in thucDonMau.Cells)
        {
            var lb = TimBuaMau(dsLoai, c.Khoi, c.Nhan);
            if (lb == null || lb.NhomBuaAnId == null) continue;
            var tenBua = lb.TenBua.ToUpperInvariant();
            if (c.Khoi == 2) continue;   // chỉ hiện dòng Mẫu giáo; đổi món Mẫu giáo thì Nhà trẻ đổi theo
            vm.DoiMon.Add(new ApDungDoiMonVm
            {
                Ngay = c.Ngay,
                Khoi = c.Khoi,
                LoaiBuaId = lb.Id,
                NhomId = lb.NhomBuaAnId,
                TenBua = lb.TenBua,
                MonMau = c.Text,
                DongBoNhaTre = c.Khoi == 1 && tenBua != "XÀO",   // Nhà trẻ không có bữa XÀO
                ThuTuBua = lb.ThuTu,
            });
        }
        // Ngày KHÔNG có trong file mẫu: thêm đủ các bữa của Mẫu giáo để người dùng chọn món (Nhà trẻ lấy theo Mẫu giáo).
        var khoiMauGiao = await _db.KhoiHocs.AsNoTracking().FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        if (khoiMauGiao != null)
        {
            for (var ngay = 0; ngay <= 5; ngay++)
            {
                if (chiTiet.NgayCoThucDon.Contains(ngay)) continue;
                foreach (var lb in dsLoai.Where(x => x.KhoiId == khoiMauGiao.Id && x.NhomBuaAnId != null).OrderBy(x => x.ThuTu))
                {
                    vm.DoiMon.Add(new ApDungDoiMonVm
                    {
                        Ngay = ngay, Khoi = khoiMauGiao.Id, LoaiBuaId = lb.Id, NhomId = lb.NhomBuaAnId, TenBua = lb.TenBua,
                        MonMau = "(mẫu không có ngày này)", TrongMau = false,
                        DongBoNhaTre = lb.TenBua.ToUpperInvariant() != "XÀO", ThuTuBua = lb.ThuTu,
                    });
                }
            }
        }
        vm.DoiMon = vm.DoiMon.OrderBy(x => x.Ngay).ThenBy(x => x.Khoi).ThenBy(x => x.ThuTuBua).ToList();
        return View(vm);
    }

    /// <summary>Tìm loại bữa ăn ứng với ô (khối, nhãn) của file mẫu — giống cách ThucDonTuMauService nạp thực đơn.</summary>
    private static LoaiBuaAn? TimBuaMau(List<LoaiBuaAn> loai, int khoi, string nhan)
    {
        var u = nhan.Trim().ToUpperInvariant();
        if (khoi == 2 && u == "XẾ") return null;               // ô XẾ của Nhà trẻ chỉ là sữa
        if (khoi == 2 && u == "PHỤ SÁNG") u = "CANH";
        if (khoi == 2 && u == "TRƯA") u = "MẶN";
        return loai.FirstOrDefault(x => x.KhoiId == khoi && x.TenBua.ToUpperInvariant() == u);
    }

    /// <summary>Ghi các món người dùng chọn ĐỔI vào thực đơn tuần mới (cho mọi cơ sở): món cũ bị thay bằng món mới,
    /// nguyên liệu và định lượng lúc xuất Excel sẽ lấy theo món mới. Mọi bữa: Nhà trẻ đổi theo Mẫu giáo (Xế MG = Chính chiều NT; Xào chỉ có ở Mẫu giáo).</summary>
    private async Task<(int SoDoi, List<string> CanhBao)> AdoDoiMonAsync(DateOnly tuanMoi, List<(int Ngay, int LoaiId, int MonId)> doi)
    {
        var canhBao = new List<string>();
        var dsLoai = await _db.LoaiBuaAns.AsNoTracking().ToListAsync();
        var khoiNhaTre = await _db.KhoiHocs.AsNoTracking().FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        var ids = doi.Select(d => d.MonId).Distinct().ToList();
        var mons = await _db.MonAns.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.TenMon, m.IsActive, m.NhomBuaAnId, CoDinhLuong = m.DinhLuongNguyenLieus.Any() })
            .ToListAsync();

        var canGhi = new List<(int Ngay, int KhoiId, int LoaiId, int MonId)>();
        foreach (var d in doi)
        {
            var lb = dsLoai.FirstOrDefault(x => x.Id == d.LoaiId);
            var mon = mons.FirstOrDefault(x => x.Id == d.MonId);
            if (lb == null || mon == null) continue;
            if (!mon.IsActive || !mon.CoDinhLuong || mon.NhomBuaAnId != lb.NhomBuaAnId)
            {
                canhBao.Add($"Bỏ qua đổi sang \"{mon.TenMon}\" (món không hợp lệ cho bữa {lb.TenBua} hoặc chưa có định lượng).");
                continue;
            }
            canGhi.Add((d.Ngay, lb.KhoiId, lb.Id, mon.Id));
            if (khoiNhaTre != null && lb.KhoiId != khoiNhaTre.Id)
            {
                var tenBua = lb.TenBua.ToUpperInvariant();
                // Nhà trẻ đổi theo Mẫu giáo: SÁNG-SÁNG, CANH-CANH, MẶN-MẶN, TRÁNG MIỆNG-TRÁNG MIỆNG, XẾ (MG) - CHÍNH CHIỀU (NT); XÀO chỉ có ở Mẫu giáo
                var tenNt = tenBua == "XẾ" ? "CHÍNH CHIỀU" : tenBua == "XÀO" ? null : tenBua;
                var nt = tenNt == null ? null : dsLoai.FirstOrDefault(x => x.KhoiId == khoiNhaTre.Id && x.TenBua.ToUpperInvariant() == tenNt);
                if (nt != null) canGhi.Add((d.Ngay, nt.KhoiId, nt.Id, mon.Id));
            }
        }

        foreach (var g in canGhi)
        {
            var ngay = tuanMoi.AddDays(g.Ngay);
            var dongs = await _db.ThucDons
                .Where(t => t.KhoiId == g.KhoiId && t.LoaiBuaAnId == g.LoaiId && t.NgayHoc.Ngay == ngay)
                .ToListAsync();
            foreach (var t in dongs)
            {
                t.MonAnId = g.MonId;
                t.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
        return (doi.Count - canhBao.Count, canhBao);
    }

    /// <summary>Thêm vào tuần mới các ngày KHÔNG có trong file mẫu (VD Thứ 7): tạo ngày học, ghi món đã chọn (Mẫu giáo; Nhà trẻ theo Mẫu giáo)
    /// và số suất + tiền ăn, cho mọi cơ sở. Phải gọi SAU NhapAsync (NhapAsync xóa các ngày không có trong mẫu).</summary>
    private async Task<(int SoMon, List<string> CanhBao)> ThemNgayNgoaiMauAsync(DateOnly tuanMoi, SortedSet<int> ngayThem,
        List<(int Ngay, int LoaiId, int MonId)> doi, Dictionary<string, double?[]> suat, Dictionary<string, double?[]> tien)
    {
        var canhBao = new List<string>();
        if (ngayThem.Count == 0) return (0, canhBao);

        var dsLoai = await _db.LoaiBuaAns.AsNoTracking().ToListAsync();
        var khoiNhaTre = await _db.KhoiHocs.AsNoTracking().FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        var ids = doi.Select(d => d.MonId).Distinct().ToList();
        var mons = await _db.MonAns.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.TenMon, m.IsActive, m.NhomBuaAnId, CoDinhLuong = m.DinhLuongNguyenLieus.Any() }).ToListAsync();

        var canGhi = new List<(int Ngay, int KhoiId, int LoaiId, int MonId)>();
        foreach (var d in doi)
        {
            var lb = dsLoai.FirstOrDefault(x => x.Id == d.LoaiId);
            var mon = mons.FirstOrDefault(x => x.Id == d.MonId);
            if (lb == null || mon == null) continue;
            if (!mon.IsActive || !mon.CoDinhLuong || mon.NhomBuaAnId != lb.NhomBuaAnId)
            {
                canhBao.Add($"Bỏ qua {LichSuThucDonViewModel.TenThu[d.Ngay]} - \"{mon.TenMon}\" (món không hợp lệ cho bữa {lb.TenBua} hoặc chưa có định lượng).");
                continue;
            }
            canGhi.Add((d.Ngay, lb.KhoiId, lb.Id, mon.Id));
            if (khoiNhaTre != null && lb.KhoiId != khoiNhaTre.Id)
            {
                var tenBua = lb.TenBua.ToUpperInvariant();
                var tenNt = tenBua == "XẾ" ? "CHÍNH CHIỀU" : tenBua == "XÀO" ? null : tenBua;
                var nt = tenNt == null ? null : dsLoai.FirstOrDefault(x => x.KhoiId == khoiNhaTre.Id && x.TenBua.ToUpperInvariant() == tenNt);
                if (nt != null) canGhi.Add((d.Ngay, nt.KhoiId, nt.Id, mon.Id));
            }
        }

        var coSos = await _db.CoSos.Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync();
        var nguoi = User.Identity?.Name;
        var homNay = DateOnly.FromDateTime(DateTime.Today);
        var tu = tuanMoi;
        var den = tuanMoi.AddDays(5);
        foreach (var cs in coSos)
        {
            var ngayHocs = await _db.NgayHocs.Where(n => n.CoSoId == cs.Id && n.Ngay >= tu && n.Ngay <= den).ToListAsync();
            foreach (var k in ngayThem)
            {
                var ngay = tu.AddDays(k);
                var nh = ngayHocs.FirstOrDefault(n => n.Ngay == ngay);
                if (nh == null)
                {
                    nh = new NgayHoc { CoSoId = cs.Id, Ngay = ngay, TrangThai = TrangThaiNgay.HocBinhThuong };
                    _db.NgayHocs.Add(nh);
                    ngayHocs.Add(nh);
                }
                else if (nh.TrangThai != TrangThaiNgay.HocBinhThuong)
                {
                    nh.TrangThai = TrangThaiNgay.HocBinhThuong;
                    nh.GhiChu = null;
                }
            }
            await _db.SaveChangesAsync();

            var nhIds = ngayHocs.Select(n => n.Id).ToList();
            var dong = await _db.ThucDons.Where(t => t.CoSoId == cs.Id && nhIds.Contains(t.NgayHocId)).ToListAsync();
            foreach (var g in canGhi)
            {
                var nh = ngayHocs.First(n => n.Ngay == tu.AddDays(g.Ngay));
                var co = dong.FirstOrDefault(t => t.NgayHocId == nh.Id && t.KhoiId == g.KhoiId && t.LoaiBuaAnId == g.LoaiId);
                if (co == null)
                {
                    var moi = new ThucDonEntity
                    {
                        CoSoId = cs.Id, KhoiId = g.KhoiId, NgayHocId = nh.Id, LoaiBuaAnId = g.LoaiId, MonAnId = g.MonId,
                        TrangThai = TrangThaiThucDon.Nhap, NguoiLap = nguoi, NgayLap = homNay,
                    };
                    _db.ThucDons.Add(moi);
                    dong.Add(moi);
                }
                else
                {
                    co.MonAnId = g.MonId;
                    co.TrangThai = TrangThaiThucDon.Nhap;
                    co.UpdatedAt = DateTime.UtcNow;
                }
            }
            await _db.SaveChangesAsync();

            // số suất ăn + tiền ăn 1 cháu (nhóm chi phí 1 = Ăn sáng, 2 = Nhà trẻ, 3 = Mẫu giáo)
            var khoa = cs.MaCoSo.Trim();
            var dm = await _db.DinhMucAnNgays.Where(x => x.CoSoId == cs.Id && x.Ngay >= tu && x.Ngay <= den).ToListAsync();
            foreach (var k in ngayThem)
            {
                if (!suat.TryGetValue(khoa + "|" + k, out var suatNgay)) continue;
                tien.TryGetValue(khoa + "|" + k, out var tienNgay);
                var ngay = tu.AddDays(k);
                for (var i = 0; i < 3; i++)
                {
                    if (suatNgay[i] == null) continue;
                    var nhomId = i + 1;
                    var so = (int)Math.Round(suatNgay[i]!.Value);
                    var gia = tienNgay?[i] ?? (i == 0 ? 10000d : TienMacDinh);
                    var cu = dm.FirstOrDefault(x => x.Ngay == ngay && x.NhomChiPhiId == nhomId);
                    if (cu == null)
                        _db.DinhMucAnNgays.Add(new DinhMucAnNgay { CoSoId = cs.Id, NhomChiPhiId = nhomId, Ngay = ngay, SoSuatAn = so, TienAn1Chau = (decimal)gia });
                    else
                    {
                        cu.SoSuatAn = so;
                        cu.TienAn1Chau = (decimal)gia;
                    }
                }
            }
            await _db.SaveChangesAsync();
        }

        foreach (var k in ngayThem)
        {
            if (!doi.Any(d => d.Ngay == k)) canhBao.Add($"{LichSuThucDonViewModel.TenThu[k]}: chưa chọn món nào — ngày được tạo nhưng thực đơn trống.");
            var coSuat = suat.Any(kv => kv.Key.EndsWith("|" + k, StringComparison.Ordinal) && kv.Value.Any(v => v is > 0));
            if (!coSuat) canhBao.Add($"{LichSuThucDonViewModel.TenThu[k]}: chưa nhập số suất — số lượng nguyên liệu của ngày này sẽ bằng 0.");
        }
        return (canGhi.Select(g => (g.Ngay, g.LoaiId)).Distinct().Count(), canhBao);
    }

    [HttpPost("ap-dung")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApDung(DateOnly nguon, DateOnly tuanMoi, string? thang, bool datHeThong)
    {
        var mau = _mau.Tim(nguon);
        if (mau == null)
        {
            TempData["LoiMau"] = "Không tìm thấy file mẫu của tuần này.";
            return RedirectToAction(nameof(Index), new { thang });
        }
        if (tuanMoi.DayOfWeek != DayOfWeek.Monday)
        {
            TempData["LoiMau"] = "Ngày bắt đầu tuần mới phải là Thứ 2.";
            return RedirectToAction(nameof(ApDung), new { nguon = nguon.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), thang });
        }

        // Ô nhập có tên s_{cơ sở}_{ngày 0..5}_{0 = ăn sáng | 1 = nhà trẻ | 2 = mẫu giáo}
        var suat = new Dictionary<string, double?[]>();
        foreach (var khoa in Request.Form.Keys)
        {
            var m = Regex.Match(khoa, @"^s_(.+)_(\d)_(\d)$");
            if (!m.Success) continue;
            var text = Request.Form[khoa].ToString().Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) continue;
            var key = m.Groups[1].Value + "|" + m.Groups[2].Value;
            if (!suat.TryGetValue(key, out var mang)) { mang = new double?[3]; suat[key] = mang; }
            mang[int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)] = v;
        }

        // Ô nhập tiền ăn 1 cháu có tên t_{cơ sở}_{ngày 0..5}_{0 = ăn sáng | 1 = nhà trẻ | 2 = mẫu giáo}
        var tien = new Dictionary<string, double?[]>();
        foreach (var khoa in Request.Form.Keys)
        {
            var m = Regex.Match(khoa, @"^t_(.+)_(\d)_(\d)$");
            if (!m.Success) continue;
            var text = Request.Form[khoa].ToString().Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) continue;
            var key = m.Groups[1].Value + "|" + m.Groups[2].Value;
            if (!tien.TryGetValue(key, out var mang)) { mang = new double?[3]; tien[key] = mang; }
            mang[int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)] = v;
        }

        // Ô chọn đổi món có tên doi_{ngày 0..5}_{khối}_{loại bữa}; giá trị = id món mới (trống = giữ nguyên món mẫu).
        var doiMon = new List<(int Ngay, int LoaiId, int MonId)>();
        foreach (var khoa in Request.Form.Keys)
        {
            var m = Regex.Match(khoa, @"^doi_(\d)_(\d+)_(\d+)$");
            if (!m.Success) continue;
            if (!int.TryParse(Request.Form[khoa].ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var monMoi) || monMoi <= 0) continue;
            doiMon.Add((int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), monMoi));
        }

        // Ngày KHÔNG có trong file mẫu (VD Thứ 7): có nhập số suất hoặc có chọn món => thêm ngày đó vào tuần mới.
        var ngayCoMau = _mau.DocChiTiet(mau).NgayCoThucDon.ToHashSet();
        var doiTrongMau = doiMon.Where(d => ngayCoMau.Contains(d.Ngay)).ToList();
        var doiNgayThem = doiMon.Where(d => !ngayCoMau.Contains(d.Ngay)).ToList();
        var ngayThem = new SortedSet<int>(doiNgayThem.Select(d => d.Ngay));
        foreach (var kv in suat)
        {
            var p = kv.Key.Split('|');
            if (p.Length == 2 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n is >= 0 and <= 5 && !ngayCoMau.Contains(n) && kv.Value.Any(v => v is > 0))
                ngayThem.Add(n);
        }

        var (namMoi, thMoi) = LichSuThucDonViewModel.ThangCuaTuan(tuanMoi);
        var idxMoi = Math.Max(0, LichSuThucDonViewModel.CacTuanCuaThang(namMoi, thMoi).IndexOf(tuanMoi));
        var tenFile = $"TUẦN {idxMoi + 1} THÁNG {thMoi} {(char)('A' + idxMoi)}.xlsx";

        // ---- CÓ ĐỔI MÓN: đặt tuần mới vào hệ thống, thay món cũ bằng món mới, rồi xuất Excel TỪ HỆ THỐNG
        //      (nguyên liệu + định lượng lấy theo món mới). Không đổi món => xuất y như cũ ở nhánh bên dưới. ----
        // Tuần mẫu có món SÚP: bột năng / bột bán phải theo quy tắc từng chợ (ChiTietNgayTinhToanService.SupMacDinh) nên phải tính lại
        // từ hệ thống — nhánh "clone file mẫu" chỉ co giãn số cũ theo số suất nên không áp dụng được quy tắc này.
        // MẶC ĐỊNH MỚI: luôn xuất Excel TỪ HỆ THỐNG (áp dụng mọi quy tắc tính nguyên liệu), không còn clone file mẫu.
        // Muốn quay lại cách clone file mẫu: đổi dungHeThong = false (nhánh bên dưới vẫn giữ nguyên).
        // TUẦN KHÔNG ĐỔI GÌ (không đổi món, không thêm ngày, số suất + tiền ăn 1 cháu đúng bằng file mẫu) => xuất ĐÚNG số lượng
        // nhân viên đã chỉnh tay trong file mẫu (đã cân Chênh lệch = 0) thay vì tính lại từ công thức.
        // Chỉ cần MỘT ngày / chợ / khối đổi suất hoặc tiền là cả tuần chuyển sang tính từ hệ thống (cân đối + dồn vào nguyên liệu chính).
        var khongDoiGi = doiMon.Count == 0 && ngayThem.Count == 0 && SuatVaTienGiongMau(_mau.DocChiTiet(mau), suat, tien);
        var dungHeThong = !khongDoiGi;
        var coMonSup = _mau.DocThucDon(mau).Cells.Any(c => ChiTietNgayTinhToanService.TenMonHienThi(c.Text).StartsWith("Súp", StringComparison.OrdinalIgnoreCase));
        if (dungHeThong || doiMon.Count > 0 || ngayThem.Count > 0 || (coMonSup && !khongDoiGi))
        {
            try
            {
                // Cả chuỗi "đặt thực đơn tuần mới vào hệ thống + dựng Excel" nằm trong MỘT giao dịch: nếu Excel không tạo được
                // (VD có nguyên liệu không có trong Bảng giá) thì thực đơn / số suất cũ của tuần đó KHÔNG bị xóa hay thay đổi.
                await using var gdNhap = await _db.Database.BeginTransactionAsync();
                var kqNhap = await _nhap.NhapAsync(mau, tuanMoi, true, TrangThaiThucDon.Nhap, User.Identity?.Name, suat, true, tien);
                if (kqNhap.Loi != null)
                {
                    TempData["LoiMau"] = kqNhap.Loi;
                    return RedirectToAction(nameof(ApDung), new { nguon = nguon.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), thang });
                }
                var (soDoi, loiDoi) = await AdoDoiMonAsync(tuanMoi, doiTrongMau);
                var (soThem, loiThem) = await ThemNgayNgoaiMauAsync(tuanMoi, ngayThem, doiNgayThem, suat, tien);
                loiDoi.AddRange(loiThem);
                var bytes = await _excelExport.XuatBangKeChoNgayAsync(tuanMoi);
                await gdNhap.CommitAsync();   // Excel đã tạo xong -> mới ghi thật vào CSDL
                TempData["ThongBao"] = $"Đã đổi {soDoi} món" + (ngayThem.Count > 0 ? $", thêm {ngayThem.Count} ngày ngoài mẫu ({soThem} món)" : "")
                    + $" và đặt thực đơn tuần {tuanMoi:dd/MM/yyyy} vào hệ thống (Nháp); Excel tính lại nguyên liệu theo thực đơn mới."
                    + (loiDoi.Count > 0 ? " Lưu ý: " + string.Join(" ", loiDoi) : "");
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", tenFile);
            }
            catch (ThieuBangGiaException ex)
            {
                // Có nguyên liệu không có trong Bảng giá (hoặc chưa có giá): báo cụ thể, không tạo file.
                if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(422, new { ok = false, message = ex.Message, items = ex.Dong });
                TempData["LoiMau"] = ex.Message;
                return RedirectToAction(nameof(ApDung), new { nguon = nguon.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), thang });
            }
            catch (Exception ex)
            {
                if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(500, new { ok = false, message = "Không đổi được món / xuất Excel: " + ex.Message + (ex.InnerException != null ? " — Chi tiết: " + ex.InnerException.Message : "") });
                TempData["LoiMau"] = "Không đổi được món / xuất Excel: " + ex.Message + (ex.InnerException != null ? " — Chi tiết: " + ex.InnerException.Message : "");
                return RedirectToAction(nameof(ApDung), new { nguon = nguon.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), thang });
            }
        }

        foreach (var key in suat.Keys.Where(k => !ngayCoMau.Contains(int.Parse(k.Split('|')[1], CultureInfo.InvariantCulture))).ToList()) suat.Remove(key);
        foreach (var key in tien.Keys.Where(k => !ngayCoMau.Contains(int.Parse(k.Split('|')[1], CultureInfo.InvariantCulture))).ToList()) tien.Remove(key);

        var canhBao = new List<string>();
        var noiDung = _mau.TaoFile(mau, tuanMoi, suat, canhBao, tien);

        // (tùy chọn) đặt luôn thực đơn tuần mới vào hệ thống: XÓA thực đơn/số suất cũ của tuần đó rồi nạp theo mẫu + số suất vừa nhập
        if (datHeThong)
        {
            try
            {
                var kqNhap = await _nhap.NhapAsync(mau, tuanMoi, true, TrangThaiThucDon.Nhap, User.Identity?.Name, suat, true, tien);
                TempData["ThongBao"] = kqNhap.TomTat(tuanMoi);
            }
            catch (Exception ex)
            {
                TempData["LoiMau"] = "Đã tạo file Excel nhưng chưa cập nhật được hệ thống: " + ex.Message;
            }
        }
        return File(noiDung, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", tenFile);
    }

    [HttpPost("cap-nhat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CapNhatThucDon(DateOnly nguon, string? thang)
    {
        var mau = _mau.Tim(nguon);
        if (mau == null)
        {
            TempData["LoiMau"] = "Không tìm thấy file mẫu của tuần này.";
            return RedirectToAction(nameof(Index), new { thang });
        }
        try
        {
            var kq = await _nhap.NhapAsync(mau, mau.ThuHai, true, TrangThaiThucDon.DaDuyet, User.Identity?.Name, null, true);
            if (kq.Loi != null) TempData["LoiMau"] = kq.Loi; else TempData["ThongBao"] = kq.TomTat(mau.ThuHai);
        }
        catch (Exception ex)
        {
            TempData["LoiMau"] = "Chưa cập nhật được thực đơn: " + ex.Message;
        }
        return RedirectToAction(nameof(Index), new { thang });
    }

    [HttpPost("tai-mau")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<IActionResult> TaiMau(List<IFormFile> files, string? thang, bool capNhatHeThong)
    {
        var ok = new List<string>();
        var loi = new List<string>();
        foreach (var f in files)
        {
            if (f.Length == 0) continue;
            if (!Path.GetExtension(f.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) { loi.Add($"{f.FileName}: không phải file .xlsx"); continue; }
            if (f.Length > 20_000_000) { loi.Add($"{f.FileName}: file quá lớn"); continue; }
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            var kq = _mau.LuuMau(ms.ToArray());
            if (!kq.Ok) { loi.Add($"{f.FileName}: {kq.Loi}"); continue; }
            var thongBao = kq.Info!.Ten;
            if (capNhatHeThong)
            {
                try
                {
                    // XÓA HẾT thực đơn cũ của tuần này rồi nạp lại theo file vừa tải lên
                    var kqNhap = await _nhap.NhapAsync(kq.Info, kq.Info.ThuHai, true, TrangThaiThucDon.DaDuyet, User.Identity?.Name, null, true);
                    thongBao += " → " + kqNhap.TomTat(kq.Info.ThuHai);
                }
                catch (Exception ex)
                {
                    loi.Add($"{f.FileName}: đã lưu file mẫu nhưng chưa cập nhật được hệ thống ({ex.Message})");
                }
            }
            ok.Add(thongBao);
        }
        if (ok.Count > 0) TempData["ThongBao"] = "Đã lưu file mẫu: " + string.Join(" | ", ok);
        if (loi.Count > 0) TempData["LoiMau"] = string.Join(" | ", loi);
        return RedirectToAction(nameof(Index), new { thang });
    }

    /// <summary>true nếu MỌI ô số suất / tiền ăn 1 cháu người dùng nhập trùng giá trị trong file mẫu (ô trống = giữ nguyên mẫu).</summary>
    private static bool SuatVaTienGiongMau(MauTuanChiTiet ct, Dictionary<string, double?[]> suat, Dictionary<string, double?[]> tien)
    {
        static bool Khop(Dictionary<string, Dictionary<int, double?[]>> mau, Dictionary<string, double?[]> nhap)
        {
            foreach (var (khoa, mang) in nhap)
            {
                var p = khoa.Split('|');
                if (p.Length != 2 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ngay)) return false;
                if (!mau.TryGetValue(p[0], out var theoNgay) || !theoNgay.TryGetValue(ngay, out var gocMang))
                {
                    if (mang.Any(v => v is > 0)) return false;   // ngày / chợ không có trong mẫu mà lại nhập số
                    continue;
                }
                for (var i = 0; i < 3; i++)
                {
                    if (mang[i] is not double moi) continue;
                    var goc = gocMang[i] ?? 0;
                    if (Math.Abs(moi - goc) > 0.0001) return false;
                }
            }
            return true;
        }
        return Khop(ct.Suat, suat) && Khop(ct.Tien, tien);
    }

    private static string TinhTrangThai(List<ThucDonEntity> dongTuan)
    {
        var coMon = dongTuan.Where(t => t.MonAnId != null).ToList();
        if (coMon.Count == 0) return "Chưa có thực đơn";
        if (coMon.All(t => t.TrangThai == TrangThaiThucDon.DaDuyet)) return "Đã duyệt";
        if (coMon.Any(t => t.TrangThai == TrangThaiThucDon.ChoDuyet)) return "Chờ duyệt";
        return "Nháp";
    }
}
