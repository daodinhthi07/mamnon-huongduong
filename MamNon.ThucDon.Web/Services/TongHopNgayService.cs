using System.Text.Json;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Tính "Chi tiết nguyên liệu &amp; giá" của 1 ngày cho CẢ các cơ sở (TC / PH / ĐT) — số liệu theo thực đơn và sĩ số ĐÃ LƯU.
/// Logic COPY NGUYÊN từ <c>ChiTietNgayController.TongHop</c> + <c>TinhToanCoSoAsync</c> (cùng một cách tính, cùng JSON trả về) để
/// trang phụ huynh ra đúng số liệu như tab "Chi tiết" ở Dashboard. Dùng khi nhân viên bấm "Công khai": kết quả được CHỤP lại
/// (bảng cong_khai_chi_tiet) nên trang phụ huynh không bao giờ tính trực tiếp trên dữ liệu nội bộ.
/// Nếu sau này sửa cách tính ở ChiTietNgayController thì sửa tương ứng ở đây.
/// </summary>
public sealed class TongHopNgayService
{
    // camelCase như JSON mặc định của MVC -> tong-hop-ngay.js đọc được y như khi gọi /chi-tiet-ngay/tong-hop
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web);

    private readonly ThucDonDbContext _db;
    private readonly NguyenLieuCalculator _nguyenLieuCalc;
    private readonly ChiTietNgayTinhToanService _tinhToanSvc;

    public TongHopNgayService(ThucDonDbContext db, NguyenLieuCalculator nguyenLieuCalc, ChiTietNgayTinhToanService tinhToanSvc)
    {
        _db = db; _nguyenLieuCalc = nguyenLieuCalc; _tinhToanSvc = tinhToanSvc;
    }

    /// <summary>JSON dạng { ok, ngay, coSo: [ { coSoId, ma, ten, soSuat..., coThucDon, sang, chinh, giaVi } ] }; null nếu chưa có đủ 2 khối.</summary>
    public async Task<string?> TinhJsonAsync(DateOnly ngay)
    {
        var khoiMg = await _db.KhoiHocs.AsNoTracking().FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        var khoiNt = await _db.KhoiHocs.AsNoTracking().FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        if (khoiMg is null || khoiNt is null) return null;

        var thuTu = new[] { "TC", "PH", "ĐT" };
        var dsCoSo = (await _db.CoSos.AsNoTracking().Where(x => x.IsActive).ToListAsync())
            .OrderBy(c => { var i = Array.FindIndex(thuTu, t => string.Equals(t, (c.MaCoSo ?? "").Trim(), StringComparison.OrdinalIgnoreCase)); return i < 0 ? 99 : i; })
            .ThenBy(c => c.TenCoSo).ToList();

        var ketQua = new List<object>();
        foreach (var cs in dsCoSo)
        {
            Dictionary<int, int?> mg = new(), nt = new();
            var ngayHoc = await _db.NgayHocs.AsNoTracking().FirstOrDefaultAsync(n => n.CoSoId == cs.Id && n.Ngay == ngay);
            if (ngayHoc is not null)
            {
                var cacThucDon = await _db.ThucDons.AsNoTracking().Where(t => t.CoSoId == cs.Id && t.NgayHocId == ngayHoc.Id).ToListAsync();
                mg = cacThucDon.Where(t => t.KhoiId == khoiMg.Id).ToDictionary(t => t.LoaiBuaAnId, t => t.MonAnId);
                nt = cacThucDon.Where(t => t.KhoiId == khoiNt.Id).ToDictionary(t => t.LoaiBuaAnId, t => t.MonAnId);
            }
            var dm = await _db.DinhMucAnNgays.AsNoTracking().Include(d => d.NhomChiPhi)
                .Where(d => d.CoSoId == cs.Id && d.Ngay == ngay).ToListAsync();
            int Suat(string ten) => dm.FirstOrDefault(d => d.NhomChiPhi.TenNhom == ten)?.SoSuatAn ?? 0;
            var req = new ChiTietNgayTinhToanRequest
            {
                KhoiIdMauGiao = khoiMg.Id, KhoiIdNhaTre = khoiNt.Id, CoSoId = cs.Id, Ngay = ngay,
                MonMauGiao = mg, MonNhaTre = nt,
                SoSuatAnSang = Suat("Ăn sáng"), SoSuatMauGiao = Suat("Mẫu giáo"), SoSuatNhaTre = Suat("Nhà trẻ"),
            };
            var r = await TinhToanCoSoAsync(req);
            var gv = await _tinhToanSvc.LayGiaViThatAsync(cs.Id, ngay);
            ketQua.Add(new
            {
                coSoId = cs.Id, ma = (cs.MaCoSo ?? "").Trim(), ten = cs.TenCoSo,
                soSuatAnSang = req.SoSuatAnSang, soSuatNhaTre = req.SoSuatNhaTre, soSuatMauGiao = req.SoSuatMauGiao,
                coThucDon = mg.Values.Any(v => v is not null) || nt.Values.Any(v => v is not null),
                sang = r.Sang, chinh = r.Chinh, giaVi = gv.DanhSach,
            });
        }
        return JsonSerializer.Serialize(new { ok = true, ngay = ngay.ToString("yyyy-MM-dd"), coSo = ketQua }, JsonOpt);
    }

    /// <summary>Tính "sáng" + "chính+xế" + (số đã lưu nếu có) cho 1 (cơ sở, ngày) — copy từ ChiTietNgayController.TinhToanCoSoAsync.</summary>
    private async Task<ChiTietNgayTinhToanResponse> TinhToanCoSoAsync(ChiTietNgayTinhToanRequest req)
    {
        var dsBuaMg = await _db.LoaiBuaAns.Where(b => b.KhoiId == req.KhoiIdMauGiao).ToListAsync();
        var dsBuaNt = await _db.LoaiBuaAns.Where(b => b.KhoiId == req.KhoiIdNhaTre).ToListAsync();

        var tatCaMonId = req.MonMauGiao.Values.Concat(req.MonNhaTre.Values)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var monTheoId = (await _db.MonAns.Where(m => tatCaMonId.Contains(m.Id))
                .Include(m => m.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
                .ToListAsync())
            .ToDictionary(m => m.Id);

        List<ThucDonEntity> XayThucDonTam(List<LoaiBuaAn> dsBua, Dictionary<int, int?> monDaChon) =>
            dsBua.Select(bua =>
            {
                var monId = monDaChon.TryGetValue(bua.Id, out var m) ? m : null;
                return new ThucDonEntity
                {
                    LoaiBuaAnId = bua.Id,
                    LoaiBuaAn = bua,
                    MonAnId = monId,
                    MonAn = monId is not null && monTheoId.TryGetValue(monId.Value, out var mon) ? mon : null,
                };
            }).ToList();

        var thucDonMg = XayThucDonTam(dsBuaMg, req.MonMauGiao);
        var thucDonNt = XayThucDonTam(dsBuaNt, req.MonNhaTre);

        var maCoSoTinh = req.CoSoId > 0
            ? await _db.CoSos.AsNoTracking().Where(c => c.Id == req.CoSoId).Select(c => c.MaCoSo).FirstOrDefaultAsync()
            : null;
        var buaSangMg = thucDonMg.Where(t => t.LoaiBuaAn.TenBua == "SÁNG");
        var ketQuaSang = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            buaSangMg.Select(t => (t, req.SoSuatAnSang)), maCoSo: maCoSoTinh);

        var ketQuaMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatMauGiao)), boSungTrangMieng: 5, maCoSo: maCoSoTinh);   // tráng miệng Mẫu giáo: số trẻ + 5
        var ketQuaNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatNhaTre)), boSungTrangMieng: 1, maCoSo: maCoSoTinh);   // tráng miệng Nhà trẻ: số trẻ + 1

        var nhuCauMonMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatMauGiao)), boSungTrangMieng: 5, tachTheoMon: true, maCoSo: maCoSoTinh);
        var nhuCauMonNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, req.SoSuatNhaTre)), boSungTrangMieng: 1, tachTheoMon: true, maCoSo: maCoSoTinh);

        var response = _tinhToanSvc.Tinh(
            ketQuaSang, ketQuaNt, ketQuaMg, req.SoSuatNhaTre, req.SoSuatMauGiao, maCoSoTinh, req.SoSuatAnSang,
            sangLaChao: thucDonMg.Any(t => t.LoaiBuaAn.TenBua == "SÁNG" && t.MonAn != null && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)),
            nhuCauTheoMonNt: nhuCauMonNt, nhuCauTheoMonMg: nhuCauMonMg,
            ngay: req.Ngay == default ? null : req.Ngay);

        // Ngày đã có số lưu cố định (Sửa/Xóa từng dòng hoặc phiếu xuất kho thật) thì dùng số đã lưu thay cho số ước lượng.
        if (req.CoSoId > 0 && req.Ngay != default)
        {
            var uocLuongChinh = response.Chinh.ToList();
            var thucPham = await _tinhToanSvc.LayThucPhamThatAsync(req.CoSoId, req.Ngay, maCoSoTinh);
            if (thucPham.CoPhieuSang)
            {
                response.Sang = thucPham.Sang;
                response.TongTienSang = thucPham.TongTienSang;
                response.CoPhieuSang = true;
            }
            if (thucPham.CoPhieuChinhXe)
            {
                response.Chinh = ChiTietNgayTinhToanService.SapXepTheoThucDon(thucPham.Chinh, uocLuongChinh);
                response.TongTienChinhNhaTre = thucPham.TongTienChinhNhaTre;
                response.TongTienChinhMauGiao = thucPham.TongTienChinhMauGiao;
                response.CoPhieuChinhXe = true;
                response.GhiChuLech = ChiTietNgayTinhToanService.PhatHienPhieuCu(uocLuongChinh, thucPham.Chinh);
            }
        }

        _tinhToanSvc.TachTheoMon(response, nhuCauMonNt, nhuCauMonMg, maCoSoTinh);
        return response;
    }
}
