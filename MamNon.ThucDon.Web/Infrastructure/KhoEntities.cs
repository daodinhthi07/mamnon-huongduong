namespace MamNon.ThucDon.Web.Infrastructure.Entities;

public enum LoaiXuatKho { GiaViHangNgay, ThucPhamSang, ThucPhamChinhXe }

public sealed class PhieuNhapKho
{
    public int Id { get; set; }
    public int CoSoId { get; set; }
    public DateOnly NgayNhap { get; set; }
    public string? SoPhieu { get; set; }
    public string? NguoiLap { get; set; }
    public string? ThuKho { get; set; }
    public string? KeToan { get; set; }
    public string? HieuTruong { get; set; }
    public string? GhiChu { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CoSo CoSo { get; set; } = null!;
    public ICollection<ChiTietNhapKho> ChiTiets { get; set; } = new List<ChiTietNhapKho>();
}

public sealed class ChiTietNhapKho
{
    public int Id { get; set; }
    public int PhieuNhapId { get; set; }
    public int HangHoaId { get; set; }
    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }

    /// <summary>Cột generated (STORED) ở phía SQLite — không set tay, EF chỉ đọc.</summary>
    public decimal ThanhTien { get; private set; }

    public PhieuNhapKho Phieu { get; set; } = null!;
    public HangHoa HangHoa { get; set; } = null!;
}

public sealed class PhieuXuatKho
{
    public int Id { get; set; }
    public int CoSoId { get; set; }
    public DateOnly NgayXuat { get; set; }
    public LoaiXuatKho LoaiXuat { get; set; }
    public string? NguoiLap { get; set; }
    public string? ThuKho { get; set; }
    public string? KeToan { get; set; }
    public string? HieuTruong { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CoSo CoSo { get; set; } = null!;
    public ICollection<ChiTietXuatKho> ChiTiets { get; set; } = new List<ChiTietXuatKho>();
}

public sealed class ChiTietXuatKho
{
    public int Id { get; set; }
    public int PhieuXuatId { get; set; }
    public int HangHoaId { get; set; }

    /// <summary>NULL nếu phiếu không tách theo nhóm (VD: Thực phẩm sáng).</summary>
    public int? NhomChiPhiId { get; set; }

    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }

    /// <summary>Cột generated (STORED) ở phía SQLite — không set tay, EF chỉ đọc.</summary>
    public decimal ThanhTien { get; private set; }

    public PhieuXuatKho Phieu { get; set; } = null!;
    public HangHoa HangHoa { get; set; } = null!;
    public NhomChiPhi? NhomChiPhi { get; set; }
}

/// <summary>
/// Định mức gia vị hằng ngày CỐ ĐỊNH theo suất — 1 dòng = định lượng/suất của 1 hàng hoá
/// cho 1 nhóm chi phí (Ăn sáng / Ăn sáng - Nhà trẻ / Nhà trẻ / Mẫu giáo). Áp dụng CHO MỌI
/// NGÀY, không phải nhập tay từng ngày — xem
/// <see cref="MamNon.ThucDon.Web.Services.ChiTietNgayTinhToanService.LayGiaViThatAsync"/>:
/// nếu ngày đó ĐÃ có phiếu xuất kho "Gia vị hằng ngày" thật thì ưu tiên dùng phiếu thật
/// (cho phép 1 ngày cụ thể lệch định mức, VD hết hàng phải đổi loại khác); nếu CHƯA có
/// phiếu thì tự tính = định mức × sĩ số ăn của đúng ngày đó (dinh_muc_an_ngay).
/// </summary>
public sealed class DinhMucGiaViChuan
{
    public int Id { get; set; }
    public int HangHoaId { get; set; }
    public int NhomChiPhiId { get; set; }
    public decimal DinhLuong1Suat { get; set; }

    public HangHoa HangHoa { get; set; } = null!;
    public NhomChiPhi NhomChiPhi { get; set; } = null!;
}


/// <summary>
/// QUY TẮC NGUYÊN LIỆU CỐ ĐỊNH — số lượng cố định (không tính theo công thức món) cho 1 mặt hàng, theo cơ sở / loại ngày.
/// Sửa/thêm ở trang "Quy tắc nguyên liệu" (/quy-tac-nguyen-lieu), không cần sửa code.
///   CheDo = MoiNgay (0): luôn xuất mỗi ngày ở mục "2. Gia vị hằng ngày" (cột Ăn sáng / Nhà trẻ / Mẫu giáo).
///   CheDo = KhiCoTrongThucDon (1): chỉ khi thực đơn có món dùng mặt hàng này; khối nào có món dùng thì cột của khối đó = số cố định
///           (mục 3 sáng / mục 4 nhà trẻ + mẫu giáo), không nhân số suất.
/// MaCoSo: danh sách mã cơ sở cách nhau dấu phẩy ("TC,PH"); trống = mọi cơ sở. LoaiNgay: 0 mọi ngày, 1 Thứ 2–6, 2 Thứ 7.
/// KhongDung = mặt hàng này KHÔNG dùng ở các cơ sở đó (bỏ dòng). Khi có nhiều quy tắc cùng khớp, quy tắc cụ thể hơn
/// (có chọn cơ sở, có chọn loại ngày) được ưu tiên.
/// </summary>
public sealed class QuyTacNguyenLieu
{
    public const int MoiNgay = 0;
    public const int KhiCoTrongThucDon = 1;

    public int Id { get; set; }
    public int HangHoaId { get; set; }
    /// <summary>NULL = quy tắc theo MẶT HÀNG (khi mặt hàng có trong thực đơn). Có giá trị = quy tắc theo MÓN: khi MÓN này có trong thực đơn
    /// thì phần mặt hàng do món này sinh ra được thay bằng số cố định (các món khác dùng cùng mặt hàng vẫn tính theo công thức).</summary>
    public int? MonAnId { get; set; }
    public string? MaCoSo { get; set; }
    public int LoaiNgay { get; set; }
    public int CheDo { get; set; }
    public decimal SlAnSang { get; set; }
    public decimal SlNhaTre { get; set; }
    public decimal SlMauGiao { get; set; }
    public bool KhongDung { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; } = true;

    public HangHoa HangHoa { get; set; } = null!;
    public MonAn? MonAn { get; set; }

    public bool KhopCoSo(string? ma)
    {
        if (string.IsNullOrWhiteSpace(MaCoSo)) return true;
        var m = Chuan(ma);
        return MaCoSo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => Chuan(x) == m);
    }

    public bool KhopNgay(DateOnly? ngay) =>
        LoaiNgay == 0 || (ngay is { } n && (LoaiNgay == 2) == (n.DayOfWeek == DayOfWeek.Saturday));

    public int DoCuThe => (string.IsNullOrWhiteSpace(MaCoSo) ? 0 : 2) + (LoaiNgay == 0 ? 0 : 1);

    private static string Chuan(string? ma) { var m = (ma ?? "").Trim().ToUpperInvariant(); return m == "DT" ? "ĐT" : m; }
}
