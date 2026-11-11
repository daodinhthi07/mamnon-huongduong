using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

/// <summary>Số lượng mục 3 (sáng) + mục 4 (chính + xế) lấy từ 1 ngày trong file Excel mẫu.</summary>
public sealed record KetQuaSoLuongMau(
    List<NguyenLieuMotCotDto> Sang, List<NguyenLieuHaiCotDto> Chinh, int CoSoMauId, DateOnly NgayMau);

/// <summary>
/// QUY TẮC CHUẨN: thực đơn của (cơ sở, ngày) đang soạn TRÙNG HẲN 1 ngày trong file Excel mẫu
/// (cùng món ở cùng bữa, cả Mẫu giáo lẫn Nhà trẻ) thì số lượng thực phẩm = đúng số của ngày mẫu đó:
///   - cùng số suất  -> y nguyên số trong file mẫu;
///   - khác số suất  -> số mẫu × (số suất mới / số suất mẫu), làm tròn như file mẫu.
/// Không còn "cân đối" co giãn theo định mức tiền, nên không bị phình số khi thiếu/thừa món.
///
/// Dữ liệu nằm trong 2 bảng của CSDL:
///   mau_thuc_don        : mỗi (cơ sở, ngày mẫu) 1 dòng — khoá thực đơn Mẫu giáo/Nhà trẻ + số suất của ngày mẫu.
///   so_luong_theo_excel : số lượng từng mặt hàng của ngày mẫu (nhom_chi_phi_id: 1 sáng / 2 nhà trẻ / 3 mẫu giáo;
///                         muc: 2 gia vị / 3 sáng / 4 chính + xế).
/// Nếu thiếu bảng hoặc không tìm thấy mẫu -> trả null và hệ thống tính theo công thức món như cũ.
/// </summary>
public sealed class MauSoLuongService
{
    private readonly ThucDonDbContext _db;

    public MauSoLuongService(ThucDonDbContext db)
    {
        _db = db;
    }

    private sealed class MauRow
    {
        public int CoSoId { get; set; }
        public string Ngay { get; set; } = string.Empty;
        public int SoSuatSang { get; set; }
        public int SoSuatNt { get; set; }
        public int SoSuatMg { get; set; }
    }

    private sealed class SoLuongRow
    {
        public int HangHoaId { get; set; }
        public int NhomChiPhiId { get; set; }
        public int Muc { get; set; }
        public double SoLuong { get; set; }
    }

    private static string Chuan(string? s) =>
        string.Join(" ", (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>Khoá nhận diện thực đơn của 1 khối: "bữa:món|bữa:món|..." (xếp theo thứ tự bữa, chỉ tính bữa có món).</summary>
    public static string KhoaThucDon(IEnumerable<ThucDonEntity> thucDon) =>
        string.Join("|", thucDon
            .Where(t => t.MonAn is not null && t.LoaiBuaAn is not null)
            .OrderBy(t => t.LoaiBuaAn.ThuTu)
            .Select(t => Chuan(t.LoaiBuaAn.TenBua) + ":" + Chuan(ChiTietNgayTinhToanService.TenMonHienThi(t.MonAn!.TenMon))));

    /// <summary>Làm tròn khi đổi số suất, theo đúng quy tắc của MauTuanExcelService: số nguyên (gói/hộp/quả...) tròn lên,
    /// còn lại 0,1 (hoặc 0,01 nếu &lt; 0,15). Mặt hàng cố định theo số trẻ (sữa) giữ nguyên số lẻ.</summary>
    private static decimal DoiSuat(decimal q, int suatMau, int suatMoi, string? dvt, string? tenHang, bool giaVi = false)
    {
        if (suatMoi == suatMau) return q;          // cùng số suất -> y nguyên số mẫu
        if (suatMau <= 0 || suatMoi <= 0) return 0m;
        var x = q * suatMoi / suatMau;
        if (ChiTietNgayTinhToanService.LaMatHangCoDinh(tenHang)) return x;
        if (giaVi) return Math.Round(x, ChiTietNgayTinhToanService.LaDonViGoi(dvt) ? 0 : 1, MidpointRounding.AwayFromZero);
        // GÓI: dưới 0,5 làm tròn xuống, từ 0,5 trở lên làm tròn lên (luôn ra số nguyên).
        if (ChiTietNgayTinhToanService.LaDonViGoi(dvt)) return Math.Round(x, 0, MidpointRounding.AwayFromZero);
        var d = (dvt ?? "").Trim().ToLowerInvariant();
        var nguyen = Math.Abs(q - Math.Round(q)) < 0.000001m && d != "kg" && d != "chai" && d != "lít" && d != "l";
        if (nguyen) return Math.Ceiling(x - 0.000001m);   // các đơn vị đếm khác (hộp, quả...) vẫn tròn lên
        return x >= 0.15m ? Math.Round(x, 1, MidpointRounding.AwayFromZero) : Math.Round(x, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<KetQuaSoLuongMau?> TimAsync(
        int coSoId, IEnumerable<ThucDonEntity> thucDonMg, IEnumerable<ThucDonEntity> thucDonNt,
        int suatSang, int suatNt, int suatMg)
    {
        try
        {
            var khoaMg = KhoaThucDon(thucDonMg);
            var khoaNt = KhoaThucDon(thucDonNt);
            if (khoaMg.Length == 0 && khoaNt.Length == 0) return null;

            var mau = await _db.Database.SqlQueryRaw<MauRow>(
                "SELECT co_so_id AS CoSoId, ngay AS Ngay, so_suat_sang AS SoSuatSang, so_suat_nt AS SoSuatNt, so_suat_mg AS SoSuatMg " +
                "FROM mau_thuc_don WHERE khoa_mg = {0} AND khoa_nt = {1}", khoaMg, khoaNt).ToListAsync();
            if (mau.Count == 0) return null;

            // Ưu tiên cùng cơ sở, rồi mẫu có số suất gần nhất, rồi ngày mới nhất.
            var chon = mau
                .OrderByDescending(m => m.CoSoId == coSoId)
                .ThenBy(m => Math.Abs(m.SoSuatSang - suatSang) + Math.Abs(m.SoSuatNt - suatNt) + Math.Abs(m.SoSuatMg - suatMg))
                .ThenByDescending(m => m.Ngay, StringComparer.Ordinal)
                .First();

            var dong = await _db.Database.SqlQueryRaw<SoLuongRow>(
                "SELECT hang_hoa_id AS HangHoaId, nhom_chi_phi_id AS NhomChiPhiId, muc AS Muc, so_luong AS SoLuong " +
                "FROM so_luong_theo_excel WHERE co_so_id = {0} AND ngay = {1} AND muc IN (3, 4)", chon.CoSoId, chon.Ngay).ToListAsync();
            if (dong.Count == 0) return null;

            var ids = dong.Select(d => d.HangHoaId).Distinct().ToList();
            var hang = await _db.HangHoas.Include(h => h.Dvt).Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id);

            var sang = new List<NguyenLieuMotCotDto>();
            foreach (var d in dong.Where(x => x.Muc == 3 && x.NhomChiPhiId == 1))
            {
                if (!hang.TryGetValue(d.HangHoaId, out var h)) continue;
                var dvt = h.Dvt?.TenDvt;
                var sl = DoiSuat((decimal)d.SoLuong, chon.SoSuatSang, suatSang, dvt, h.TenHang);
                if (sl <= 0) continue;
                var gia = h.DonGiaGanNhat ?? 0m;
                sang.Add(new NguyenLieuMotCotDto(h.TenHang, dvt, sl, gia, sl * gia, h.Id));
            }

            var chinh = new List<NguyenLieuHaiCotDto>();
            foreach (var g in dong.Where(x => x.Muc == 4 && (x.NhomChiPhiId == 2 || x.NhomChiPhiId == 3)).GroupBy(x => x.HangHoaId))
            {
                if (!hang.TryGetValue(g.Key, out var h)) continue;
                var dvt = h.Dvt?.TenDvt;
                decimal nt = 0m, mg = 0m;
                foreach (var d in g)
                {
                    if (d.NhomChiPhiId == 2) nt += DoiSuat((decimal)d.SoLuong, chon.SoSuatNt, suatNt, dvt, h.TenHang);
                    else mg += DoiSuat((decimal)d.SoLuong, chon.SoSuatMg, suatMg, dvt, h.TenHang);
                }
                if (nt <= 0 && mg <= 0) continue;
                var gia = h.DonGiaGanNhat ?? 0m;
                chinh.Add(new NguyenLieuHaiCotDto(h.TenHang, dvt, nt, mg, gia, nt * gia, mg * gia, h.Id));
            }

            if (sang.Count == 0 && chinh.Count == 0) return null;
            return new KetQuaSoLuongMau(sang.OrderBy(x => x.TenHang).ToList(), chinh,
                chon.CoSoId, DateOnly.ParseExact(chon.Ngay, "yyyy-MM-dd"));
        }
        catch (Exception)
        {
            // Chưa có bảng mẫu (CSDL cũ) hoặc lỗi đọc -> bỏ qua, tính theo công thức món như trước.
            return null;
        }
    }
}
