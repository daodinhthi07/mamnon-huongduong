using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Infrastructure.Initializers;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Tinh(): gộp 2 nguồn "nhu cầu nguyên liệu" (nhà trẻ / mẫu giáo — đã tính riêng bằng
/// NguyenLieuCalculator theo công thức món) thành đúng 2 bảng "3. Sáng" (1 cột) và
/// "4. Chính + xế" (2 cột) — dùng để ƯỚC LƯỢNG/xem thử ở trang "Chi tiết ngày" khi
/// đang chọn món, TRƯỚC KHI có phiếu xuất kho thật cho ngày đó.
///
/// LayGiaViThatAsync / LayThucPhamThatAsync: đọc THẲNG từ phiếu xuất kho thật đã lập
/// ở /kho/xuat-kho (KHÔNG tính từ công thức món) — dùng cho Excel và trang
/// "/cho/chi-tiet", là số liệu CHÍNH THỨC/đã ghi sổ.
///
/// Lý do tách 2 cách tính: khi rà lại vì sao mục 2/3/4 xuất Excel không khớp Excel
/// gốc, phát hiện bảng công thức món ăn (dinh_luong_nguyen_lieu) không đủ tin cậy để
/// dùng làm số liệu chính thức — (a) mục 2 (gạo/dầu/muối/bột nêm...) phần lớn KHÔNG
/// gắn với món ăn cụ thể nào nên không tính ra được từ công thức; (b) ngay cả mục 3/4
/// cũng có lỗi trích xuất công thức (VD: ~19-20 món Canh/Cá nấu bị gắn nhầm "Sữa Ellac
/// Multi" làm nguyên liệu — xem Database/11_xoa_gan_nham_sua_vao_mon_canh.sql). Số liệu
/// đáng tin cậy nhất luôn là phiếu xuất kho THẬT do thủ kho lập tay mỗi ngày, nên Excel
/// (báo cáo chính thức) phải đọc từ đó, còn Tinh() (công thức) chỉ còn dùng để ước
/// lượng/lên kế hoạch trước khi lập phiếu.
///
/// TÁCH RIÊNG khỏi ChiTietNgayController để DÙNG CHUNG được với
/// ExcelExportService — nút "Xuất Excel" ở trang Chi tiết ngày phải luôn ra
/// đúng số y hệt số đang hiển thị trên trang, tránh 2 nơi tính hai kiểu rồi
/// lệch nhau như lỗi phân loại "Gia vị" đã gặp trước đó.
/// </summary>
public sealed class ChiTietNgayTinhToanService
{
    private static readonly string[] CacLoaiHanhRauThom = { "Hành lá", "Hành ngò", "Hành tím" };

    private readonly ThucDonDbContext _db;

    public ChiTietNgayTinhToanService(ThucDonDbContext db)
    {
        _db = db;
    }

    public static bool LaHanhRauThom(string? tenHang) =>
        CacLoaiHanhRauThom.Any(ten => (tenHang ?? "").TrimStart().StartsWith(ten, StringComparison.OrdinalIgnoreCase));

    public static bool LaHanhBiAnTaiTc(string? tenHang, string? maCoSo)
    {
        if (!string.Equals(maCoSo?.Trim(), "TC", StringComparison.OrdinalIgnoreCase))
            return false;

        var ten = (tenHang ?? "").TrimStart();
        return ten.StartsWith("Hành lá", StringComparison.OrdinalIgnoreCase)
            || ten.StartsWith("Hành ngò", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LaThucPhamTrongMuc34(HangHoa hangHoa, string? maCoSo) =>
        !LaHanhBiAnTaiTc(hangHoa.TenHang, maCoSo)
        && (hangHoa.LoaiHang != LoaiHangHoa.GiaVi || LaHanhRauThom(hangHoa.TenHang));

    // ----- Làm tròn số lượng như file mẫu: cân/lít = 0,1 ; trứng/hộp/gói/chai... = số nguyên -----
    /// <summary>Đơn vị tính theo CÂN/LÍT (kg, lít, g) — làm tròn 1 chữ số thập phân. Mọi đơn vị khác (gói, chai, hộp,
    /// quả, lon, bộ, cái…) là đơn vị ĐẾM — phải làm tròn LÊN thành số nguyên (không mua được lẻ).</summary>
    public static bool DonViCan(string? dvt)
    {
        var d = (dvt ?? "").Trim().ToLowerInvariant();
        return d is "kg" or "lít" or "l" or "g" or "";   // không rõ đơn vị -> coi như cân
    }

    /// <summary>Quy tắc của nhân viên: số lượng = định mức × số trẻ rồi làm tròn theo đơn vị tính:
    /// kg → 0,1 ; gói/chai/hộp/quả… → tròn lên số nguyên.</summary>
    public static decimal LamTronTheoDvt(decimal sl, string? dvt) =>
        DonViCan(dvt) ? Math.Round(sl, 1, MidpointRounding.AwayFromZero)
        : LaDonViGoi(dvt) ? Math.Round(sl, 0, MidpointRounding.AwayFromZero)   // gói: dưới 0,5 làm tròn xuống, từ 0,5 làm tròn lên
        : Math.Ceiling(sl - 0.000001m);

    /// <summary>Đơn vị tính là "gói": làm tròn số nguyên theo quy tắc thông thường — phần lẻ DƯỚI 0,5 làm tròn xuống,
    /// từ 0,5 trở lên làm tròn lên (các đơn vị đếm khác vẫn luôn tròn lên).</summary>
    public static bool LaDonViGoi(string? dvt)
    {
        var d = (dvt ?? "").Normalize().Trim();
        return string.Equals(d, "gói", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d, "goi", StringComparison.OrdinalIgnoreCase);
    }

    // ===================== QUY TẮC RIÊNG THEO MẶT HÀNG (sửa tại đây khi nhà bếp đổi quy tắc) =====================

    /// <summary>Mì tôm: số GÓI tính theo số trẻ của từng khối (KHÔNG theo định lượng trong công thức món), đối chiếu bảng kê mẫu 27/10:
    /// Nhà trẻ 3 trẻ / 2 gói (2/3 gói/trẻ); Mẫu giáo 2 trẻ / 1 gói (1/2 gói/trẻ). Làm tròn gói theo LamTronTheoDvt.
    /// Chỉ áp dụng khi đơn vị tính của mặt hàng là "Gói".</summary>
    private static readonly (string TienTo, decimal NhaTreMoiTre, decimal MauGiaoMoiTre)[] GoiMiTomTheoSoTre =
    {
        ("Mì tôm", 2m / 3m, 1m / 2m),
        ("Mỳ tôm", 2m / 3m, 1m / 2m),
    };

    /// <summary>Số lượng TỐI ĐA mỗi ngày CHO MỖI CỘT (Ăn sáng / Nhà trẻ / Mẫu giáo): Dầu hào tối đa 1 chai.
    /// (Đơn vị đếm bị làm tròn LÊN nên 2 món cùng dùng dầu hào có thể ra 2 chai.) Muốn giới hạn trên TỔNG cả ngày thì báo để đổi.</summary>
    private static readonly (string TienTo, decimal ToiDaMoiCot)[] GioiHanToiDaMoiCot =
    {
        ("Dầu hào", 1m),
    };

    /// <summary>GẠO (gia vị hằng ngày): Nhà trẻ = số suất × 0,055 kg; Mẫu giáo = số suất × 0,065 kg.
    /// Cột Ăn sáng = gạo nấu CHÁO (chỉ khi sáng hôm đó có món Cháo và có suất ăn sáng), còn không = 0.</summary>
    public const decimal GaoNhaTreMotSuat = 0.055m;
    public const decimal GaoMauGiaoMotSuat = 0.065m;

    /// <summary>Gạo cháo buổi sáng (kg) theo cơ sở: (ngày thường, thứ 7). TC 6/4; PH 5/3,5; ĐT 1/0,5.</summary>
    public static decimal GaoChaoSang(string? maCoSo, DateOnly ngay)
    {
        var thu7 = ngay.DayOfWeek == DayOfWeek.Saturday;
        return (maCoSo ?? "").Trim().ToUpperInvariant() switch
        {
            "TC" => thu7 ? 4m : 6m,
            "PH" => thu7 ? 3.5m : 5m,
            "ĐT" or "DT" => thu7 ? 0.5m : 1m,
            _ => 0m,
        };
    }

    /// <summary>Gạo nấu CHÁO Ở XẾ / chính chiều (kg) cộng vào gạo hằng ngày, theo cơ sở và khối có món Cháo.
    /// TC/PH: Nhà trẻ 1 + Mẫu giáo 5 (chỉ Nhà trẻ có cháo -> 1,5); ĐT: Mẫu giáo 1,5 (Nhà trẻ 0).</summary>
    public static (decimal Nt, decimal Mg) GaoChaoXe(string? maCoSo, bool coNt, bool coMg)
    {
        var ma = (maCoSo ?? "").Trim().ToUpperInvariant();
        if (ma == "TC" || ma == "PH") return (coNt ? (coMg ? 1m : 1.5m) : 0m, coMg ? 5m : 0m);
        if (ma == "ĐT" || ma == "DT") return (0m, coMg ? 1.5m : 0m);
        return (0m, 0m);
    }

    /// <summary>Nguyên liệu nấu cháo sáng theo cơ sở (kg/chai): 387 nếp hương, 376 đậu xanh XK không vỏ, 138 cà rốt Đà lạt (TC 2; PH 2,5; ĐT 0,5),
    /// 694 dầu mè, 443 dầu gấc (TC/PH 2 chai; ĐT 1 chai).</summary>
    public static IEnumerable<(int HangHoaId, decimal SoLuong)> NguyenLieuChaoSang(string? maCoSo)
    {
        var ma = (maCoSo ?? "").Trim().ToUpperInvariant();
        var dt = ma is "ĐT" or "DT";
        var kg = dt ? 0.5m : (ma == "PH" ? 2.5m : 2m);
        var chai = dt ? 1m : 2m;
        // Chợ TC: món cháo KHÔNG dùng cà rốt (138).
        // Đậu xanh (376) KHÔNG còn là nguyên liệu mặc định của cháo: nếu công thức món có đậu xanh thì số lượng tính theo công thức món.
        if (ma == "TC") return new[] { (387, kg), (694, chai), (443, chai) };
        return new[] { (387, kg), (138, kg), (694, chai), (443, chai) };
    }

    public static bool LaHangGao(string? tenHang) => BatDauBang(tenHang, "Gạo");

    private static bool BatDauBang(string? ten, string tienTo) =>
        (ten ?? "").Normalize().TrimStart().StartsWith(tienTo.Normalize(), StringComparison.OrdinalIgnoreCase);

    /// <summary>CỐ ĐỊNH vị trí ở bữa SÁNG: hành tím, hành ngò, hành tây, ngò (nếu có) luôn nằm DƯỚI CÙNG danh sách nguyên liệu ăn sáng,
    /// theo đúng thứ tự này. Các nguyên liệu còn lại giữ nguyên thứ tự cũ. Dùng ở mọi nơi dựng danh sách sáng
    /// (màn hình, phiếu đã lưu, xuất Excel) để thứ tự luôn giống nhau.</summary>
    public static List<NguyenLieuMotCotDto> XepHanhNgoXuongCuoiAnSang(IEnumerable<NguyenLieuMotCotDto> ds)
    {
        static int Hang(string? ten)
        {
            if (BatDauBang(ten, "Hành tím")) return 1;
            if (BatDauBang(ten, "Hành ngò")) return 2;
            if (BatDauBang(ten, "Hành tây")) return 3;
            if (BatDauBang(ten, "Ngò")) return 4;
            return 0;   // nguyên liệu thường
        }
        return ds
            .Select((x, i) => (Dong: x, Idx: i, H: Hang(x.TenHang)))
            .OrderBy(p => p.H == 0 ? 0 : 1)
            .ThenBy(p => p.H)
            .ThenBy(p => p.Idx)
            .Select(p => p.Dong)
            .ToList();
    }

    /// <summary>Số gói mì tôm theo số trẻ; null = mặt hàng này không theo quy tắc (dùng số tính từ công thức).</summary>
    private static decimal? SoGoiTheoSoTre(HangHoa hh, bool laMauGiao, int soSuat)
    {
        if (soSuat <= 0 || !LaDonViGoi(hh.Dvt?.TenDvt)) return null;
        foreach (var r in GoiMiTomTheoSoTre)
            if (BatDauBang(hh.TenHang, r.TienTo)) return soSuat * (laMauGiao ? r.MauGiaoMoiTre : r.NhaTreMoiTre);
        return null;
    }

    /// <summary>Dầu hào: tối đa 1 chai CẢ NGÀY cho mỗi chợ (bảng kê mẫu 27/10: Nhà trẻ 0,2 + Mẫu giáo 0,8; chỉ 1 khối dùng thì khối đó 1 chai).
    /// Điểm trường không dùng (bảng kê mẫu ghi 0).</summary>
    public static bool LaDauHao(string? tenHang) => BatDauBang(tenHang, "Dầu hào");

    /// <summary>Mì / Mỳ / Bún / Phở TƯƠI tính theo kg: số lượng = số suất × tỷ lệ (ăn sáng 0,11; ăn chính Nhà trẻ + Mẫu giáo 0,125 kg/suất), LÀM TRÒN XUỐNG số nguyên kg.
    /// Không phụ thuộc định lượng trong công thức món, không bị nhích khi cân đối.</summary>
    public const decimal SoiTuoiAnSang = 0.11m;
    public const decimal SoiTuoiAnChinh = 0.125m;
    public static bool LaSoiTuoi(string? tenHang, string? dvt) =>
        DonViCan(dvt) && (tenHang ?? "").Contains("tươi", StringComparison.OrdinalIgnoreCase)
        && (BatDauBang(tenHang, "Mì") || BatDauBang(tenHang, "Mỳ") || BatDauBang(tenHang, "Bún") || BatDauBang(tenHang, "Phở"));
    public static decimal SoiTuoiTheoSuat(int soSuat, decimal tyLe) => Math.Floor(Math.Max(0, soSuat) * tyLe);

    /// <summary>Chả (kg) ở bữa SÁNG và XẾ: tối đa 3 kg mỗi cột và làm tròn số nguyên kg (phần lẻ dưới 0,5 giữ nguyên số 1 chữ số thập phân để không về 0).</summary>
    public static bool LaChaCan(string? tenHang, string? dvt) => BatDauBang(tenHang, "Chả") && DonViCan(dvt);
    public const decimal ChaToiDaKg = 3m;
    public static decimal ApChaToiDa(decimal sl)
    {
        if (sl <= 0) return sl;
        var r = Math.Min(ChaToiDaKg, Math.Round(sl, 0, MidpointRounding.AwayFromZero));
        return r > 0 ? r : Math.Min(ChaToiDaKg, sl);
    }

    /// <summary>Món "Canh rau ...": mỗi nguyên liệu rau (kg) tối đa 20 kg mỗi cột.</summary>
    public const decimal CanhRauToiDaKg = 20m;
    public static bool LaRauCanhRau(string? tenHang, string? tenMon, string? dvt) =>
        DonViCan(dvt) && BatDauBang(tenHang, "Rau") && TenMonHienThi(tenMon).Trim().StartsWith("Canh rau", StringComparison.OrdinalIgnoreCase);

    private static decimal ApGioiHanToiDa(string? tenHang, decimal sl)
    {
        foreach (var g in GioiHanToiDaMoiCot)
            if (BatDauBang(tenHang, g.TienTo)) return Math.Min(sl, g.ToiDaMoiCot);
        return sl;
    }

    /// <summary>Mặt hàng ĐÚNG MỖI NGÀY, tính THẲNG theo số trẻ của từng khối, KHÔNG phụ thuộc món Xế nào được
    /// chọn (file mẫu: Sữa Ellac Multi = số trẻ × 3.000đ ÷ đơn giá = 0,0125 kg/trẻ). Nếu để trong công thức món
    /// Xế thì ngày chưa chọn món Xế (hoặc chọn riêng 1 khối) sẽ mất sữa / sai số lượng.</summary>
    private static readonly (string TenHang, decimal DinhLuongMotTre)[] MatHangMoiTre =
    {
        ("Sữa Ellac Multi", 0.0125m),
    };

    // ----- HÀNH TÍM CỐ ĐỊNH mỗi ngày (không tính theo công thức món, không nhân số suất) -----
    /// <summary>Số kg hành tím cố định mỗi ngày cho (Ăn sáng, Nhà trẻ, Mẫu giáo): TC và PH = 0,2 / 0,1 / 0,2; ĐT = 0,1 / 0,1 / 0,1.</summary>
    private static (decimal Sang, decimal NhaTre, decimal MauGiao) HanhTimCoDinhMoiNgay(string? maCoSo)
    {
        var ma = (maCoSo ?? "").Trim();
        var laDt = ma.Equals("ĐT", StringComparison.OrdinalIgnoreCase) || ma.Equals("DT", StringComparison.OrdinalIgnoreCase);
        return laDt ? (0.1m, 0.1m, 0.1m) : (0.2m, 0.1m, 0.2m);
    }

    // ----- CHUỐI theo cơ sở: nếu thực đơn có món chuối thì CHỈ dùng 1 loại chuối tuỳ cơ sở -----
    /// <summary>Trường chính (TC) dùng chuối TIÊU; Phân hiệu (PH) và Điểm trường (ĐT) dùng chuối XANH (mặt hàng "Chuối lớn 7h" — loại chuối PH/ĐT
    /// vẫn mua theo file Excel mẫu). Đổi tên ở đây nếu nhà bếp đổi loại chuối.</summary>
    private const string TenChuoiTieu = "Chuối tiêu 7h";
    private const string TenChuoiXanh = "Chuối lớn 7h";

    /// <summary>Chuối tính theo QUẢ (loại "Chuối…" có đơn vị Quả). Chuối chát/chuối xanh bán theo kg, tinh dầu chuối, bắp chuối… không tính.</summary>
    private static bool LaChuoiQua(string? tenHang, string? dvt) =>
        BatDauBang(tenHang, "Chuối") && string.Equals((dvt ?? "").Trim(), "Quả", StringComparison.OrdinalIgnoreCase);

    /// <summary>Số quả chuối mỗi ngày (Nhà trẻ, Mẫu giáo) theo cơ sở, sl = số suất của khối:
    ///   TC (chuối tiêu): Nhà trẻ = sl + 1; Mẫu giáo = sl + 10.
    ///   PH, ĐT (chuối xanh): Nhà trẻ = sl / 2 + 3; Mẫu giáo = sl / 2 + 10 (làm tròn lên số quả).</summary>
    public static (decimal NhaTre, decimal MauGiao) SoLuongChuoi(string? maCoSo, int soSuatNhaTre, int soSuatMauGiao) =>
        string.Equals((maCoSo ?? "").Trim(), "TC", StringComparison.OrdinalIgnoreCase)
            ? (soSuatNhaTre + 1m, soSuatMauGiao + 10m)
            : (Math.Ceiling(soSuatNhaTre / 2m + 3m), Math.Ceiling(soSuatMauGiao / 2m + 10m));

    // ----- MÓN SÚP: bột năng / bột bán MẶC ĐỊNH mỗi ngày theo cơ sở, CHIA CỐ ĐỊNH cho Nhà trẻ / Mẫu giáo (kg cả ngày) -----
    /// <summary>Khi ngày có món Súp (mỗi mặt hàng: Nhà trẻ + Mẫu giáo = tổng của chợ):
    ///   TC: bột năng 0,9 + 4,1 = 5 kg; không có bột bán.
    ///   PH: bột năng 0,2 + 0,8 = 1 kg; bột bán 0,9 + 4,1 = 5 kg.
    ///   ĐT: bột năng 0,1 + 0,1 = 0,2 kg; bột bán 0,2 + 0,8 = 1 kg.
    /// Số này THAY phần bột năng/bột bán do các món Súp tính từ công thức; các món khác (không phải Súp) vẫn giữ phần của mình.
    /// Nếu chỉ MỘT khối có món Súp thì khối đó nhận cả tổng của chợ.</summary>
    private static (decimal NtBotNang, decimal MgBotNang, decimal NtBotBan, decimal MgBotBan)? SupMacDinh(string? maCoSo) =>
        (maCoSo ?? "").Trim().ToUpperInvariant() switch
        {
            "TC" => (0.9m, 4.1m, 0m, 0m),
            "PH" => (0.2m, 0.8m, 0.9m, 4.1m),
            "ĐT" or "DT" => (0.1m, 0.1m, 0.2m, 0.8m),
            _ => null,
        };

    // ----- CHÁO XẾ / CHÍNH CHIỀU: nếp hương, đậu xanh, cà rốt CỐ ĐỊNH mỗi ngày theo cơ sở (giống cháo sáng — KHÔNG nhân số suất) -----
    /// <summary>Khi ngày có món Cháo ở xế (Mẫu giáo) / chính chiều (Nhà trẻ): MỖI loại nếp hương (387), đậu xanh XK không vỏ (376), cà rốt Đà lạt (138) (và dầu mè 694, dầu gấc 443: 2 chai TC/PH, 1 chai ĐT)
    /// có số kg cố định cả ngày bằng số của cháo sáng: TC 2 kg; PH 2,5 kg; ĐT 0,5 kg. Chia cho 2 khối (Nhà trẻ + Mẫu giáo = tổng của chợ):
    ///   TC: 0,5 + 1,5;  PH: 0,5 + 2;  ĐT: 0,1 + 0,4.   Nếu chỉ MỘT khối có món Cháo thì khối đó nhận cả tổng của chợ.
    /// Số này THAY phần nếp/đậu xanh/cà rốt do các món Cháo tính từ công thức; các món khác (không phải Cháo) vẫn giữ phần của mình.</summary>
    private static readonly int[] HangChaoCoDinh = { 387, 138 };   // đậu xanh (376) bỏ khỏi danh sách mặc định — chỉ có khi nằm trong công thức món

    private static (decimal Nt, decimal Mg)? ChaoXeMacDinh(string? maCoSo) =>
        (maCoSo ?? "").Trim().ToUpperInvariant() switch
        {
            "TC" => (0.5m, 1.5m),   // TC: tổng 2 kg (nếp, đậu xanh, cà rốt) — khớp bảng kê mẫu
            "PH" => (0.5m, 2m),
            "ĐT" or "DT" => (0.1m, 0.4m),
            _ => null,
        };

    private static bool LaMonChao(string? tenMon) =>
        TenMonHienThi(tenMon).StartsWith("Cháo", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tên món (có thể gộp nhiều món nối bằng "+") có món Phở.</summary>
    private static bool LaMonPhoGop(string? tenMon) =>
        (tenMon ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(m => TenMonHienThi(m).StartsWith("Phở", StringComparison.OrdinalIgnoreCase));

    /// <summary>Hành tây CỦA MÓN PHỞ — số cố định, không bị nhích khi cân đối.</summary>
    public static bool LaHanhTayPho(string? tenHang, string? tenMon) =>
        string.Equals((tenHang ?? "").Trim(), "Hành Tây", StringComparison.OrdinalIgnoreCase) && LaMonPhoGop(tenMon);

    private static bool LaMonSup(string? tenMon) =>
        TenMonHienThi(tenMon).StartsWith("Súp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mặt hàng "Hành tím" (đúng tên) — dòng cố định, KHÔNG bị co giãn / nhích khi cân đối định mức.</summary>
    public static bool LaHanhTimCoDinh(string? tenHang) =>
        string.Equals((tenHang ?? "").Trim(), "Hành tím", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dòng không được co giãn / nhích khi cân đối "Chênh lệch cuối ngày": sữa cố định + hành tím cố định.</summary>
    /// <summary>Mì/Mỳ/Bún/Phở tươi (theo tên): số lượng cố định theo suất nên không cân đối.</summary>
    private static bool LaSoiTuoiTheoTen(string? tenHang) =>
        (tenHang ?? "").Contains("tươi", StringComparison.OrdinalIgnoreCase)
        && (BatDauBang(tenHang, "Mì") || BatDauBang(tenHang, "Mỳ") || BatDauBang(tenHang, "Bún") || BatDauBang(tenHang, "Phở"));

    public static bool LaDongKhongCanDoi(string? tenHang, string? tenMon = null) => LaNguyenLieuChaoCoDinh(tenHang, tenMon) || LaMatHangCoDinh(tenHang) || LaHanhTimCoDinh(tenHang) || LaBotChienDaiHan(tenHang) || LaBotSup(tenHang) || LaMatHangCoDinhKhiCo(tenHang) || LaDongCoDinhTheoMon(tenHang, tenMon) || LaChaiMotMucBon(tenHang) || LaBanhCoGiaSan(tenHang) || LaHanhTayPho(tenHang, tenMon) || LaDauHao(tenHang) || LaSoiTuoiTheoTen(tenHang);

    /// <summary>Nước mắm / dầu ăn / xì dầu ở mục 4 (nguyên liệu chính + xế): CHỈ 1 chai, để ở cột Mẫu giáo (Nhà trẻ = 0). Không tính dầu hào, dầu mè, dầu gấc, dầu dấm (có quy tắc riêng).</summary>
    public static bool LaChaiMotMucBon(string? tenHang)
    {
        var t = (tenHang ?? "").Trim();
        if (BatDauBang(t, "Nước mắm") || BatDauBang(t, "Xì dầu")) return true;
        if (!BatDauBang(t, "Dầu")) return false;
        return !new[] { "hào", "mè", "gấc", "dấm" }.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Dòng của đúng (mặt hàng, món) có quy tắc THEO MÓN (bảng quy_tac_nguyen_lieu, cột mon_an_id) — số cố định, không bị nhích khi cân đối
    /// và không bị gộp/chia lại ở bước tách món.</summary>
    public static bool LaDongCoDinhTheoMon(string? tenHang, string? tenMon)
    {
        var ds = QuyTacCache.TheoMon;
        if (ds.Count == 0) return false;
        var h = (tenHang ?? "").Trim();
        var m = (tenMon ?? "").Trim();
        if (h.Length == 0 || m.Length == 0) return false;
        return ds.Any(p => string.Equals(p.TenHang, h, StringComparison.OrdinalIgnoreCase) && string.Equals(p.TenMon, m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Bánh có giá tiền sẵn (tên hàng bắt đầu "Bánh"): dùng đúng giá/số lượng theo suất, KHÔNG bị co giãn khi cân đối và không tính vào trần xế.</summary>
    public static bool LaBanhCoGiaSan(string? tenHang) => (tenHang ?? "").TrimStart().StartsWith("Bánh", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nếp hương / đậu xanh / cà rốt / dầu mè / dầu gấc CỦA MÓN CHÁO: số cố định theo bảng từng chợ — không bị nhích khi cân đối. Chỉ khóa dòng thuộc món Cháo
    /// (tenMon bắt đầu bằng "Cháo"), cà rốt/dầu của món khác vẫn cân đối bình thường.</summary>
    public static bool LaNguyenLieuChaoCoDinh(string? tenHang, string? tenMon) =>
        TenMonHienThi(tenMon).StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)
        && (BatDauBang(tenHang, "Nếp hương") || BatDauBang(tenHang, "Cà rốt")
            || BatDauBang(tenHang, "Dầu mè") || BatDauBang(tenHang, "Dầu gấc"));

    /// <summary>Hành ngò / hành lá / me chua / đường vàng: số cố định theo chợ (xem CoDinhKhiCo trong Tinh) — không bị nhích khi cân đối.</summary>
    public static bool LaMatHangCoDinhKhiCo(string? tenHang) =>
        QuyTacCache.TienTo.Count > 0
            ? QuyTacCache.TienTo.Any(t => BatDauBang(tenHang, t))   // danh sách lấy từ bảng quy tắc (trang /quy-tac-nguyen-lieu)
            : BatDauBang(tenHang, "Hành ngò") || BatDauBang(tenHang, "Hành lá") || BatDauBang(tenHang, "Me chua") || BatDauBang(tenHang, "Đường vàng");

    /// <summary>Bột năng / bột bán của món Súp: số cố định theo bảng từng chợ (SupMacDinh) — KHÔNG được nhích/co giãn khi cân đối "Chênh lệch cuối ngày".</summary>
    public static bool LaBotSup(string? tenHang) =>
        string.Equals((tenHang ?? "").Trim(), "Bột năng không bao bì", StringComparison.OrdinalIgnoreCase)
        || string.Equals((tenHang ?? "").Trim(), "Bột bán (10 kg/bao)", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bột chiên Đại Hàn (món Cá hồi chiên mắm tỏi): CỐ ĐỊNH 2 gói/ngày ở TC và PH (1 gói Nhà trẻ + 1 gói Mẫu giáo); ĐT không dùng.</summary>
    public static bool LaBotChienDaiHan(string? tenHang) =>
        (tenHang ?? "").Trim().StartsWith("Bột chiên Đại Hàn", StringComparison.OrdinalIgnoreCase);

    private static bool LaHanhTimBatKy(string? tenHang) =>
        (tenHang ?? "").TrimStart().StartsWith("Hành tím", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mặt hàng tính CỐ ĐỊNH theo số trẻ (VD Sữa Ellac Multi): KHÔNG làm tròn 0,1 và KHÔNG bị nhích/co giãn
    /// khi cân đối định mức — file mẫu giữ nguyên số lẻ (VD 0,875 / 4,25 kg).</summary>
    public static bool LaMatHangCoDinh(string? tenHang) =>
        MatHangMoiTre.Any(m => string.Equals(m.TenHang, tenHang?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tên món để IN RA: bỏ hậu tố " (xế)" / " (sáng)" — chỉ dùng trong CSDL để tên món không trùng giữa các nhóm bữa.</summary>
    public static string TenMonHienThi(string? ten) =>
        (ten ?? "").Replace(" (xế)", "", StringComparison.OrdinalIgnoreCase).Replace(" (sáng)", "", StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary>Sắp các dòng mục 4 đọc từ phiếu đã lưu theo THỨ TỰ MÓN trong thực đơn (Canh, Mặn, Xào, Tráng miệng, Xế…) như
    /// file mẫu, thay vì theo tên mặt hàng; sữa và các dòng không thuộc món nào (còn sót) xếp cuối. Vị trí lấy từ bảng ước lượng
    /// theo thực đơn (so khớp theo tên đã chuẩn hoá). Số lượng của phiếu giữ nguyên.</summary>
    public static List<NguyenLieuHaiCotDto> SapXepTheoThucDon(
        IReadOnlyList<NguyenLieuHaiCotDto> daLuu, IReadOnlyList<NguyenLieuHaiCotDto> uocLuong)
    {
        static string Khoa(string? t) => string.Join(" ", (t ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var viTri = new Dictionary<string, (int Idx, NguyenLieuHaiCotDto Dong)>();
        for (var i = 0; i < uocLuong.Count; i++) viTri.TryAdd(Khoa(uocLuong[i].TenHang), (i, uocLuong[i]));

        return daLuu
            .Select((x, i) =>
            {
                var coDinh = LaMatHangCoDinh(x.TenHang);
                if (!coDinh && viTri.TryGetValue(Khoa(x.TenHang), out var u))
                    return (Dong: x with { ThuTuMon = u.Dong.ThuTuMon, TenMon = u.Dong.TenMon }, ViTri: u.Idx);
                return (Dong: x, ViTri: (coDinh ? 2_000_000 : 1_000_000) + i);
            })
            .OrderBy(p => p.ViTri)
            .Select(p => p.Dong)
            .ToList();
    }

    /// <summary>Phát hiện số "đã lưu cố định" (mục 4) có thể đã CŨ so với thực đơn hiện tại: mặt hàng mà ước lượng
    /// theo thực đơn có số lượng cho 1 khối nhưng bảng đã lưu thiếu dòng hoặc đang bằng 0 (VD phiếu lưu từ lúc
    /// Mẫu giáo chưa chọn món xế). Trả null nếu không thấy gì lệch.</summary>
    public static string? PhatHienPhieuCu(
        IReadOnlyList<NguyenLieuHaiCotDto> uocLuong, IReadOnlyList<NguyenLieuHaiCotDto> daLuu)
    {
        // So khớp theo TÊN đã chuẩn hoá (bỏ khoảng trắng thừa, không phân biệt hoa/thường) chứ không theo Id, vì
        // danh mục có thể có 2 mặt hàng trùng tên (VD "Cá khế  y con" và "Cá khế y con").
        static string Khoa(string? t) => string.Join(" ", (t ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var daLuuTheoTen = daLuu.GroupBy(x => Khoa(x.TenHang))
            .ToDictionary(g => g.Key, g => (Nt: g.Sum(x => x.SlNhaTre), Mg: g.Sum(x => x.SlMauGiao)));
        var loi = new List<string>();
        foreach (var u in uocLuong)
        {
            var co = daLuuTheoTen.TryGetValue(Khoa(u.TenHang), out var l);
            if (u.SlNhaTre > 0 && (!co || l.Nt == 0)) loi.Add($"{u.TenHang.Trim()} (Nhà trẻ)");
            if (u.SlMauGiao > 0 && (!co || l.Mg == 0)) loi.Add($"{u.TenHang.Trim()} (Mẫu giáo)");
        }
        // Ngược lại: dòng ĐANG CÓ SỐ trong phiếu nhưng thực đơn hiện tại không còn dùng (còn sót từ công thức món cũ).
        var coTrongThucDon = new HashSet<string>(uocLuong.Where(u => u.SlNhaTre > 0 || u.SlMauGiao > 0).Select(u => Khoa(u.TenHang)));
        var thua = daLuu.Where(l => (l.SlNhaTre > 0 || l.SlMauGiao > 0) && !coTrongThucDon.Contains(Khoa(l.TenHang)))
            .Select(l => l.TenHang.Trim()).Distinct().ToList();

        if (loi.Count == 0 && thua.Count == 0) return null;
        var phan = new List<string>();
        if (loi.Count > 0)
            phan.Add("đang thiếu/bằng 0 so với thực đơn hiện tại ở: " + string.Join(", ", loi.Take(6)) + (loi.Count > 6 ? $"… (+{loi.Count - 6})" : ""));
        if (thua.Count > 0)
            phan.Add("còn dòng mà thực đơn hiện tại không dùng: " + string.Join(", ", thua.Take(6)) + (thua.Count > 6 ? $"… (+{thua.Count - 6})" : ""));
        return "Số đã lưu cố định " + string.Join("; ", phan)
            + ". Nếu không phải do bạn chủ ý sửa/xóa thì bấm \"Đặt lại theo thực đơn\" để tính lại.";
    }

    // ---------------------------------------------------------------------------------------
    // QUY TẮC NGUYÊN LIỆU CỐ ĐỊNH — đọc từ bảng quy_tac_nguyen_lieu (sửa ở trang /quy-tac-nguyen-lieu)
    // ---------------------------------------------------------------------------------------
    private List<QuyTacNguyenLieu> LayQuyTac(int cheDo) =>
        _db.QuyTacNguyenLieus.AsNoTracking().Include(q => q.HangHoa).ThenInclude(h => h.Dvt)
            .Where(q => q.IsActive && q.CheDo == cheDo && q.MonAnId == null).ToList();   // MonAnId != null = quy tắc THEO MÓN (LayQuyTacTheoMon)

    /// <summary>Quy tắc THEO MÓN (mon_an_id có giá trị): khi món đó có trong thực đơn, phần mặt hàng do chính món đó sinh ra = số cố định.</summary>
    private List<QuyTacNguyenLieu> LayQuyTacTheoMon() =>
        _db.QuyTacNguyenLieus.AsNoTracking().Include(q => q.HangHoa).ThenInclude(h => h.Dvt).Include(q => q.MonAn)
            .Where(q => q.IsActive && q.CheDo == QuyTacNguyenLieu.KhiCoTrongThucDon && q.MonAnId != null).ToList();

    /// <summary>Với mỗi (mặt hàng, món): chọn quy tắc khớp (cơ sở, loại ngày) và CỤ THỂ nhất.</summary>
    private static IEnumerable<QuyTacNguyenLieu> ChonQuyTacTheoMon(IEnumerable<QuyTacNguyenLieu> ds, string? maCoSo, DateOnly? ngay) =>
        ds.Where(q => q.KhopCoSo(maCoSo) && q.KhopNgay(ngay))
            .GroupBy(q => (q.HangHoaId, q.MonAnId))
            .Select(g => g.OrderByDescending(q => q.DoCuThe).ThenByDescending(q => q.Id).First())
            .ToList();

    /// <summary>Với mỗi mặt hàng: chọn quy tắc khớp (cơ sở, loại ngày) và CỤ THỂ nhất. Mặt hàng không có quy tắc nào khớp thì không có trong kết quả.</summary>
    private static Dictionary<int, QuyTacNguyenLieu> ChonQuyTac(IEnumerable<QuyTacNguyenLieu> ds, string? maCoSo, DateOnly? ngay) =>
        ds.Where(q => q.KhopCoSo(maCoSo) && q.KhopNgay(ngay))
            .GroupBy(q => q.HangHoaId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.DoCuThe).ThenByDescending(q => q.Id).First());

    // ---------------------------------------------------------------------------------------
    // ĐIỂM TRƯỜNG (ĐT): chỉ ăn ỨC GÀ, không ăn thịt gà tam hoàng
    // ---------------------------------------------------------------------------------------
    private const string TenUcGaDiemTruong = "Thịt ức gà";

    public static bool LaDiemTruong(string? maCoSo)
    {
        var ma = (maCoSo ?? "").Trim();
        return ma.Equals("ĐT", StringComparison.OrdinalIgnoreCase) || ma.Equals("DT", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LaGaTamHoang(string? tenHang) =>
        (tenHang ?? "").TrimStart().StartsWith("Thịt gà tam hoàng", StringComparison.OrdinalIgnoreCase);

    private HangHoa? LayUcGa() =>
        _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == TenUcGaDiemTruong);

    private static IReadOnlyList<KetQuaNguyenLieu> DoiGaTamHoangSangUcGa(IReadOnlyList<KetQuaNguyenLieu> ds, HangHoa ucGa) =>
        ds.Select(k => LaGaTamHoang(k.HangHoa.TenHang)
                ? k with { HangHoa = ucGa, ThanhTien = k.TongCanDung * (ucGa.DonGiaGanNhat ?? 0m) }
                : k)
            .ToList();

    // ---------------------------------------------------------------------------------------
    // PH + ĐT mua MẶT HÀNG KHÁC với TC cho một số nguyên liệu (cùng công thức món, đổi mặt hàng + quy đổi số lượng)
    //   Rong biển khô (kg)  -> Rong biển khô Hàn Quốc 50gr (gói): 1 kg ≈ 7,9 gói (theo bảng kê 07/10/2026)
    //   Sườn non (kg)       -> Sườn non chặt nhỏ (kg): cùng số lượng
    //   Chuối tiêu 7h (quả) -> Chuối lớn 7h (quả): 1 quả chuối tiêu ≈ 0,5 quả chuối lớn (theo bảng kê PH/ĐT 4 tuần T10–T11)
    // ---------------------------------------------------------------------------------------
    private static readonly (int Tu, int Sang, decimal HeSo)[] DoiMatHangPhDt = { (389, 702, 7.9m), (5, 6, 1m), (312, 310, 0.5m) };

    public static bool LaPhongHoc(string? maCoSo) => string.Equals((maCoSo ?? "").Trim(), "PH", StringComparison.OrdinalIgnoreCase);

    // Mặt hàng chợ TC KHÔNG ăn: vẫn nằm trong công thức món (PH, ĐT dùng) nhưng không xuất ở bảng kê chợ TC.
    private static readonly string[] MatHangTcKhongAn = { "Óc heo" };

    private static IReadOnlyList<KetQuaNguyenLieu> BoMatHangTcKhongAn(IReadOnlyList<KetQuaNguyenLieu> ds, string? maCoSo) =>
        string.Equals((maCoSo ?? "").Trim(), "TC", StringComparison.OrdinalIgnoreCase)
            ? ds.Where(k => !MatHangTcKhongAn.Any(t => BatDauBang(k.HangHoa.TenHang, t))).ToList()
            : ds;

    // Chợ TC: các món BÁNH CANH không dùng cà rốt (138) — bảng chung (TC) vẫn có cà rốt, PH / ĐT giữ nguyên.
    private static bool LaMonBanhCanh(string? tenMon) =>
        TenMonHienThi(tenMon).Trim().StartsWith("Bánh canh", StringComparison.OrdinalIgnoreCase);

    /// <summary>TC: bỏ cà rốt của món Bánh canh. <paramref name="theoMon"/> = nhu cầu tách từng món (nếu có) để trừ đúng phần của món Bánh canh
    /// khỏi dòng đã gộp nhiều món; không có thì chỉ bỏ dòng mà tên món là đúng 1 món Bánh canh.</summary>
    private static IReadOnlyList<KetQuaNguyenLieu> BoCaRotBanhCanhTc(IReadOnlyList<KetQuaNguyenLieu> ds, IReadOnlyList<KetQuaNguyenLieu>? theoMon, string? maCoSo)
    {
        if (!string.Equals((maCoSo ?? "").Trim(), "TC", StringComparison.OrdinalIgnoreCase)) return ds;
        const int CaRotId = 138;
        var phanBanhCanh = theoMon?.Where(k => k.HangHoa.Id == CaRotId && LaMonBanhCanh(k.TenMon)).Sum(k => k.TongCanDung) ?? 0m;
        var ketQua = new List<KetQuaNguyenLieu>(ds.Count);
        foreach (var k in ds)
        {
            if (k.HangHoa.Id != CaRotId) { ketQua.Add(k); continue; }
            var ten = k.TenMon ?? "";
            if (theoMon is null || theoMon.Count == 0)
            {
                if (!ten.Contains('+') && LaMonBanhCanh(ten)) continue;   // dòng của đúng 1 món Bánh canh -> bỏ
                ketQua.Add(k); continue;
            }
            if (phanBanhCanh <= 0m) { ketQua.Add(k); continue; }
            var con = Math.Max(0m, k.TongCanDung - phanBanhCanh);
            phanBanhCanh = Math.Max(0m, phanBanhCanh - k.TongCanDung);
            if (con > 0m) ketQua.Add(k with { TongCanDung = con, ThanhTien = k.TongCanDung > 0m ? k.ThanhTien * con / k.TongCanDung : 0m });
        }
        return ketQua;
    }

    private IReadOnlyList<KetQuaNguyenLieu> DoiMatHangTheoCoSo(IReadOnlyList<KetQuaNguyenLieu> ds, string? maCoSo)
    {
        if (!(LaPhongHoc(maCoSo) || LaDiemTruong(maCoSo)) || ds.Count == 0) return ds;
        var moi = new Dictionary<int, HangHoa>();
        foreach (var d in DoiMatHangPhDt)
            if (ds.Any(k => k.HangHoa.Id == d.Tu) && _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == d.Sang) is { } hh)
                moi[d.Tu] = hh;
        if (moi.Count == 0) return ds;
        return ds.Select(k =>
        {
            if (!moi.TryGetValue(k.HangHoa.Id, out var hhMoi)) return k;
            var heSo = DoiMatHangPhDt.First(d => d.Tu == k.HangHoa.Id).HeSo;
            var sl = k.TongCanDung * heSo;
            return k with { HangHoa = hhMoi, TongCanDung = sl, ThanhTien = sl * (hhMoi.DonGiaGanNhat ?? 0m) };
        }).ToList();
    }

    // ---------------------------------------------------------------------------------------
    // TÁCH NGUYÊN LIỆU THEO TỪNG MÓN (mục 4)
    // ---------------------------------------------------------------------------------------
    /// <summary>Chia số lượng <paramref name="q"/> (đã làm tròn) ra các món theo tỷ lệ <paramref name="w"/> (nhu cầu gốc của từng món):
    /// kg → bội của 0,1 ; đơn vị đếm → số nguyên; phần dư chia theo "số dư lớn nhất" để cộng lại ĐÚNG bằng <paramref name="q"/>.</summary>
    private static decimal[] ChiaTheoTyLe(decimal q, IReadOnlyList<decimal> w, string? dvt)
    {
        var n = w.Count;
        var kq = new decimal[n];
        if (n == 0 || q <= 0) return kq;
        var tong = w.Sum();
        if (tong <= 0) { kq[0] = q; return kq; }

        var buoc = DonViCan(dvt) ? 0.1m : 1m;
        var soBuoc = Math.Round(q / buoc, 0, MidpointRounding.AwayFromZero);
        var nguyen = new decimal[n];
        var du = new decimal[n];
        for (var i = 0; i < n; i++)
        {
            var ly = soBuoc * w[i] / tong;
            nguyen[i] = Math.Floor(ly);
            du[i] = ly - nguyen[i];
        }
        var con = (int)Math.Max(0, soBuoc - nguyen.Sum());
        foreach (var idx in Enumerable.Range(0, n).OrderByDescending(t => du[t]).ThenByDescending(t => w[t]).Take(con))
            nguyen[idx] += 1;
        for (var i = 0; i < n; i++) kq[i] = nguyen[i] * buoc;

        var lech = q - kq.Sum();   // q không chia hết cho bước (số lẻ đặc biệt) -> dồn vào món có tỷ trọng lớn nhất
        if (lech != 0)
        {
            var lon = 0;
            for (var i = 1; i < n; i++) if (w[i] > w[lon]) lon = i;
            kq[lon] += lech;
        }
        return kq;
    }

    /// <summary>Mục 4 gộp nguyên liệu dùng chung giữa nhiều món vào 1 dòng ("Canh A + Gà sốt B + Cháo C"). Hàm này TÁCH lại thành
    /// từng dòng riêng cho từng món: tổng số lượng của mỗi nguyên liệu GIỮ NGUYÊN (đã qua mọi quy tắc làm tròn / giới hạn / mì tôm…),
    /// chỉ chia ra các món theo tỷ lệ nhu cầu gốc của từng món. Sữa cố định và hành tím cố định không tách. Chạy lại nhiều lần vẫn đúng.
    /// <paramref name="nhuCauNt"/> / <paramref name="nhuCauMg"/> là kết quả <c>TinhNhuCauNguyenLieu(..., tachTheoMon: true)</c>.</summary>
    public void TachTheoMon(
        ChiTietNgayTinhToanResponse kq,
        IReadOnlyList<KetQuaNguyenLieu> nhuCauNt, IReadOnlyList<KetQuaNguyenLieu> nhuCauMg, string? maCoSo = null)
    {
        if (kq.Chinh.Count == 0) return;
        if (LaDiemTruong(maCoSo) && LayUcGa() is { } ucGa)
        {
            nhuCauNt = DoiGaTamHoangSangUcGa(nhuCauNt, ucGa);
            nhuCauMg = DoiGaTamHoangSangUcGa(nhuCauMg, ucGa);
        }
        nhuCauNt = DoiMatHangTheoCoSo(nhuCauNt, maCoSo);
        nhuCauMg = DoiMatHangTheoCoSo(nhuCauMg, maCoSo);
        nhuCauNt = BoMatHangTcKhongAn(nhuCauNt, maCoSo);
        nhuCauMg = BoMatHangTcKhongAn(nhuCauMg, maCoSo);
        nhuCauNt = BoCaRotBanhCanhTc(nhuCauNt, null, maCoSo);   // TC: Bánh canh không có cà rốt
        nhuCauMg = BoCaRotBanhCanhTc(nhuCauMg, null, maCoSo);

        // hàng hoá -> (tên món -> (thứ tự bữa, nhu cầu Nhà trẻ, nhu cầu Mẫu giáo))
        var theoHang = new Dictionary<int, Dictionary<string, (int Ord, decimal Nt, decimal Mg)>>();
        void Gom(IReadOnlyList<KetQuaNguyenLieu> ds, bool laMauGiao)
        {
            foreach (var k in ds)
            {
                if (LaDongCoDinhTheoMon(k.HangHoa.TenHang, k.TenMon)) continue;   // phần cố định theo món đã là dòng riêng -> không chia lại
                var ten = (k.TenMon ?? "").Trim();
                if (!theoHang.TryGetValue(k.HangHoa.Id, out var mon))
                {
                    mon = new Dictionary<string, (int Ord, decimal Nt, decimal Mg)>(StringComparer.OrdinalIgnoreCase);
                    theoHang[k.HangHoa.Id] = mon;
                }
                var co = mon.TryGetValue(ten, out var v);
                var ord = co ? Math.Min(v.Ord, k.ThuTuMon) : k.ThuTuMon;
                mon[ten] = laMauGiao ? (ord, v.Nt, v.Mg + k.TongCanDung) : (ord, v.Nt + k.TongCanDung, v.Mg);
            }
        }
        Gom(nhuCauNt, false);
        Gom(nhuCauMg, true);

        // 1) gộp lại theo hàng hoá (nếu đã tách từ trước) để chạy lại không bị nhân đôi
        var gop = new List<NguyenLieuHaiCotDto>();
        var viTri = new Dictionary<int, int>();
        foreach (var x in kq.Chinh)
        {
            if (LaDongKhongCanDoi(x.TenHang, x.TenMon) || x.HangHoaId <= 0) { gop.Add(x); continue; }
            if (viTri.TryGetValue(x.HangHoaId, out var i))
            {
                var g = gop[i];
                gop[i] = g with
                {
                    SlNhaTre = g.SlNhaTre + x.SlNhaTre, SlMauGiao = g.SlMauGiao + x.SlMauGiao,
                    TtNhaTre = g.TtNhaTre + x.TtNhaTre, TtMauGiao = g.TtMauGiao + x.TtMauGiao,
                };
            }
            else { viTri[x.HangHoaId] = gop.Count; gop.Add(x); }
        }

        // 2) tách từng hàng hoá ra các món
        var thuong = new List<NguyenLieuHaiCotDto>();
        var coDinh = new List<NguyenLieuHaiCotDto>();
        foreach (var x in gop)
        {
            if (LaDongKhongCanDoi(x.TenHang, x.TenMon) || x.HangHoaId <= 0) { coDinh.Add(x); continue; }
            if (!theoHang.TryGetValue(x.HangHoaId, out var mons) || mons.Count == 0) { thuong.Add(x); continue; }
            if (mons.Count == 1)
            {
                var mot = mons.First();
                thuong.Add(x with { TenMon = mot.Key, ThuTuMon = mot.Value.Ord });
                continue;
            }
            var ds = mons.OrderBy(m => m.Value.Ord).ThenBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var slNt = ChiaTheoTyLe(x.SlNhaTre, ds.Select(m => m.Value.Nt).ToList(), x.Dvt);
            var slMg = ChiaTheoTyLe(x.SlMauGiao, ds.Select(m => m.Value.Mg).ToList(), x.Dvt);
            for (var i = 0; i < ds.Count; i++)
            {
                if (slNt[i] == 0 && slMg[i] == 0) continue;
                thuong.Add(x with
                {
                    SlNhaTre = slNt[i], SlMauGiao = slMg[i],
                    TtNhaTre = slNt[i] * x.DonGia, TtMauGiao = slMg[i] * x.DonGia,
                    TenMon = ds[i].Key, ThuTuMon = ds[i].Value.Ord,
                });
            }
        }

        // 3) xếp theo thứ tự món (Canh, Mặn, Xào…), trong món thì mặt hàng tốn tiền nhất lên đầu; hành tím cố định chèn cuối món của nó, sữa cuối cùng
        // Nhiều món cùng 1 bữa có cùng ThuTuMon -> phải gom theo TÊN MÓN (món tốn tiền nhất trước) rồi mới xếp theo tiền,
        // nếu không các dòng của 2 món cùng bữa (VD Nước cam / Lơ xanh) bị xen kẽ nhau.
        var tongTheoMon = thuong
            .GroupBy(x => (x.ThuTuMon, Mon: TenMonHienThi(x.TenMon)))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TtNhaTre + x.TtMauGiao));
        var ketQua = thuong
            .OrderBy(x => x.ThuTuMon)
            .ThenByDescending(x => tongTheoMon[(x.ThuTuMon, TenMonHienThi(x.TenMon))])
            .ThenBy(x => TenMonHienThi(x.TenMon), StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.TtNhaTre + x.TtMauGiao).ThenBy(x => x.TenHang, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var x in coDinh.Where(x => !LaMatHangCoDinh(x.TenHang)))
        {
            var idx = ketQua.FindIndex(r => r.ThuTuMon > x.ThuTuMon);
            ketQua.Insert(idx < 0 ? ketQua.Count : idx, x);
        }
        ketQua.AddRange(coDinh.Where(x => LaMatHangCoDinh(x.TenHang)));

        kq.Chinh = ketQua;
        kq.TongTienChinhNhaTre = ketQua.Sum(x => x.TtNhaTre);
        kq.TongTienChinhMauGiao = ketQua.Sum(x => x.TtMauGiao);
    }

    public ChiTietNgayTinhToanResponse Tinh(
        IReadOnlyList<KetQuaNguyenLieu> ketQuaSang,
        IReadOnlyList<KetQuaNguyenLieu> ketQuaNt,
        IReadOnlyList<KetQuaNguyenLieu> ketQuaMg,
        int soSuatNhaTre = 0, int soSuatMauGiao = 0,
        string? maCoSo = null, int soSuatAnSang = -1, bool sangLaChao = false,
        IReadOnlyList<KetQuaNguyenLieu>? nhuCauTheoMonNt = null, IReadOnlyList<KetQuaNguyenLieu>? nhuCauTheoMonMg = null,
        DateOnly? ngay = null)
    {
        bool LaThucPham(KetQuaNguyenLieu k) => LaThucPhamTrongMuc34(k.HangHoa, maCoSo);

        // Điểm trường (ĐT) chỉ ăn ức gà, không ăn thịt gà tam hoàng: đổi mặt hàng (cùng số lượng) trước khi tính.
        if (LaDiemTruong(maCoSo) && LayUcGa() is { } ucGa)
        {
            ketQuaSang = DoiGaTamHoangSangUcGa(ketQuaSang, ucGa);
            ketQuaNt = DoiGaTamHoangSangUcGa(ketQuaNt, ucGa);
            ketQuaMg = DoiGaTamHoangSangUcGa(ketQuaMg, ucGa);
        }
        ketQuaSang = DoiMatHangTheoCoSo(ketQuaSang, maCoSo);
        ketQuaNt = DoiMatHangTheoCoSo(ketQuaNt, maCoSo);
        ketQuaMg = DoiMatHangTheoCoSo(ketQuaMg, maCoSo);
        ketQuaSang = BoMatHangTcKhongAn(ketQuaSang, maCoSo);
        ketQuaNt = BoMatHangTcKhongAn(ketQuaNt, maCoSo);
        ketQuaMg = BoMatHangTcKhongAn(ketQuaMg, maCoSo);
        ketQuaSang = BoCaRotBanhCanhTc(ketQuaSang, null, maCoSo);   // TC: Bánh canh không có cà rốt
        ketQuaNt = BoCaRotBanhCanhTc(ketQuaNt, nhuCauTheoMonNt, maCoSo);
        ketQuaMg = BoCaRotBanhCanhTc(ketQuaMg, nhuCauTheoMonMg, maCoSo);
        nhuCauTheoMonNt = nhuCauTheoMonNt is null ? null : BoCaRotBanhCanhTc(nhuCauTheoMonNt, null, maCoSo);
        nhuCauTheoMonMg = nhuCauTheoMonMg is null ? null : BoCaRotBanhCanhTc(nhuCauTheoMonMg, null, maCoSo);

        // ----- "Sáng" (không phải gia vị) -----
        var sangDto = ketQuaSang.Where(k => LaThucPhamTrongMuc34(k.HangHoa, maCoSo))
            .OrderBy(k => k.ThuTuMon).ThenByDescending(k => k.ThanhTien).ThenBy(k => k.HangHoa.TenHang)
            .Select(k =>
            {
                var gia = k.HangHoa.DonGiaGanNhat ?? 0;
                var sl = ApGioiHanToiDa(k.HangHoa.TenHang, LamTronTheoDvt(k.TongCanDung, k.HangHoa.Dvt?.TenDvt));
                if (LaChaCan(k.HangHoa.TenHang, k.HangHoa.Dvt?.TenDvt)) sl = ApChaToiDa(sl);   // chả bữa sáng: ≤ 3 kg, làm tròn
                if (soSuatAnSang > 0 && LaSoiTuoi(k.HangHoa.TenHang, k.HangHoa.Dvt?.TenDvt)) sl = SoiTuoiTheoSuat(soSuatAnSang, SoiTuoiAnSang);   // mì/bún/phở tươi sáng: suất × 0,11, làm tròn xuống
                return new NguyenLieuMotCotDto(k.HangHoa.TenHang, k.HangHoa.Dvt?.TenDvt, sl, gia, sl * gia, k.HangHoa.Id, k.ThuTuMon, k.TenMon);
            })
            .Where(x => x.SoLuong > 0)
            .ToList();

        // Ngày có CHÁO sáng: thêm nguyên liệu nấu cháo (nếp hương, đậu xanh, cà rốt, dầu mè, dầu gấc) vào mục 3.
        if (sangLaChao && soSuatAnSang != 0 && string.Equals((maCoSo ?? "").Trim(), "TC", StringComparison.OrdinalIgnoreCase))
            sangDto.RemoveAll(x => x.HangHoaId == 138);   // chợ TC: cháo sáng không có cà rốt
        if (sangLaChao && soSuatAnSang != 0)
            foreach (var (id, sl) in NguyenLieuChaoSang(maCoSo).Where(t => ketQuaSang.Any(k => k.HangHoa.Id == t.HangHoaId)))   // chỉ nguyên liệu mà món cháo CÓ dùng
            {
                var hh = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == id);
                if (hh is null) continue;
                var gia = hh.DonGiaGanNhat ?? 0;
                sangDto.RemoveAll(x => x.HangHoaId == id);
                sangDto.Add(new NguyenLieuMotCotDto(hh.TenHang, hh.Dvt?.TenDvt, sl, gia, sl * gia, hh.Id, int.MaxValue, "Cháo sáng"));
            }

        // ----- "Chính + xế" (không phải gia vị) — gộp Nhà trẻ + Mẫu giáo theo đúng hàng hoá -----
        var chinhTheoHangHoa = new Dictionary<int, (HangHoa hh, decimal nt, decimal mg, decimal tnt, decimal tmg, int ord, string? mon)>();
        foreach (var k in ketQuaNt.Where(LaThucPham))
        {
            chinhTheoHangHoa.TryGetValue(k.HangHoa.Id, out var hienTai);
            if (hienTai.hh is null) { hienTai.ord = int.MaxValue; }
            hienTai.hh = k.HangHoa;
            hienTai.nt = SoGoiTheoSoTre(k.HangHoa, false, soSuatNhaTre) ?? hienTai.nt + k.TongCanDung;
            hienTai.tnt += k.ThanhTien;
            if (k.ThuTuMon < hienTai.ord) { hienTai.ord = k.ThuTuMon; hienTai.mon = k.TenMon; }
            chinhTheoHangHoa[k.HangHoa.Id] = hienTai;
        }
        foreach (var k in ketQuaMg.Where(LaThucPham))
        {
            chinhTheoHangHoa.TryGetValue(k.HangHoa.Id, out var hienTai);
            if (hienTai.hh is null) { hienTai.ord = int.MaxValue; }
            hienTai.hh = k.HangHoa;
            hienTai.mg = SoGoiTheoSoTre(k.HangHoa, true, soSuatMauGiao) ?? hienTai.mg + k.TongCanDung;
            hienTai.tmg += k.ThanhTien;
            if (k.ThuTuMon < hienTai.ord || (k.ThuTuMon == hienTai.ord && hienTai.mon is null)) { hienTai.ord = k.ThuTuMon; hienTai.mon = k.TenMon; }
            chinhTheoHangHoa[k.HangHoa.Id] = hienTai;
        }

        // Mặt hàng đúng mỗi ngày theo số trẻ (VD Sữa Ellac Multi): GHI ĐÈ (không cộng dồn) lên số tính từ công
        // thức món để không bị tính 2 lần nếu công thức món còn sót mặt hàng này.
        foreach (var (tenHang, dinhLuong) in MatHangMoiTre)
        {
            var hh = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == tenHang);
            if (hh is null) continue;
            chinhTheoHangHoa[hh.Id] = (hh, dinhLuong * soSuatNhaTre, dinhLuong * soSuatMauGiao, 0, 0, int.MaxValue, "Sữa uống mỗi ngày");
        }

        // ----- MÓN SÚP: bột năng / bột bán MẶC ĐỊNH theo cơ sở (thay phần các món Súp tính từ công thức) -----
        // nhuCauTheoMon* = nhu cầu TÁCH THEO TỪNG MÓN (mỗi dòng đúng 1 món) -> nhận ra phần của món Súp. Không có món Súp thì không đổi gì.
        var supNt = (nhuCauTheoMonNt ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => LaMonSup(k.TenMon)).ToList();
        var supMg = (nhuCauTheoMonMg ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => LaMonSup(k.TenMon)).ToList();
        var coSupNt = soSuatNhaTre > 0 && supNt.Count > 0;
        var coSupMg = soSuatMauGiao > 0 && supMg.Count > 0;
        if ((coSupNt || coSupMg) && SupMacDinh(maCoSo) is { } supCoSo)
        {
            var ordSup = supNt.Concat(supMg).Min(k => k.ThuTuMon);
            var tenSup = supNt.Concat(supMg).OrderBy(k => k.ThuTuMon).Select(k => k.TenMon).FirstOrDefault();
            var suatNt = coSupNt ? soSuatNhaTre : 0;
            var suatMg = coSupMg ? soSuatMauGiao : 0;

            void DatMacDinh(string tenHang, decimal ntCd, decimal mgCd)
            {
                var hh = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == tenHang);
                if (hh is null) return;
                chinhTheoHangHoa.TryGetValue(hh.Id, out var e);
                var coDong = e.hh is not null;
                // bỏ phần của các món Súp (tính từ công thức), giữ phần của các món khác
                var nt0 = Math.Max(0m, (coDong ? e.nt : 0m) - supNt.Where(k => k.HangHoa.Id == hh.Id).Sum(k => k.TongCanDung));
                var mg0 = Math.Max(0m, (coDong ? e.mg : 0m) - supMg.Where(k => k.HangHoa.Id == hh.Id).Sum(k => k.TongCanDung));
                // chia cố định theo bảng; chỉ 1 khối có Súp -> khối đó nhận cả tổng
                var themNt = suatNt > 0 && suatMg > 0 ? ntCd : suatNt > 0 ? ntCd + mgCd : 0m;
                var themMg = suatNt > 0 && suatMg > 0 ? mgCd : suatMg > 0 ? ntCd + mgCd : 0m;
                chinhTheoHangHoa[hh.Id] = (hh, nt0 + themNt, mg0 + themMg,
                    coDong ? e.tnt : 0m, coDong ? e.tmg : 0m, coDong ? e.ord : ordSup, coDong ? e.mon : tenSup);
            }
            DatMacDinh("Bột năng không bao bì", supCoSo.NtBotNang, supCoSo.MgBotNang);
            DatMacDinh("Bột bán (10 kg/bao)", supCoSo.NtBotBan, supCoSo.MgBotBan);
        }

        // ----- MÓN CHÁO Ở XẾ / CHÍNH CHIỀU: nếp, đậu xanh, cà rốt CỐ ĐỊNH theo cơ sở (thay phần các món Cháo tính từ công thức) -----
        var chaoNt = (nhuCauTheoMonNt ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => LaMonChao(k.TenMon)).ToList();
        var chaoMg = (nhuCauTheoMonMg ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => LaMonChao(k.TenMon)).ToList();
        var coChaoNt = soSuatNhaTre > 0 && chaoNt.Count > 0;
        var coChaoMg = soSuatMauGiao > 0 && chaoMg.Count > 0;
        if ((coChaoNt || coChaoMg) && ChaoXeMacDinh(maCoSo) is { } chaoCd)
        {
            var ordChao = chaoNt.Concat(chaoMg).Min(k => k.ThuTuMon);
            var tenChao = chaoNt.Concat(chaoMg).OrderBy(k => k.ThuTuMon).Select(k => k.TenMon).FirstOrDefault();
            foreach (var idChao in HangChaoCoDinh)
            {
                if (!chaoNt.Concat(chaoMg).Any(k => k.HangHoa.Id == idChao)) continue;   // món cháo không dùng nguyên liệu này -> không thêm
                var hhChao = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == idChao);
                if (hhChao is null) continue;
                chinhTheoHangHoa.TryGetValue(idChao, out var ec);
                var coDongChao = ec.hh is not null;
                // bỏ phần của các món Cháo (tính từ công thức), giữ phần của các món khác
                var ntChao0 = Math.Max(0m, (coDongChao ? ec.nt : 0m) - chaoNt.Where(k => k.HangHoa.Id == idChao).Sum(k => k.TongCanDung));
                var mgChao0 = Math.Max(0m, (coDongChao ? ec.mg : 0m) - chaoMg.Where(k => k.HangHoa.Id == idChao).Sum(k => k.TongCanDung));
                // Nếp hương (387) ở chợ TC: cố định 0,5 Nhà trẻ + 1,5 Mẫu giáo (đậu xanh, cà rốt giữ bảng chung).
                var laTc = string.Equals((maCoSo ?? "").Trim(), "TC", StringComparison.OrdinalIgnoreCase);
                var cdChao = idChao == 387 && laTc ? (Nt: 0.5m, Mg: 1.5m) : idChao == 138 && laTc ? (Nt: 0m, Mg: 0m) : chaoCd;   // TC: cháo không dùng cà rốt
                // Tách riêng từng khối: khối nào ăn cháo chỉ nhận phần của khối đó (KHÔNG dồn cả tổng của chợ cho một khối).
                var themNtChao = coChaoNt ? cdChao.Nt : 0m;
                var themMgChao = coChaoMg ? cdChao.Mg : 0m;
                if (ntChao0 + themNtChao <= 0m && mgChao0 + themMgChao <= 0m) { chinhTheoHangHoa.Remove(idChao); continue; }
                chinhTheoHangHoa[idChao] = (hhChao, ntChao0 + themNtChao, mgChao0 + themMgChao,
                    coDongChao ? ec.tnt : 0m, coDongChao ? ec.tmg : 0m, coDongChao ? ec.ord : ordChao, coDongChao ? ec.mon : tenChao);
            }

            // Dầu mè (694) và dầu gấc (443): MỖI loại TC 2 chai; PH 2 chai; ĐT 1 chai cả ngày. Đếm theo chai nên KHÔNG chia lẻ: dồn cho Mẫu giáo
            // (không có Mẫu giáo ăn cháo thì cho Nhà trẻ).
            var chaiDauCao = LaDiemTruong(maCoSo) ? 1m : 2m;
            foreach (var idDau in new[] { 694, 443 })
            {
                if (!chaoNt.Concat(chaoMg).Any(k => k.HangHoa.Id == idDau)) continue;   // món cháo không dùng dầu này -> không thêm
                var hhDau = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == idDau);
                if (hhDau is null) continue;
                chinhTheoHangHoa.TryGetValue(idDau, out var ed);
                var coDongDau = ed.hh is not null;
                var ntDau0 = Math.Max(0m, (coDongDau ? ed.nt : 0m) - chaoNt.Where(k => k.HangHoa.Id == idDau).Sum(k => k.TongCanDung));
                var mgDau0 = Math.Max(0m, (coDongDau ? ed.mg : 0m) - chaoMg.Where(k => k.HangHoa.Id == idDau).Sum(k => k.TongCanDung));
                var themNtDau = coChaoMg ? 0m : chaiDauCao;
                var themMgDau = coChaoMg ? chaiDauCao : 0m;
                chinhTheoHangHoa[idDau] = (hhDau, ntDau0 + themNtDau, mgDau0 + themMgDau,
                    coDongDau ? ed.tnt : 0m, coDongDau ? ed.tmg : 0m, coDongDau ? ed.ord : ordChao, coDongDau ? ed.mon : tenChao);
            }
        }

        // ----- MÓN PHỞ: HÀNH TÂY (189) — TỔNG NGÀY (sáng + xế) TC 1 kg, PH 1,5 kg; ĐT không dùng. Phần xế chia NT 0,3 / MG 0,7. -----
        // Phở BỮA SÁNG thì KHÔNG đụng tới: phần sáng nằm ở MỤC 3, được đặt theo phần chia của tổng ngày
        // (bảng kê mẫu ngày chỉ có Phở sáng: hành tây chỉ nằm ở mục 3).
        {
            const int HanhTayId = 189;
            var phoNt = (nhuCauTheoMonNt ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => k.HangHoa.Id == HanhTayId && LaMonPhoGop(k.TenMon)).ToList();
            var phoMg = (nhuCauTheoMonMg ?? Array.Empty<KetQuaNguyenLieu>()).Where(k => k.HangHoa.Id == HanhTayId && LaMonPhoGop(k.TenMon)).ToList();
            if (phoNt.Count > 0 || phoMg.Count > 0 || sangDto.Any(x => x.HangHoaId == HanhTayId && LaMonPhoGop(x.TenMon)))
            {
                var hhHt = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == HanhTayId);
                if (hhHt is not null)
                {
                    chinhTheoHangHoa.TryGetValue(HanhTayId, out var eh);
                    var coDongHt = eh.hh is not null;
                    var nt0 = Math.Max(0m, (coDongHt ? eh.nt : 0m) - phoNt.Sum(k => k.TongCanDung));
                    var mg0 = Math.Max(0m, (coDongHt ? eh.mg : 0m) - phoMg.Sum(k => k.TongCanDung));
                    var dt = LaDiemTruong(maCoSo);
                    // TỔNG hành tây của món Phở trong NGÀY (cộng cả bữa sáng và xế/chính chiều): TC 1 kg, PH 1,5 kg; ĐT không dùng.
                    // Ngày chỉ Phở ở một bữa thì bữa đó nhận cả tổng; có Phở ở cả hai bữa thì chia đều hai bữa.
                    var tongHanhTay = dt ? 0m : string.Equals((maCoSo ?? "").Trim(), "PH", StringComparison.OrdinalIgnoreCase) ? 1.5m : 1m;
                    var coPhoSang = sangDto.Any(x => x.HangHoaId == HanhTayId && LaMonPhoGop(x.TenMon));
                    var coPhoXe = phoNt.Count > 0 || phoMg.Count > 0;
                    var phanBua = (coPhoSang ? 1 : 0) + (coPhoXe ? 1 : 0);
                    var moiBua = phanBua > 0 ? tongHanhTay / phanBua : 0m;
                    if (coPhoSang && moiBua > 0m)
                    {
                        for (var i = 0; i < sangDto.Count; i++)
                            if (sangDto[i].HangHoaId == HanhTayId && LaMonPhoGop(sangDto[i].TenMon))
                                sangDto[i] = sangDto[i] with { SoLuong = moiBua, ThanhTien = moiBua * sangDto[i].DonGia };
                    }
                    // Phần xế/chính chiều chia Nhà trẻ 30% / Mẫu giáo 70% (khối nào không ăn thì khối còn lại nhận cả phần).
                    var phanXe = coPhoXe ? moiBua : 0m;
                    var ntCoPho = soSuatNhaTre > 0 && phoNt.Count > 0;
                    var mgCoPho = soSuatMauGiao > 0 && phoMg.Count > 0;
                    var themNt = ntCoPho ? (mgCoPho ? Math.Round(phanXe * 0.3m, 1, MidpointRounding.AwayFromZero) : phanXe) : 0m;
                    var themMg = mgCoPho ? phanXe - themNt : 0m;
                    var tenPho = phoNt.Concat(phoMg).Select(k => k.TenMon).FirstOrDefault();
                    chinhTheoHangHoa[HanhTayId] = (hhHt, nt0 + themNt, mg0 + themMg,
                        coDongHt ? eh.tnt : 0m, coDongHt ? eh.tmg : 0m, coDongHt ? eh.ord : int.MaxValue, coDongHt ? eh.mon : tenPho);
                }
            }
        }

        var chinhDto = chinhTheoHangHoa.Values
            .OrderBy(x => LaMatHangCoDinh(x.hh.TenHang) ? 1 : 0)   // mọi nguyên liệu xếp THEO MÓN (thứ tự món trong thực đơn); chỉ sữa cố định nằm cuối
            .ThenBy(x => x.ord).ThenByDescending(x => x.tnt + x.tmg).ThenBy(x => x.hh.TenHang)
            .Select(x =>
            {
                var gia = x.hh.DonGiaGanNhat ?? 0;
                var dvt = x.hh.Dvt?.TenDvt;
                var coDinh = LaMatHangCoDinh(x.hh.TenHang);   // sữa: giữ nguyên số lẻ như file mẫu
                var nt = coDinh ? x.nt : ApGioiHanToiDa(x.hh.TenHang, LamTronTheoDvt(x.nt, dvt));
                var mg = coDinh ? x.mg : ApGioiHanToiDa(x.hh.TenHang, LamTronTheoDvt(x.mg, dvt));
                if (LaSoiTuoi(x.hh.TenHang, dvt))   // mì/bún/phở tươi ăn chính: suất × 0,125, làm tròn xuống (khối nào dùng thì tính)
                {
                    if (nt > 0 && soSuatNhaTre > 0) nt = SoiTuoiTheoSuat(soSuatNhaTre, SoiTuoiAnChinh);
                    if (mg > 0 && soSuatMauGiao > 0) mg = SoiTuoiTheoSuat(soSuatMauGiao, SoiTuoiAnChinh);
                }
                if (LaRauCanhRau(x.hh.TenHang, x.mon, dvt)) { nt = Math.Min(nt, CanhRauToiDaKg); mg = Math.Min(mg, CanhRauToiDaKg); }   // canh rau: ≤ 20 kg
                if (BatDauBang(x.hh.TenHang, "Xì dầu") || BatDauBang(x.hh.TenHang, "Nước mắm")) mg = Math.Min(mg, 1m);   // xì dầu / nước mắm: Mẫu giáo tối đa 1 chai (mục 4, riêng với mục 2)
                return new NguyenLieuHaiCotDto(x.hh.TenHang, dvt, nt, mg, gia, nt * gia, mg * gia, x.hh.Id, x.ord, x.mon);
            })
            .Where(x => x.SlNhaTre > 0 || x.SlMauGiao > 0)
            .ToList();

        // ----- Bánh mì: chỉ MỘT khối ăn — Nhà trẻ ăn bánh mì gối (khúc), Mẫu giáo ăn bánh mì ổ nhỏ (ổ). Khối còn lại = 0 -----
        {
            var chiMotKhoi = new (string TienTo, bool NhaTre, bool MauGiao)[]
            {
                ("Bánh mì gối Quốc Doanh", true, false),
                ("Bánh mì ổ nhỏ Quốc Doanh", false, true),
            };
            for (var i = chinhDto.Count - 1; i >= 0; i--)
            {
                var x = chinhDto[i];
                var r = chiMotKhoi.FirstOrDefault(k => BatDauBang(x.TenHang, k.TienTo));
                if (r.TienTo is null) continue;
                var bn = r.NhaTre ? x.SlNhaTre : 0m;
                var bm = r.MauGiao ? x.SlMauGiao : 0m;
                if (bn == 0 && bm == 0) chinhDto.RemoveAt(i);
                else chinhDto[i] = x with { SlNhaTre = bn, SlMauGiao = bm, TtNhaTre = bn * x.DonGia, TtMauGiao = bm * x.DonGia };
            }
        }

        // ----- Bột chiên Đại Hàn CỐ ĐỊNH: TC/PH mỗi khối 1 gói (tổng 2 gói); ĐT không dùng -----
        {
            var laDtBot = LaDiemTruong(maCoSo);
            for (var i = chinhDto.Count - 1; i >= 0; i--)
            {
                var x = chinhDto[i];
                if (!LaBotChienDaiHan(x.TenHang)) continue;
                var bn = !laDtBot && soSuatNhaTre > 0 ? 1m : 0m;
                var bm = !laDtBot && soSuatMauGiao > 0 ? 1m : 0m;
                if (bn == 0 && bm == 0) chinhDto.RemoveAt(i);
                else chinhDto[i] = x with { SlNhaTre = bn, SlMauGiao = bm, TtNhaTre = bn * x.DonGia, TtMauGiao = bm * x.DonGia };
            }
        }

        // ----- Dầu hào: TỐI ĐA 1 chai cả ngày (Nhà trẻ 0,2 + Mẫu giáo 0,8; 1 khối thì khối đó 1 chai); ĐT không dùng -----
        {
            var laDtDauHao = LaDiemTruong(maCoSo);
            for (var i = chinhDto.Count - 1; i >= 0; i--)
            {
                var x = chinhDto[i];
                if (!LaDauHao(x.TenHang)) continue;
                var coN = x.SlNhaTre > 0;
                var coM = x.SlMauGiao > 0;
                if (laDtDauHao || (!coN && !coM)) { chinhDto.RemoveAt(i); continue; }
                var dn = coN && coM ? 0.2m : coN ? 1m : 0m;
                var dm = coN && coM ? 0.8m : coM ? 1m : 0m;
                chinhDto[i] = x with { SlNhaTre = dn, SlMauGiao = dm, TtNhaTre = dn * x.DonGia, TtMauGiao = dm * x.DonGia };
            }
        }

        // ----- Hành tím CỐ ĐỊNH: bỏ hành tím tính theo công thức các món, thay bằng 1 dòng số lượng cố định mỗi ngày -----
        // Sáng (1 cột) -> mục 3; Nhà trẻ / Mẫu giáo -> mục 4. Nhóm nào không có trẻ (số suất = 0) hoặc không có món thì không có dòng.
        var hanhTimHang = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == "Hành tím");
        if (hanhTimHang is not null)
        {
            var (hanhSang, hanhNhaTre, hanhMauGiao) = HanhTimCoDinhMoiNgay(maCoSo);
            var giaHanhTim = hanhTimHang.DonGiaGanNhat ?? 0;
            var dvtHanhTim = hanhTimHang.Dvt?.TenDvt;

            var viTriSang = sangDto.FindIndex(x => LaHanhTimBatKy(x.TenHang));
            var dongCuSang = viTriSang >= 0 ? sangDto[viTriSang] : null;
            sangDto.RemoveAll(x => LaHanhTimBatKy(x.TenHang));
            var coAnSang = ketQuaSang.Count > 0 && (soSuatAnSang < 0 || soSuatAnSang > 0);
            if (coAnSang && hanhSang > 0)
                sangDto.Insert(viTriSang >= 0 ? Math.Min(viTriSang, sangDto.Count) : sangDto.Count,
                    new NguyenLieuMotCotDto(hanhTimHang.TenHang, dvtHanhTim, hanhSang, giaHanhTim, hanhSang * giaHanhTim, hanhTimHang.Id,
                        dongCuSang?.ThuTuMon ?? int.MaxValue, dongCuSang?.TenMon));

            var viTriChinh = chinhDto.FindIndex(x => LaHanhTimBatKy(x.TenHang));
            var dongCuChinh = viTriChinh >= 0 ? chinhDto[viTriChinh] : null;
            chinhDto.RemoveAll(x => LaHanhTimBatKy(x.TenHang));
            var hanhNt = ketQuaNt.Count > 0 && soSuatNhaTre > 0 ? hanhNhaTre : 0m;
            var hanhMg = ketQuaMg.Count > 0 && soSuatMauGiao > 0 ? hanhMauGiao : 0m;
            if (hanhNt > 0 || hanhMg > 0)
            {
                var viTriChen = viTriChinh >= 0 ? Math.Min(viTriChinh, chinhDto.Count) : chinhDto.FindIndex(x => LaMatHangCoDinh(x.TenHang));
                if (viTriChen < 0) viTriChen = chinhDto.Count;
                chinhDto.Insert(viTriChen,
                    new NguyenLieuHaiCotDto(hanhTimHang.TenHang, dvtHanhTim, hanhNt, hanhMg, giaHanhTim, hanhNt * giaHanhTim, hanhMg * giaHanhTim,
                        hanhTimHang.Id, dongCuChinh?.ThuTuMon ?? int.MaxValue, dongCuChinh?.TenMon));
            }
        }

        // ----- MẶT HÀNG CỐ ĐỊNH KHI CÓ TRONG THỰC ĐƠN (theo từng chợ) -----
        // Khối (Sáng / Nhà trẻ / Mẫu giáo) nào có món dùng mặt hàng đó thì cột của khối ấy = số cố định (không theo công thức, không nhân số suất):
        //   Hành ngò, Hành lá: chỉ chợ PH — sáng 0,2 / Nhà trẻ 0,1 / Mẫu giáo 0,2 (TC không ăn; ĐT giữ cách tính cũ).
        //   Me chua:    TC, PH — Nhà trẻ 0,2 / Mẫu giáo 0,3;  ĐT — 0,1 / 0,2.
        //   Đường vàng: TC, PH — Nhà trẻ 0,2 / Mẫu giáo 0,3;  ĐT — không dùng (bỏ dòng).
        //   Đường thốt nốt: như đường vàng.  Nước mắm đệ nhị: TC, PH — Mẫu giáo 1 chai; ĐT — không dùng.
        if (!string.IsNullOrWhiteSpace(maCoSo))
        {
            var maCs = maCoSo.Trim().ToUpperInvariant();

            void CoDinhKhiCo(string tienTo, int hangId, (decimal Sang, decimal NhaTre, decimal MauGiao)? so)
            {
                var coS = ketQuaSang.Any(k => BatDauBang(k.HangHoa.TenHang, tienTo));
                var coN = ketQuaNt.Any(k => BatDauBang(k.HangHoa.TenHang, tienTo));
                var coM = ketQuaMg.Any(k => BatDauBang(k.HangHoa.TenHang, tienTo));
                // ĐƯỜNG VÀNG (TC, PH; ĐT "không dùng"): món MẶN dùng ĐƯỜNG TRẮNG thì THAY đường trắng bằng đường vàng, GIỮ NGUYÊN định lượng của món
                // nhưng mỗi khối KHÔNG QUÁ 0,5 kg. Tuyệt đối KHÔNG cộng thêm một dòng đường vàng nào ngoài số đã có của món (dòng vẫn mang tên món mặn).
                const int ThuTuBuaMan = 3;   // loai_bua_an.thu_tu của bữa MẶN
                var laDuongVang = BatDauBang(tienTo, "Đường vàng");
                var trangMan = laDuongVang && so is not null
                    ? chinhDto.Where(x => x.ThuTuMon == ThuTuBuaMan && BatDauBang(x.TenHang, "Đường trắng") && (x.SlNhaTre > 0 || x.SlMauGiao > 0)).ToList()
                    : new List<NguyenLieuHaiCotDto>();
                var manDuongN = trangMan.Any(x => x.SlNhaTre > 0);
                var manDuongM = trangMan.Any(x => x.SlMauGiao > 0);
                if (!coS && !coN && !coM && !manDuongN && !manDuongM) return;
                var hh = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.Id == hangId);
                if (hh is null) return;

                var viTriS = sangDto.FindIndex(x => BatDauBang(x.TenHang, tienTo));
                var cuS = viTriS >= 0 ? sangDto[viTriS] : null;
                sangDto.RemoveAll(x => BatDauBang(x.TenHang, tienTo));
                var viTriC = chinhDto.FindIndex(x => BatDauBang(x.TenHang, tienTo));
                var cuC = viTriC >= 0 ? chinhDto[viTriC] : null;
                chinhDto.RemoveAll(x => BatDauBang(x.TenHang, tienTo));
                var viTriTrang = trangMan.Count > 0 ? chinhDto.FindIndex(x => trangMan.Contains(x)) : -1;   // chỗ của dòng đường trắng sắp được thay
                if (trangMan.Count > 0) chinhDto.RemoveAll(x => trangMan.Contains(x));
                if (so is null) return;

                var gia = hh.DonGiaGanNhat ?? 0;
                var sang = coS && soSuatAnSang != 0 ? so.Value.Sang : 0m;
                if (sang > 0)
                    sangDto.Insert(viTriS >= 0 ? Math.Min(viTriS, sangDto.Count) : sangDto.Count,
                        new NguyenLieuMotCotDto(hh.TenHang, hh.Dvt?.TenDvt, sang, gia, sang * gia, hh.Id, cuS?.ThuTuMon ?? int.MaxValue, cuS?.TenMon));
                var nt = coN && soSuatNhaTre > 0 ? so.Value.NhaTre : 0m;
                var mg = coM && soSuatMauGiao > 0 ? so.Value.MauGiao : 0m;
                // Đường trắng của món mặn -> đường vàng, giữ nguyên số lượng, tối đa 0,5 kg mỗi khối. Khối nào công thức món đã có đường vàng
                // thì vẫn theo số cố định của quy tắc (0,2 / 0,3) như cũ.
                if (laDuongVang)
                {
                    if (!coN && manDuongN && soSuatNhaTre > 0) nt = Math.Min(0.5m, trangMan.Sum(x => x.SlNhaTre));
                    if (!coM && manDuongM && soSuatMauGiao > 0) mg = Math.Min(0.5m, trangMan.Sum(x => x.SlMauGiao));
                }
                else if (BatDauBang(tienTo, "Đường thốt nốt"))
                {
                    bool ManCoDuong(IReadOnlyList<KetQuaNguyenLieu>? ds) => ds is not null && ds.Any(k => k.ThuTuMon == ThuTuBuaMan && BatDauBang(k.HangHoa.TenHang, tienTo));
                    if (nt > 0 && ManCoDuong(nhuCauTheoMonNt)) nt += 0.5m;
                    if (mg > 0 && ManCoDuong(nhuCauTheoMonMg)) mg += 0.5m;
                }
                if (nt > 0 || mg > 0)
                {
                    var viTriChen = viTriC >= 0 ? Math.Min(viTriC, chinhDto.Count)
                        : viTriTrang >= 0 ? Math.Min(viTriTrang, chinhDto.Count)
                        : chinhDto.FindIndex(x => LaMatHangCoDinh(x.TenHang));
                    if (viTriChen < 0) viTriChen = chinhDto.Count;
                    var dongMonMan = trangMan.FirstOrDefault();   // đường vàng thay đường trắng: ghi chú = đúng tên món mặn
                    chinhDto.Insert(viTriChen,
                        new NguyenLieuHaiCotDto(hh.TenHang, hh.Dvt?.TenDvt, nt, mg, gia, nt * gia, mg * gia, hh.Id,
                            cuC?.ThuTuMon ?? dongMonMan?.ThuTuMon ?? int.MaxValue, cuC?.TenMon ?? dongMonMan?.TenMon));
                }
            }

            // Các mặt hàng cố định khi có trong thực đơn: LẤY TỪ BẢNG QUY TẮC (trang /quy-tac-nguyen-lieu) — hành ngò, hành lá, me chua,
            // đường vàng, đường thốt nốt, tỏi khô, nước mắm đệ nhị, dầu mè trắng, táo tàu... Thêm mặt hàng mới chỉ cần thêm quy tắc ở form.
            foreach (var qt in ChonQuyTac(LayQuyTac(QuyTacNguyenLieu.KhiCoTrongThucDon), maCoSo, ngay).Values.OrderBy(q => q.HangHoaId))
                CoDinhKhiCo((qt.HangHoa.TenHang ?? "").Trim(), qt.HangHoaId,
                    qt.KhongDung ? null : (qt.SlAnSang, qt.SlNhaTre, qt.SlMauGiao));

            // ----- QUY TẮC THEO MÓN (VD "Thịt xúi mại sốt cà": bánh mì ổ lớn, cà chua, cà chua lon cố định theo chợ) -----
            // Món có trong thực đơn của khối nào (nhuCauTheoMon* = nhu cầu tách theo từng món) thì phần mặt hàng do CHÍNH MÓN ĐÓ sinh ra
            // được thay bằng số cố định của quy tắc (cột Nhà trẻ / Mẫu giáo); các món KHÁC dùng cùng mặt hàng (VD canh cà chua) vẫn tính theo công thức
            // và nằm ở dòng riêng. Dòng cố định mang tên món nên không bị cân đối nhích (LaDongCoDinhTheoMon).
            foreach (var tmQt in ChonQuyTacTheoMon(LayQuyTacTheoMon(), maCoSo, ngay))
            {
                var tmTenMon = (tmQt.MonAn?.TenMon ?? "").Trim();
                if (tmTenMon.Length == 0) continue;
                bool TmLaMon(KetQuaNguyenLieu tk) => string.Equals((tk.TenMon ?? "").Trim(), tmTenMon, StringComparison.OrdinalIgnoreCase);
                var tmMonNt = (nhuCauTheoMonNt ?? Array.Empty<KetQuaNguyenLieu>()).Where(TmLaMon).ToList();
                var tmMonMg = (nhuCauTheoMonMg ?? Array.Empty<KetQuaNguyenLieu>()).Where(TmLaMon).ToList();
                var tmCoNt = soSuatNhaTre > 0 && tmMonNt.Count > 0;
                var tmCoMg = soSuatMauGiao > 0 && tmMonMg.Count > 0;
                // Bữa SÁNG (mục 3, 1 cột): món có trong thực đơn sáng -> cột ăn sáng của quy tắc (nhu cầu sáng không tách món, bữa sáng thường chỉ 1 món)
                var tmCoSang = soSuatAnSang != 0 && ketQuaSang.Any(tk =>
                    (tk.TenMon ?? "").Split(" + ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(tp => string.Equals(tp, tmTenMon, StringComparison.OrdinalIgnoreCase)));
                if (!tmCoNt && !tmCoMg && !tmCoSang) continue;   // món không có trong thực đơn ngày này

                var tmHang = tmQt.HangHoa;
                var tmDvt = tmHang.Dvt?.TenDvt;
                var tmGia = tmHang.DonGiaGanNhat ?? 0m;

                if (tmCoSang)
                {
                    var tmViTriS = sangDto.FindIndex(tx => tx.HangHoaId == tmHang.Id);
                    var tmCuS = tmViTriS >= 0 ? sangDto[tmViTriS] : null;
                    sangDto.RemoveAll(tx => tx.HangHoaId == tmHang.Id);
                    var tmSang = tmQt.KhongDung ? 0m : tmQt.SlAnSang;
                    if (tmSang > 0)
                        sangDto.Insert(tmViTriS >= 0 ? Math.Min(tmViTriS, sangDto.Count) : sangDto.Count,
                            new NguyenLieuMotCotDto(tmHang.TenHang, tmDvt, tmSang, tmGia, tmSang * tmGia, tmHang.Id, tmCuS?.ThuTuMon ?? int.MaxValue, tmTenMon));
                }
                if (!tmCoNt && !tmCoMg) continue;   // món chỉ có ở bữa sáng

                var tmOrdMon = tmMonNt.Concat(tmMonMg).Min(tk => tk.ThuTuMon);

                // Phần mặt hàng này của CÁC MÓN KHÁC -> giữ lại ở dòng riêng.
                var tmKhacNt = (nhuCauTheoMonNt ?? Array.Empty<KetQuaNguyenLieu>()).Where(tk => tk.HangHoa.Id == tmHang.Id && !TmLaMon(tk)).ToList();
                var tmKhacMg = (nhuCauTheoMonMg ?? Array.Empty<KetQuaNguyenLieu>()).Where(tk => tk.HangHoa.Id == tmHang.Id && !TmLaMon(tk)).ToList();
                var tmConNt = soSuatNhaTre > 0 && tmKhacNt.Count > 0 ? ApGioiHanToiDa(tmHang.TenHang, LamTronTheoDvt(tmKhacNt.Sum(tk => tk.TongCanDung), tmDvt)) : 0m;
                var tmConMg = soSuatMauGiao > 0 && tmKhacMg.Count > 0 ? ApGioiHanToiDa(tmHang.TenHang, LamTronTheoDvt(tmKhacMg.Sum(tk => tk.TongCanDung), tmDvt)) : 0m;
                var tmKhacDau = tmKhacNt.Concat(tmKhacMg).OrderBy(tk => tk.ThuTuMon).FirstOrDefault();

                var tmViTriCu = chinhDto.FindIndex(tx => tx.HangHoaId == tmHang.Id && !LaDongCoDinhTheoMon(tx.TenHang, tx.TenMon));
                chinhDto.RemoveAll(tx => tx.HangHoaId == tmHang.Id && !LaDongCoDinhTheoMon(tx.TenHang, tx.TenMon));
                chinhDto.RemoveAll(tx => tx.HangHoaId == tmHang.Id && LaDongCoDinhTheoMon(tx.TenHang, tx.TenMon) && string.Equals((tx.TenMon ?? "").Trim(), tmTenMon, StringComparison.OrdinalIgnoreCase));   // tính lại: không nhân đôi

                if (tmConNt > 0 || tmConMg > 0)
                    chinhDto.Insert(tmViTriCu >= 0 ? Math.Min(tmViTriCu, chinhDto.Count) : chinhDto.Count,
                        new NguyenLieuHaiCotDto(tmHang.TenHang, tmDvt, tmConNt, tmConMg, tmGia, tmConNt * tmGia, tmConMg * tmGia, tmHang.Id,
                            tmKhacDau?.ThuTuMon ?? int.MaxValue, tmKhacDau?.TenMon));

                var tmNt = tmCoNt && !tmQt.KhongDung ? tmQt.SlNhaTre : 0m;
                var tmMg = tmCoMg && !tmQt.KhongDung ? tmQt.SlMauGiao : 0m;
                if (tmNt > 0 || tmMg > 0)
                {
                    var tmViTriChen = chinhDto.FindIndex(tx => tx.ThuTuMon > tmOrdMon);
                    chinhDto.Insert(tmViTriChen < 0 ? chinhDto.Count : tmViTriChen,
                        new NguyenLieuHaiCotDto(tmHang.TenHang, tmDvt, tmNt, tmMg, tmGia, tmNt * tmGia, tmMg * tmGia, tmHang.Id, tmOrdMon, tmTenMon));
                }
            }
        }

        // ----- CHUỐI theo cơ sở: có món chuối thì gộp mọi loại chuối (quả) thành 1 dòng đúng loại của cơ sở, số quả theo SoLuongChuoi -----
        if (!string.IsNullOrWhiteSpace(maCoSo))
        {
            var viTriChuoi = chinhDto.FindIndex(x => LaChuoiQua(x.TenHang, x.Dvt));
            if (viTriChuoi >= 0)
            {
                var dongChuoiCu = chinhDto[viTriChuoi];
                var coNtChuoi = chinhDto.Any(x => LaChuoiQua(x.TenHang, x.Dvt) && x.SlNhaTre > 0);
                var coMgChuoi = chinhDto.Any(x => LaChuoiQua(x.TenHang, x.Dvt) && x.SlMauGiao > 0);
                var tenChuoi = string.Equals(maCoSo.Trim(), "TC", StringComparison.OrdinalIgnoreCase) ? TenChuoiTieu : TenChuoiXanh;
                var hhChuoi = _db.HangHoas.Include(h => h.Dvt).FirstOrDefault(h => h.TenHang == tenChuoi);
                if (hhChuoi is not null)
                {
                    var (slNt, slMg) = SoLuongChuoi(maCoSo, soSuatNhaTre, soSuatMauGiao);
                    var ntChuoi = coNtChuoi ? slNt : 0m;
                    var mgChuoi = coMgChuoi ? slMg : 0m;
                    var giaChuoi = hhChuoi.DonGiaGanNhat ?? 0;
                    chinhDto.RemoveAll(x => LaChuoiQua(x.TenHang, x.Dvt));
                    chinhDto.Insert(Math.Min(viTriChuoi, chinhDto.Count),
                        new NguyenLieuHaiCotDto(hhChuoi.TenHang, hhChuoi.Dvt?.TenDvt, ntChuoi, mgChuoi, giaChuoi,
                            ntChuoi * giaChuoi, mgChuoi * giaChuoi, hhChuoi.Id, dongChuoiCu.ThuTuMon, dongChuoiCu.TenMon));
                }
            }
        }

        // ----- NƯỚC MẮM / DẦU ĂN / XÌ DẦU (mục 4: nguyên liệu chính + xế): chỉ 1 chai, để ở cột Mẫu giáo -----
        {
            for (var i = chinhDto.Count - 1; i >= 0; i--)
            {
                var x = chinhDto[i];
                if (!LaChaiMotMucBon(x.TenHang) || (x.SlNhaTre <= 0 && x.SlMauGiao <= 0)) continue;
                chinhDto[i] = x with { SlNhaTre = 0m, SlMauGiao = 1m, TtNhaTre = 0m, TtMauGiao = x.DonGia };
            }
        }

        var sangXep = XepHanhNgoXuongCuoiAnSang(sangDto);   // hành tím / hành ngò / hành tây / ngò: dưới cùng nguyên liệu ăn sáng
        return new ChiTietNgayTinhToanResponse
        {
            Sang = sangXep,
            Chinh = chinhDto,
            TongTienSang = sangXep.Sum(x => x.ThanhTien),
            TongTienChinhNhaTre = chinhDto.Sum(x => x.TtNhaTre),
            TongTienChinhMauGiao = chinhDto.Sum(x => x.TtMauGiao),
        };
    }

    /// <summary>
    /// Mục "2. Xuất kho gia vị hằng ngày" — pivot 1 dòng/hàng hoá theo 3 cột hiển thị
    /// (Ăn sáng/Nhà trẻ/Mẫu giáo). Cột "Ăn sáng" gộp CẢ 2 nhóm chi phí "Ăn sáng" (Mẫu
    /// giáo) và "Ăn sáng - Nhà trẻ" (2 khối giờ có món sáng + sĩ số ăn sáng riêng — xem
    /// Database/11_tach_an_sang_nha_tre.sql), vì giao diện/Excel chỉ có 1 cột "ăn sáng"
    /// duy nhất, không tách riêng theo khối.
    ///
    /// Thứ tự ưu tiên nguồn dữ liệu (đúng yêu cầu "gia vị hằng ngày cho cố định tất cả
    /// các ngày" — không phải nhập tay từng ngày):
    ///   1) ĐÃ có phiếu xuất kho THẬT (loại "Gia vị hằng ngày", lập ở /kho/xuat-kho) cho
    ///      đúng (cơ sở, ngày) -> dùng số liệu thật (cho phép 1 ngày cụ thể lệch định mức
    ///      khi thực tế khác, VD hết hàng phải đổi loại khác).
    ///   2) CHƯA có phiếu -> tự tính = định mức cố định (dinh_muc_gia_vi_chuan, sửa ở
    ///      /kho/dinh-muc-gia-vi) × sĩ số ăn của đúng ngày đó (dinh_muc_an_ngay) — áp
    ///      dụng SẴN cho MỌI ngày, không cần lập phiếu.
    /// </summary>
    /// <param name="anSangHienTai">Sĩ số ăn sáng (Mẫu giáo) muốn DÙNG THẲNG để tính nhánh
    /// "tự động theo định mức" — null nghĩa là dùng đúng số đã lưu trong dinh_muc_an_ngay
    /// (không xem thử gì). Cho phép trang "/cho/chi-tiet" xem thử sĩ số khác mà KHÔNG cần
    /// qua bước "tính theo tỉ lệ hiện tại/gốc" (cách đó hỏng khi gốc = 0 — chia cho 0 nên
    /// luôn ra tỉ lệ 0, đổi sĩ số thử không có tác dụng gì — xem lịch sử sửa lỗi).</param>
    /// <param name="nhaTreHienTai">Tương tự, cho nhóm "Nhà trẻ".</param>
    /// <param name="mauGiaoHienTai">Tương tự, cho nhóm "Mẫu giáo".</param>
    public async Task<GiaViThatDto> LayGiaViThatAsync(
        int coSoId, DateOnly ngay,
        int? anSangHienTai = null, int? nhaTreHienTai = null, int? mauGiaoHienTai = null,
        bool? sangLaChao = null, bool? xeChaoNhaTre = null, bool? xeChaoMauGiao = null)
    {
        static bool LaNhomAnSang(string? tenNhom) => tenNhom == "Ăn sáng" || tenNhom == "Ăn sáng - Nhà trẻ";

        // ----- 1) Định mức cố định × sĩ số -> nền TỰ ĐỘNG cho TOÀN BỘ danh sách cố định. -----
        // Nhóm cũ "Ăn sáng - Nhà trẻ" không còn dùng (giờ chỉ có 1 cột/1 sĩ số ăn sáng) -> bỏ khỏi danh sách,
        // nếu không các mã hàng cũ trùng loại (nhiều loại gạo/dầu/muối/xì dầu) sẽ hiện thêm dòng số 0.
        var dinhMuc = await _db.DinhMucGiaViChuans
            .Include(d => d.HangHoa).ThenInclude(h => h.Dvt)
            .Include(d => d.NhomChiPhi)
            .Where(d => d.NhomChiPhi.TenNhom != "Ăn sáng - Nhà trẻ")
            .ToListAsync();

        var soSuatDaLuu = await _db.DinhMucAnNgays
            .Include(d => d.NhomChiPhi)
            .Where(d => d.CoSoId == coSoId && d.Ngay == ngay)
            .ToDictionaryAsync(d => d.NhomChiPhi.TenNhom, d => d.SoSuatAn);

        int SoSuatCuaNhom(string tenNhom) => tenNhom switch
        {
            "Ăn sáng" => anSangHienTai ?? LayHoacMacDinh("Ăn sáng"),
            // Chưa có ô "xem thử" riêng cho nhóm này ở trang Chi tiết chợ -> luôn dùng số đã lưu.
            "Ăn sáng - Nhà trẻ" => LayHoacMacDinh("Ăn sáng - Nhà trẻ"),
            "Nhà trẻ" => nhaTreHienTai ?? LayHoacMacDinh("Nhà trẻ"),
            "Mẫu giáo" => mauGiaoHienTai ?? LayHoacMacDinh("Mẫu giáo"),
            _ => 0,
        };
        int LayHoacMacDinh(string tenNhom) => soSuatDaLuu.TryGetValue(tenNhom, out var ss) ? ss : 0;

        // Sáng có món Cháo? (caller truyền sẵn khi có bản nháp; không thì đọc thực đơn đã lưu) + mã cơ sở để lấy số kg gạo cháo.
        var maCoSoChao = await _db.CoSos.Where(c => c.Id == coSoId).Select(c => c.MaCoSo).FirstOrDefaultAsync();
        var chaoSang = sangLaChao ?? await _db.ThucDons.AnyAsync(t =>
            t.CoSoId == coSoId && t.NgayHoc.Ngay == ngay && t.LoaiBuaAn.TenBua == "SÁNG"
            && t.MonAn != null && t.MonAn.TenMon.StartsWith("Cháo"));
        var gaoChao = GaoChaoSang(maCoSoChao, ngay);
        // Cháo ở xế / chính chiều (không phải bữa SÁNG) của từng khối -> cộng gạo nấu cháo vào gạo hằng ngày.
        async Task<bool> CoChaoXeAsync(string tenKhoi) => await _db.ThucDons.AnyAsync(t =>
            t.CoSoId == coSoId && t.NgayHoc.Ngay == ngay && t.Khoi.TenKhoi == tenKhoi && t.LoaiBuaAn.TenBua != "SÁNG"
            && t.MonAn != null && t.MonAn.TenMon.StartsWith("Cháo"));
        var coXeChaoNt = xeChaoNhaTre ?? await CoChaoXeAsync("Nhà trẻ");
        var coXeChaoMg = xeChaoMauGiao ?? await CoChaoXeAsync("Mẫu giáo");
        var (gaoXeNt, gaoXeMg) = GaoChaoXe(maCoSoChao, coXeChaoNt && SoSuatCuaNhom("Nhà trẻ") > 0, coXeChaoMg && SoSuatCuaNhom("Mẫu giáo") > 0);

        var autoTheoHangHoa = dinhMuc.GroupBy(d => d.HangHoaId).ToDictionary(g => g.Key, g =>
        {
            var dauTien = g.First();
            decimal SlTheoNhom(Func<string?, bool> khopNhom) => g
                .Where(d => khopNhom(d.NhomChiPhi.TenNhom))
                .Sum(d => d.DinhLuong1Suat * SoSuatCuaNhom(d.NhomChiPhi.TenNhom));

            var slAnSang = SlTheoNhom(LaNhomAnSang);
            var slNhaTre = SlTheoNhom(t => t == "Nhà trẻ");
            var slMauGiao = SlTheoNhom(t => t == "Mẫu giáo");
            var donGia = dauTien.HangHoa.DonGiaGanNhat ?? 0;
            // GIA VỊ: sổ xuất gia vị ghi theo 0,1 (kể cả chai/gói — dùng dở gói), KHÔNG tròn lên số nguyên như thực phẩm.
            // (Tròn lên làm tiền gia vị phình ~55% so với thực chi.)
            // Riêng đơn vị GÓI: làm tròn số nguyên (dưới 0,5 xuống, từ 0,5 lên).
            var chuSoGiaVi = LaDonViGoi(dauTien.HangHoa.Dvt?.TenDvt) ? 0 : 1;
            slAnSang = Math.Round(slAnSang, chuSoGiaVi, MidpointRounding.AwayFromZero);
            slNhaTre = Math.Round(slNhaTre, chuSoGiaVi, MidpointRounding.AwayFromZero);
            slMauGiao = Math.Round(slMauGiao, chuSoGiaVi, MidpointRounding.AwayFromZero);
            if (LaHangGao(dauTien.HangHoa.TenHang))
            {
                // QUY TẮC GẠO (thay định mức DB): NT = suất × 0,055; MG = suất × 0,065; sáng = gạo cháo theo cơ sở/thứ.
                slNhaTre = SoSuatCuaNhom("Nhà trẻ") * GaoNhaTreMotSuat;
                slMauGiao = SoSuatCuaNhom("Mẫu giáo") * GaoMauGiaoMotSuat;
                slAnSang = (chaoSang && SoSuatCuaNhom("Ăn sáng") > 0) ? gaoChao : 0m;
                // Ngày có món cháo ở xế/chính chiều: cộng thêm gạo nấu cháo vào gạo nguyên liệu hằng ngày.
                // Ngày có CẢ cháo sáng và cháo xế: dồn tổng gạo (sáng + xế) vào một ô ở cột Ăn sáng; chỉ cháo xế: cộng vào cột Nhà trẻ / Mẫu giáo.
                if (slAnSang > 0 && (gaoXeNt + gaoXeMg) > 0) slAnSang += gaoXeNt + gaoXeMg;
                else { slNhaTre += gaoXeNt; slMauGiao += gaoXeMg; }
            }
            return new NguyenLieuBaCotDto(
                dauTien.HangHoaId, dauTien.HangHoa.TenHang, dauTien.HangHoa.Dvt?.TenDvt,
                slAnSang, slNhaTre, slMauGiao, donGia,
                slAnSang * donGia, slNhaTre * donGia, slMauGiao * donGia);
        });

        // ----- 1b) GIA VỊ MỖI NGÀY theo số cố định của từng cơ sở (thay định mức DB), kể cả ngày không có cháo.
        // Mã hàng: 412 bột canh, 428 bột nêm, 450 dầu, 504 nước mắm, 539 xì dầu, 469 đường.
        // Cột nào không có suất ăn (suất = 0) thì giữ 0. Mục 2 CỐ ĐỊNH 8 mặt hàng (gạo, muối + 6 gia vị trên);
        // nguyên liệu món cháo (nếp, đậu xanh, cà rốt, dầu mè, dầu gấc) nằm ở mục 3 — xem NguyenLieuChaoSang/Tinh().
        var ma = (maCoSoChao ?? "").Trim().ToUpperInvariant();
        var dt = ma is "ĐT" or "DT";
        var coSang = SoSuatCuaNhom("Ăn sáng") > 0;
        var coNt = SoSuatCuaNhom("Nhà trẻ") > 0;
        var coMg = SoSuatCuaNhom("Mẫu giáo") > 0;
        // Bảng số cố định mỗi ngày (kg / chai / gói) — mỗi dòng: (mã hàng, Ăn sáng, Nhà trẻ, Mẫu giáo):
        //   NGÀY THƯỜNG (Thứ 2–6): TC và PH   — ăn sáng / nhà trẻ / mẫu giáo
        //     412 bột canh : 0,5 / 0,5 / 2   (3 gói/ngày)      428 bột nêm   : 0,2 / 0,2 / 0,6
        //     450 dầu (lít): 0,3 / 0,2 / 2,1                   504 nước mắm  : 0,3 / 0,2 / 1,6
        //     539 xì dầu   : 0,3 / 0,2 / 0,6                   469 đường trắng: 0,3 / 0,2 / 0,6
        //   NGÀY THƯỜNG: ĐT — mọi mặt hàng: 0,1 / 0,1 / 0,2
        //   THỨ 7: TC và PH
        //     412 bột canh : 0,2 / 0,2 / 1                     428 bột nêm   : 0,2 / 0,2 / 0,5
        //     450 dầu (lít): 0,2 / 0,1 / 0,9                   504 nước mắm  : 0,2 / 0,2 / 0,9
        //     539 xì dầu   : 0,2 / 0,2 / 0,4                   469 đường trắng: 0,2 / 0,2 / 0,4
        //   THỨ 7: ĐT — chưa có số riêng nên dùng số ngày thường của ĐT.
        // Số cố định mỗi ngày lấy từ BẢNG QUY TẮC (trang /quy-tac-nguyen-lieu, chế độ "Mỗi ngày"): theo cơ sở + Thứ 2–6 / Thứ 7.
        // Mặt hàng "không dùng" ở cơ sở này thì bỏ dòng; mặt hàng chưa có quy tắc nào khớp thì giữ số tính từ định mức gia vị.
        {
            var quyTac = ChonQuyTac(LayQuyTac(QuyTacNguyenLieu.MoiNgay), maCoSoChao, ngay);
            foreach (var (hangId, qt) in quyTac)
            {
                if (qt.KhongDung) { autoTheoHangHoa.Remove(hangId); continue; }
                var hh = qt.HangHoa;
                var sang = coSang ? qt.SlAnSang : 0m;
                var nt = coNt ? qt.SlNhaTre : 0m;
                var mg = coMg ? qt.SlMauGiao : 0m;
                // Xì dầu và nước mắm: MỖI LOẠI tối đa 1 chai cho Mẫu giáo.
                if (BatDauBang(hh.TenHang, "Xì dầu") || BatDauBang(hh.TenHang, "Nước mắm")) mg = Math.Min(mg, 1m);
                var dg = hh.DonGiaGanNhat ?? 0;
                autoTheoHangHoa[hangId] = new NguyenLieuBaCotDto(
                    hh.Id, hh.TenHang, hh.Dvt?.TenDvt, sang, nt, mg, dg, sang * dg, nt * dg, mg * dg);
            }
        }

        // ----- 2) Phiếu xuất kho thật (nếu có) -> GHI ĐÈ TỪNG HÀNG HOÁ lên nền tự động ở
        // trên (hàng hoá nào có mặt trong phiếu, kể cả giá trị 0, nghĩa là ngày này ĐÃ
        // CHỈNH TAY mặt hàng đó — "Xóa" 1 dòng ở trang Chi tiết ngày cũng lưu 1 dòng = 0
        // vào đây để không bị định mức tự động "hồi sinh" lại). Hàng hoá nào CHƯA từng
        // đụng tới thì vẫn giữ nguyên số tự động — nhờ vậy sửa/xóa 1 dòng không bao giờ
        // làm mất các dòng khác, và danh sách luôn đủ/cố định dù phiếu có đủ hay không. -----
        var phieu = await _db.PhieuXuatKhos
            .Where(p => p.CoSoId == coSoId && p.NgayXuat == ngay && p.LoaiXuat == LoaiXuatKho.GiaViHangNgay)
            .Include(p => p.ChiTiets).ThenInclude(c => c.HangHoa).ThenInclude(h => h.Dvt)
            .Include(p => p.ChiTiets).ThenInclude(c => c.NhomChiPhi)
            .FirstOrDefaultAsync();

        var ketQua = new Dictionary<int, NguyenLieuBaCotDto>(autoTheoHangHoa);
        if (phieu is not null)
        {
            decimal TongTheoNhom(IEnumerable<ChiTietXuatKho> dong, Func<string?, bool> khopNhom, Func<ChiTietXuatKho, decimal> chonSo)
                => dong.Where(c => khopNhom(c.NhomChiPhi?.TenNhom)).Sum(chonSo);

            foreach (var g in phieu.ChiTiets.GroupBy(c => c.HangHoaId))
            {
                var dauTien = g.First();
                ketQua[g.Key] = new NguyenLieuBaCotDto(
                    dauTien.HangHoaId, dauTien.HangHoa.TenHang, dauTien.HangHoa.Dvt?.TenDvt,
                    TongTheoNhom(g, LaNhomAnSang, c => c.SoLuong),
                    TongTheoNhom(g, t => t == "Nhà trẻ", c => c.SoLuong),
                    TongTheoNhom(g, t => t == "Mẫu giáo", c => c.SoLuong),
                    dauTien.DonGia,
                    TongTheoNhom(g, LaNhomAnSang, c => c.ThanhTien),
                    TongTheoNhom(g, t => t == "Nhà trẻ", c => c.ThanhTien),
                    TongTheoNhom(g, t => t == "Mẫu giáo", c => c.ThanhTien));
            }
        }

        var danhSach = ketQua.Values.OrderBy(x => x.TenHang).ToList();
        return new GiaViThatDto { DanhSach = danhSach, CoPhieu = phieu is not null };
    }

    /// <summary>
    /// Mục "3. Chi tiền thực phẩm sáng" + "4. Chi tiền thực phẩm ăn chính + xế" — đọc
    /// THẲNG từ phiếu xuất kho thật (loại "Thực phẩm sáng" / "Thực phẩm chính + xế") đã
    /// lập ở /kho/xuat-kho cho đúng (cơ sở, ngày), giống hệt cách trang "/cho/chi-tiet"
    /// (<see cref="MamNon.ThucDon.Web.Controllers.ChoController"/>) đang dựng 2 mục này —
    /// dùng cho Excel để KHÔNG lệch với trang xem trực tuyến/số liệu thật đã ghi sổ.
    /// </summary>
    public async Task<ThucPhamThatDto> LayThucPhamThatAsync(int coSoId, DateOnly ngay, string? maCoSo = null)
    {
        var phieus = await _db.PhieuXuatKhos
            .Where(p => p.CoSoId == coSoId && p.NgayXuat == ngay
                && (p.LoaiXuat == LoaiXuatKho.ThucPhamSang || p.LoaiXuat == LoaiXuatKho.ThucPhamChinhXe))
            .Include(p => p.ChiTiets).ThenInclude(c => c.HangHoa).ThenInclude(h => h.Dvt)
            .Include(p => p.ChiTiets).ThenInclude(c => c.NhomChiPhi)
            .ToListAsync();

        var phieuSang = phieus.FirstOrDefault(p => p.LoaiXuat == LoaiXuatKho.ThucPhamSang);
        var phieuChinhXe = phieus.FirstOrDefault(p => p.LoaiXuat == LoaiXuatKho.ThucPhamChinhXe);

        var sangDto = phieuSang?.ChiTiets
            .Select(c =>
            {
                var goi = LaDonViGoi(c.HangHoa.Dvt?.TenDvt);
                var sl = goi ? Math.Round(c.SoLuong, 0, MidpointRounding.AwayFromZero) : c.SoLuong;
                return new NguyenLieuMotCotDto(c.HangHoa.TenHang, c.HangHoa.Dvt?.TenDvt, sl, c.DonGia, goi ? sl * c.DonGia : c.ThanhTien, c.HangHoaId);
            })
            .Where(x => !LaHanhBiAnTaiTc(x.TenHang, maCoSo))
            .OrderBy(x => x.TenHang)
            .ToList() ?? new List<NguyenLieuMotCotDto>();

        static decimal TongTheoNhom(IEnumerable<ChiTietXuatKho> dong, string tenNhom, Func<ChiTietXuatKho, decimal> chonSo)
            => dong.Where(c => c.NhomChiPhi?.TenNhom == tenNhom).Sum(chonSo);

        var chinhDto = phieuChinhXe?.ChiTiets
            .GroupBy(c => c.HangHoaId)
            .Select(g =>
            {
                var dauTien = g.First();
                var goi = LaDonViGoi(dauTien.HangHoa.Dvt?.TenDvt);
                var slNt = TongTheoNhom(g, "Nhà trẻ", c => c.SoLuong);
                var slMg = TongTheoNhom(g, "Mẫu giáo", c => c.SoLuong);
                if (goi)
                {
                    slNt = Math.Round(slNt, 0, MidpointRounding.AwayFromZero);
                    slMg = Math.Round(slMg, 0, MidpointRounding.AwayFromZero);
                }
                return new NguyenLieuHaiCotDto(
                    dauTien.HangHoa.TenHang, dauTien.HangHoa.Dvt?.TenDvt,
                    slNt, slMg,
                    dauTien.DonGia,
                    goi ? slNt * dauTien.DonGia : TongTheoNhom(g, "Nhà trẻ", c => c.ThanhTien),
                    goi ? slMg * dauTien.DonGia : TongTheoNhom(g, "Mẫu giáo", c => c.ThanhTien),
                    g.Key);
            })
            .Where(x => !LaHanhBiAnTaiTc(x.TenHang, maCoSo))
            .OrderBy(x => x.TenHang)
            .ToList() ?? new List<NguyenLieuHaiCotDto>();

        return new ThucPhamThatDto
        {
            Sang = XepHanhNgoXuongCuoiAnSang(sangDto),   // hành tím / hành ngò / hành tây / ngò: dưới cùng nguyên liệu ăn sáng
            Chinh = chinhDto,
            CoPhieuSang = phieuSang is not null,
            CoPhieuChinhXe = phieuChinhXe is not null,
        };
    }
}
