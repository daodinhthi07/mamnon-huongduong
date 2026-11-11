namespace MamNon.ThucDon.Web.Infrastructure.Entities;

public enum LoaiHangHoa { ThucPhamTuoi, GiaVi, Khac }

public sealed class CoSo
{
    public int Id { get; set; }
    public string MaCoSo { get; set; } = string.Empty;
    public string TenCoSo { get; set; } = string.Empty;
    public string? DiaChi { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<NgayHoc> NgayHocs { get; set; } = new List<NgayHoc>();
    public ICollection<ThucDon> ThucDons { get; set; } = new List<ThucDon>();
    public ICollection<PhieuNhapKho> PhieuNhapKhos { get; set; } = new List<PhieuNhapKho>();
    public ICollection<PhieuXuatKho> PhieuXuatKhos { get; set; } = new List<PhieuXuatKho>();
}

public sealed class DonViTinh
{
    public int Id { get; set; }
    public string TenDvt { get; set; } = string.Empty;

    public ICollection<HangHoa> HangHoas { get; set; } = new List<HangHoa>();
}

public sealed class KhoiHoc
{
    public int Id { get; set; }
    public string TenKhoi { get; set; } = string.Empty;
    public byte ThuTu { get; set; }

    public ICollection<LoaiBuaAn> LoaiBuaAns { get; set; } = new List<LoaiBuaAn>();
    public ICollection<ThucDon> ThucDons { get; set; } = new List<ThucDon>();
    public ICollection<DinhMucTyLeNganSach> DinhMucTyLeNganSachs { get; set; } = new List<DinhMucTyLeNganSach>();
}

public sealed class NhomChiPhi
{
    public int Id { get; set; }
    public string TenNhom { get; set; } = string.Empty;

    public ICollection<DinhMucAnNgay> DinhMucAnNgays { get; set; } = new List<DinhMucAnNgay>();
    public ICollection<ChiTietXuatKho> ChiTietXuatKhos { get; set; } = new List<ChiTietXuatKho>();
}

public sealed class HangMucNganSach
{
    public int Id { get; set; }
    public string TenHangMuc { get; set; } = string.Empty;
    public byte ThuTu { get; set; }

    public ICollection<LoaiBuaAn> LoaiBuaAns { get; set; } = new List<LoaiBuaAn>();
    public ICollection<DinhMucTyLeNganSach> DinhMucTyLeNganSachs { get; set; } = new List<DinhMucTyLeNganSach>();
}

/// <summary>
/// Nhóm bữa dùng chung cho mọi khối (Sáng/Canh/Mặn/Xào/Tráng miệng/Xế/...).
/// Mỗi <see cref="LoaiBuaAn"/> (bữa cụ thể của 1 khối) VÀ mỗi <see cref="MonAn"/>
/// đều gắn vào đúng 1 nhóm ở đây, để khi soạn thực đơn dropdown chọn món chỉ
/// hiện đúng món cùng nhóm với bữa đang soạn.
/// </summary>
public sealed class NhomBuaAn
{
    public int Id { get; set; }
    public string TenNhom { get; set; } = string.Empty;
    public byte ThuTu { get; set; }

    public ICollection<LoaiBuaAn> LoaiBuaAns { get; set; } = new List<LoaiBuaAn>();
    public ICollection<MonAn> MonAns { get; set; } = new List<MonAn>();
}

public sealed class HangHoa
{
    public int Id { get; set; }
    public string? MaHang { get; set; }
    public string TenHang { get; set; } = string.Empty;
    public int DvtId { get; set; }
    public LoaiHangHoa LoaiHang { get; set; } = LoaiHangHoa.ThucPhamTuoi;
    public decimal? DonGiaGanNhat { get; set; }
    /// <summary>Ghi chú của bảng giá (VD "Tăng 10.000đ so với năm trước").</summary>
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; } = true;

    public DonViTinh Dvt { get; set; } = null!;
    public ICollection<DinhLuongNguyenLieu> DinhLuongNguyenLieus { get; set; } = new List<DinhLuongNguyenLieu>();
    public ICollection<ChiTietNhapKho> ChiTietNhapKhos { get; set; } = new List<ChiTietNhapKho>();
    public ICollection<ChiTietXuatKho> ChiTietXuatKhos { get; set; } = new List<ChiTietXuatKho>();
}
