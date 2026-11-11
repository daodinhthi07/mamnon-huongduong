using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Tính nhu cầu nguyên liệu cần mua/xuất kho, từ các dòng ThucDon (đã Include
/// MonAn.DinhLuongNguyenLieus.HangHoa) + số suất ăn tương ứng của khối/nhóm
/// chi phí trong ngày đó. SoSuatAn truyền vào tường minh theo từng dòng thay
/// vì suy ra tự động, vì 1 khối có thể gộp vào nhiều nhóm chi phí khác nhau
/// tùy cách trường tổ chức (VD Nhà trẻ 6-18T và 18-24T cùng chung 1 nhóm
/// chi phí "Nhà trẻ").
/// </summary>
public sealed class NguyenLieuCalculator
{
    private readonly ThucDonDbContext _db;
    private HangHoa? _thitMacDinh;
    private bool _daTimThitMacDinh;

    public NguyenLieuCalculator(ThucDonDbContext db)
    {
        _db = db;
    }

    // ----- Món XẾ (xế Mẫu giáo / chính chiều Nhà trẻ): KHÔNG dùng xương, thay bằng thịt -----
    private static bool LaBuaXe(ThucDonEntity thucDon)
    {
        var ten = thucDon.LoaiBuaAn?.TenBua;
        return string.Equals(ten, "XẾ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ten, "CHÍNH CHIỀU", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LaXuong(string? tenHang) =>
        (tenHang ?? "").Contains("xương", StringComparison.OrdinalIgnoreCase);

    /// <summary>Thịt heo dùng để cộng dồn phần xương bị bỏ (ưu tiên thịt đã có sẵn trong công thức món).</summary>
    private static bool LaThitHeo(string? tenHang)
    {
        var t = (tenHang ?? "").TrimStart();
        return t.StartsWith("Thịt nạc", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Thịt mông", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Thịt vai", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Thịt ba chỉ", StringComparison.OrdinalIgnoreCase);
    }

    private HangHoa? ThitMacDinh()
    {
        if (!_daTimThitMacDinh)
        {
            _thitMacDinh = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == "Thịt nạc vai");
            _daTimThitMacDinh = true;
        }
        return _thitMacDinh;
    }

    /// <summary>Công thức nguyên liệu của món trong dòng thực đơn. Món Xế có nguyên liệu "xương": BỎ xương và CỘNG đúng phần định lượng đó
    /// vào thịt (thịt heo đã có trong công thức món — dòng nhiều nhất; nếu món chưa có thịt thì thêm "Thịt nạc vai").</summary>
    private IEnumerable<(HangHoa HangHoa, decimal DinhLuong)> CongThucMon(ThucDonEntity thucDon, IReadOnlyDictionary<int, List<(HangHoa HangHoa, decimal DinhLuong)>>? riengTheoCho = null)
    {
        // Món có định lượng RIÊNG cho chợ đang tính (PH / ĐT) -> dùng toàn bộ công thức riêng; không thì dùng bảng chung (chuẩn chợ TC).
        var goc = riengTheoCho is not null && riengTheoCho.TryGetValue(thucDon.MonAn!.Id, out var rieng)
            ? rieng.ToList()
            : thucDon.MonAn!.DinhLuongNguyenLieus
                .Select(d => (HangHoa: d.HangHoa, DinhLuong: DinhLuongKhoi(d.DinhLuong, d.DinhLuongNhaTre, LaKhoiNhaTre(thucDon))))
                .Where(d => d.DinhLuong > 0m).ToList();
        if (!LaBuaXe(thucDon)) return goc;
        var xuong = goc.Where(d => LaXuong(d.HangHoa.TenHang)).ToList();
        if (xuong.Count == 0) return goc;

        var tongXuong = xuong.Sum(d => d.DinhLuong);
        var con = goc.Where(d => !LaXuong(d.HangHoa.TenHang)).ToList();
        var viTri = -1;
        var lonNhat = -1m;
        for (var i = 0; i < con.Count; i++)
        {
            if (LaThitHeo(con[i].HangHoa.TenHang) && con[i].DinhLuong > lonNhat) { lonNhat = con[i].DinhLuong; viTri = i; }
        }
        if (viTri >= 0) con[viTri] = (con[viTri].HangHoa, con[viTri].DinhLuong + tongXuong);
        else
        {
            var thit = ThitMacDinh();
            if (thit is null) return goc;   // không tìm thấy mặt hàng thịt để thay -> giữ nguyên công thức
            con.Add((thit, tongXuong));
        }
        return con;
    }

    // ----- Món NHÀ TRẺ KHÔNG ĂN (chỉ Mẫu giáo ăn): bỏ khỏi nhu cầu của khối Nhà trẻ -----
    private static readonly string[] MonNhaTreKhongAn = { "Khoai lang", "Bánh bèo tôm khô", "Khoai tây chiên", "Bánh bao" };
    private int? _nhaTreKhoiId;
    private bool _daTimNhaTreKhoi;

    private bool LaNhaTreKhongAn(ThucDonEntity thucDon)
    {
        var ten = (thucDon.MonAn?.TenMon ?? "").Replace("(xế)", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!MonNhaTreKhongAn.Any(m => string.Equals(m, ten, StringComparison.OrdinalIgnoreCase))) return false;
        return LaKhoiNhaTre(thucDon);
    }

    /// <summary>Dòng thực đơn thuộc khối Nhà trẻ -> dùng cột dinh_luong_nha_tre (nếu có) thay cho định lượng Mẫu giáo.</summary>
    /// <summary>Món tráng miệng là BÁNH (tên món bắt đầu "Bánh": Bánh su kem...) hoặc YAKULT (Yakult / Yakul): dự phòng Mẫu giáo +10 phần, Nhà trẻ +1.</summary>
    private static bool LaBanhHoacYakult(ThucDonEntity thucDon)
    {
        var ten = ChiTietNgayTinhToanService.TenMonHienThi(thucDon.MonAn?.TenMon).Trim();
        return ten.StartsWith("Bánh", StringComparison.OrdinalIgnoreCase) || ten.StartsWith("Yakul", StringComparison.OrdinalIgnoreCase);
    }

    private bool LaKhoiNhaTre(ThucDonEntity thucDon)
    {
        if (!_daTimNhaTreKhoi)
        {
            _nhaTreKhoiId = _db.KhoiHocs.AsNoTracking().Where(k => k.TenKhoi == "Nhà trẻ").Select(k => (int?)k.Id).FirstOrDefault();
            _daTimNhaTreKhoi = true;
        }
        return _nhaTreKhoiId is { } id && thucDon.KhoiId == id;
    }

    /// <summary>Định lượng 1 suất theo khối: Nhà trẻ lấy cột riêng nếu có, không thì dùng chung với Mẫu giáo.</summary>
    private static decimal DinhLuongKhoi(decimal chung, decimal? nhaTre, bool laNhaTre) =>
        laNhaTre && nhaTre.HasValue ? nhaTre.Value : chung;

    /// <summary>Định lượng riêng của chợ <paramref name="maCoSo"/> (PH / ĐT...): món -> danh sách (hàng hoá, lượng 1 suất). Chợ TC hoặc chưa có dữ liệu -> null (dùng bảng chung).</summary>
    private Dictionary<int, List<(HangHoa HangHoa, decimal DinhLuong)>>? LayDinhLuongRieng(string? maCoSo, bool nhaTre)
    {
        var ma = (maCoSo ?? "").Trim();
        if (ma.Equals("DT", StringComparison.OrdinalIgnoreCase)) ma = "ĐT";
        if (ma.Length == 0) return null;
        if (_cacheRieng.TryGetValue((ma, nhaTre), out var daCo)) return daCo;
        var ds = _db.DinhLuongNguyenLieuTheoCoSos.AsNoTracking()
            .Include(x => x.HangHoa).ThenInclude(h => h.Dvt)
            .Where(x => x.MaCoSo == ma).ToList();
        Dictionary<int, List<(HangHoa HangHoa, decimal DinhLuong)>>? kq = null;
        if (ds.Count > 0)
            kq = ds.GroupBy(x => x.MonAnId).ToDictionary(g => g.Key,
                g => g.Select(x => (HangHoa: x.HangHoa, DinhLuong: DinhLuongKhoi(x.DinhLuong, x.DinhLuongNhaTre, nhaTre)))
                      .Where(t => t.DinhLuong > 0m).ToList());
        _cacheRieng[(ma, nhaTre)] = kq;
        return kq;
    }

    // Cache theo (chợ, khối) trong phạm vi 1 request (service Scoped) — tránh truy vấn lặp cho mỗi ngày / mỗi lần tính.
    private readonly Dictionary<(string Ma, bool NhaTre), Dictionary<int, List<(HangHoa HangHoa, decimal DinhLuong)>>?> _cacheRieng = new();

    /// <param name="boSungTrangMieng">Số PHẦN dự phòng cộng thêm cho món TRÁNG MIỆNG chia theo phần (kem flan, sữa chua,
    /// yakult, tàu hủ, chuối… — mặt hàng đếm theo hộp/chai/quả/cái, định mức ≥ 0,5 phần/trẻ). Công thức nhân viên:
    /// số trẻ + dự phòng — Nhà trẻ +1, Mẫu giáo +5 (đúng file mẫu =I7+1, =J7+5); riêng Bánh và Yakult: Nhà trẻ +1, Mẫu giáo +10. Mặt hàng tính theo kg (dưa hấu, thanh long…) không cộng.</param>
    /// <param name="tachTheoMon">true = KHÔNG gộp nguyên liệu giữa các món: mỗi (nguyên liệu, món) là 1 dòng riêng, TenMon = đúng 1 món.
    /// Dùng để tách số lượng đã gộp ra từng món (xem ChiTietNgayTinhToanService.TachTheoMon).</param>
    public IReadOnlyList<KetQuaNguyenLieu> TinhNhuCauNguyenLieu(
        IEnumerable<(ThucDonEntity ThucDon, int SoSuatAn)> thucDonVaSoSuat, int boSungTrangMieng = 0, bool tachTheoMon = false, string? maCoSo = null)
    {
        var riengMg = LayDinhLuongRieng(maCoSo, false);   // định lượng riêng của chợ: khối Mẫu giáo / ăn sáng
        var riengNt = LayDinhLuongRieng(maCoSo, true);    // ... khối Nhà trẻ (cột dinh_luong_nha_tre ?? dinh_luong)
        var tongHop = new Dictionary<(int HangId, int MonId), (HangHoa HangHoa, decimal SoLuong)>();
        var monTheoHang = new Dictionary<(int HangId, int MonId), SortedDictionary<int, string>>();   // (hàng hoá[, món]) -> (thứ tự bữa -> tên món)

        foreach (var (thucDon, soSuatAn) in thucDonVaSoSuat)
        {
            if (thucDon.MonAn is null)
            {
                continue; // Ngày không có món (VD nghỉ lễ)
            }

            if (LaNhaTreKhongAn(thucDon)) continue;   // Nhà trẻ không ăn món này

            foreach (var (hangHoa, dinhLuongMon) in CongThucMon(thucDon, LaKhoiNhaTre(thucDon) ? riengNt : riengMg))
            {
                var canDung = dinhLuongMon * soSuatAn;
                if (boSungTrangMieng > 0 && soSuatAn > 0 && dinhLuongMon >= 0.5m
                    && string.Equals(thucDon.LoaiBuaAn?.TenBua, "TRÁNG MIỆNG", StringComparison.OrdinalIgnoreCase)
                    && !ChiTietNgayTinhToanService.DonViCan(hangHoa.Dvt?.TenDvt))
                    canDung += LaBanhHoacYakult(thucDon) && !LaKhoiNhaTre(thucDon) ? 10 : boSungTrangMieng;   // phần tráng miệng: số trẻ + dự phòng (Bánh, Yakult: Nhà trẻ +1, Mẫu giáo +10)

                var khoa = (hangHoa.Id, tachTheoMon ? thucDon.MonAn.Id : 0);
                tongHop[khoa] = tongHop.TryGetValue(khoa, out var hienCo)
                    ? (hienCo.HangHoa, hienCo.SoLuong + canDung)
                    : (hangHoa, canDung);

                if (!monTheoHang.TryGetValue(khoa, out var dsMon)) { dsMon = new(); monTheoHang[khoa] = dsMon; }
                var thuTu = thucDon.LoaiBuaAn?.ThuTu ?? 99;
                dsMon.TryAdd(thuTu * 100 + dsMon.Count, thucDon.MonAn.TenMon);
            }
        }

        return tongHop
            .Select(kv =>
            {
                var x = kv.Value;
                var dsMon = monTheoHang[kv.Key];
                var ten = string.Join(" + ", dsMon.Values.Distinct().Take(3)) + (dsMon.Values.Distinct().Count() > 3 ? "…" : "");
                return new KetQuaNguyenLieu(x.HangHoa, x.SoLuong, x.SoLuong * (x.HangHoa.DonGiaGanNhat ?? 0m),
                    dsMon.Keys.Min() / 100, ten);
            })
            // Xếp theo THỨ TỰ MÓN trong thực đơn (Canh, Mặn, Xào, Tráng miệng, Xế…), trong cùng món thì mặt hàng tốn tiền nhất lên đầu.
            .OrderBy(x => x.ThuTuMon)
            .ThenByDescending(x => x.ThanhTien)
            .ThenBy(x => x.HangHoa.TenHang)
            .ToList();
    }
}
