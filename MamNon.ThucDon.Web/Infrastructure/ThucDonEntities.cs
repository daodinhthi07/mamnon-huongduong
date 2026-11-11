namespace MamNon.ThucDon.Web.Infrastructure.Entities;

public enum TrangThaiNgay { HocBinhThuong, NghiLe, NghiKhac }

public enum TrangThaiThucDon { Nhap, ChoDuyet, DaDuyet }

public sealed class LoaiBuaAn
{
    public int Id { get; set; }
    public int KhoiId { get; set; }
    public string TenBua { get; set; } = string.Empty;
    public byte ThuTu { get; set; }

    /// <summary>NULL với các bữa thuộc nhóm "Ăn sáng" (SÁNG/PHỤ SÁNG/TRƯA/CHÍNH CHIỀU).</summary>
    public int? HangMucId { get; set; }

    /// <summary>Nhóm bữa (Sáng/Canh/Mặn/Xào/...) — dùng để lọc dropdown chọn món cùng nhóm.</summary>
    public int? NhomBuaAnId { get; set; }

    public KhoiHoc Khoi { get; set; } = null!;
    public HangMucNganSach? HangMuc { get; set; }
    public NhomBuaAn? NhomBuaAn { get; set; }
    public ICollection<ThucDon> ThucDons { get; set; } = new List<ThucDon>();
}

public sealed class MonAn
{
    public int Id { get; set; }
    public string TenMon { get; set; } = string.Empty;
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Nhóm bữa món này thuộc về (Sáng/Canh/Mặn/Xào/Tráng miệng/Xế/...).</summary>
    public int? NhomBuaAnId { get; set; }

    public NhomBuaAn? NhomBuaAn { get; set; }
    public ICollection<DinhLuongNguyenLieu> DinhLuongNguyenLieus { get; set; } = new List<DinhLuongNguyenLieu>();
    public ICollection<ThucDon> ThucDons { get; set; } = new List<ThucDon>();
}

public sealed class DinhLuongNguyenLieu
{
    public int Id { get; set; }
    public int MonAnId { get; set; }
    public int HangHoaId { get; set; }

    /// <summary>Lượng dùng cho 1 suất ăn, theo ĐVT của HangHoa (Mẫu giáo / ăn sáng).</summary>
    public decimal DinhLuong { get; set; }

    /// <summary>Lượng dùng cho 1 suất của khối NHÀ TRẺ (cột dinh_luong_nha_tre). NULL = Nhà trẻ dùng chung <see cref="DinhLuong"/>.</summary>
    public decimal? DinhLuongNhaTre { get; set; }

    /// <summary>true = nguyên liệu CHÍNH của món: khi bảng kê còn dư / thiếu tiền thì số lượng dòng này được nhích trước
    /// (cột la_nguyen_lieu_chinh, mặc định 0). Món không đánh dấu dòng nào thì tự lấy dòng tính theo cân có thành tiền lớn nhất.</summary>
    public bool LaNguyenLieuChinh { get; set; }

    public MonAn MonAn { get; set; } = null!;
    public HangHoa HangHoa { get; set; } = null!;
}

/// <summary>Định lượng RIÊNG của một món cho một chợ (PH / ĐT). Món nào có dòng ở đây cho chợ đang tính thì dùng TOÀN BỘ công thức
/// riêng này thay cho bảng chung <see cref="DinhLuongNguyenLieu"/> (bảng chung = định lượng chuẩn của chợ TC).</summary>
public sealed class DinhLuongNguyenLieuTheoCoSo
{
    public int Id { get; set; }
    public int MonAnId { get; set; }
    public int HangHoaId { get; set; }
    /// <summary>Mã chợ: "TC", "PH", "ĐT".</summary>
    public string MaCoSo { get; set; } = "";
    /// <summary>Lượng dùng cho 1 suất ăn, theo ĐVT của HangHoa (Mẫu giáo / ăn sáng).</summary>
    public decimal DinhLuong { get; set; }
    /// <summary>Lượng dùng cho 1 suất của khối NHÀ TRẺ tại chợ này. NULL = Nhà trẻ dùng chung <see cref="DinhLuong"/>.</summary>
    public decimal? DinhLuongNhaTre { get; set; }
    public string? GhiChu { get; set; }

    public MonAn MonAn { get; set; } = null!;
    public HangHoa HangHoa { get; set; } = null!;
}

public sealed class NgayHoc
{
    public int Id { get; set; }
    public int CoSoId { get; set; }
    public DateOnly Ngay { get; set; }
    public TrangThaiNgay TrangThai { get; set; } = TrangThaiNgay.HocBinhThuong;
    public string? GhiChu { get; set; }

    public CoSo CoSo { get; set; } = null!;
    public ICollection<ThucDon> ThucDons { get; set; } = new List<ThucDon>();
}

public sealed class DinhMucAnNgay
{
    public int Id { get; set; }
    public int CoSoId { get; set; }
    public int NhomChiPhiId { get; set; }
    public DateOnly Ngay { get; set; }
    public decimal TienAn1Chau { get; set; }
    public int SoSuatAn { get; set; }

    public decimal TienDuocPhepChi => TienAn1Chau * SoSuatAn;

    public CoSo CoSo { get; set; } = null!;
    public NhomChiPhi NhomChiPhi { get; set; } = null!;
}

public sealed class DinhMucTyLeNganSach
{
    public int Id { get; set; }
    public int KhoiId { get; set; }
    public int HangMucId { get; set; }
    public decimal TienDinhMuc { get; set; }
    public DateOnly NgayApDungTu { get; set; }
    public string? GhiChu { get; set; }

    public KhoiHoc Khoi { get; set; } = null!;
    public HangMucNganSach HangMuc { get; set; } = null!;
}

public sealed class ThucDon
{
    public int Id { get; set; }
    public int CoSoId { get; set; }
    public int KhoiId { get; set; }
    public int NgayHocId { get; set; }
    public int LoaiBuaAnId { get; set; }

    /// <summary>NULL nếu ngày đó không có bữa (VD: nghỉ lễ).</summary>
    public int? MonAnId { get; set; }

    public TrangThaiThucDon TrangThai { get; set; } = TrangThaiThucDon.Nhap;
    public string? NguoiLap { get; set; }
    public string? NguoiDuyet { get; set; }
    public DateOnly? NgayLap { get; set; }
    public DateOnly? NgayDuyet { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CoSo CoSo { get; set; } = null!;
    public KhoiHoc Khoi { get; set; } = null!;
    public NgayHoc NgayHoc { get; set; } = null!;
    public LoaiBuaAn LoaiBuaAn { get; set; } = null!;
    public MonAn? MonAn { get; set; }
}
