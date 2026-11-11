namespace MamNon.ThucDon.Web.ViewModels;

public sealed class BangGiaRowVm
{
    public int Id { get; set; }
    public string? MaHang { get; set; }
    public string TenHang { get; set; } = string.Empty;
    public string Dvt { get; set; } = string.Empty;
    public string LoaiHang { get; set; } = string.Empty;
    public decimal DonGia { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; }
}

public sealed class BangGiaFormVm
{
    public int? Id { get; set; }
    public string? MaHang { get; set; }
    public string TenHang { get; set; } = string.Empty;
    public int? DvtId { get; set; }
    /// <summary>Nhập tên ĐVT mới nếu ĐVT cần chưa có trong danh sách.</summary>
    public string? DvtMoi { get; set; }
    public string LoaiHang { get; set; } = "Thực phẩm tươi";
    public decimal DonGia { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DvtOptionVm> DsDvt { get; set; } = new();
}

public sealed record DvtOptionVm(int Id, string TenDvt);

/// <summary>1 dòng đọc được từ file Excel bảng giá.</summary>
public sealed class DongBangGiaExcel
{
    public int SoDong { get; set; }
    public string? MaHang { get; set; }
    public string TenHang { get; set; } = string.Empty;
    public string? Dvt { get; set; }
    public string? LoaiHang { get; set; }
    public decimal DonGia { get; set; }
    public string? GhiChu { get; set; }
}

/// <summary>Dữ liệu sửa nhanh 1 dòng ngay trên bảng giá (gửi bằng fetch). Giá nhận dạng chuỗi để không phụ thuộc culture máy chủ.</summary>
public sealed class LuuNhanhVm
{
    public string? TenHang { get; set; }
    public string? Dvt { get; set; }
    public string? DonGia { get; set; }
    public string? GhiChu { get; set; }
}

/// <summary>1 dòng trong bảng xem trước khi nhập: sẽ cập nhật / thêm mới / bỏ qua.</summary>
public sealed class DongXemTruocVm
{
    public int SoDong { get; set; }
    public string HanhDong { get; set; } = string.Empty;   // capnhat | them | khongdoi | bo
    public string TenHang { get; set; } = string.Empty;
    public string? MaHang { get; set; }
    public string? Dvt { get; set; }
    public string? DvtCu { get; set; }
    public decimal? GiaCu { get; set; }
    public decimal GiaMoi { get; set; }
    /// <summary>Ghi chú của mặt hàng đọc từ file Excel (cột Ghi chú).</summary>
    public string? Note { get; set; }
    /// <summary>Cảnh báo / lý do của dòng này khi xem trước.</summary>
    public string? GhiChu { get; set; }
}

public sealed class NhapExcelVm
{
    public bool CoXemTruoc { get; set; }
    public string? TenFile { get; set; }
    public string? DuLieuJson { get; set; }
    /// <summary>Mặt hàng hiện có nhưng không có trong file và không dùng ở đâu -> bị XÓA.</summary>
    public List<string> SeXoa { get; set; } = new();
    /// <summary>Mặt hàng không có trong file nhưng đang dùng trong công thức món / kho -> giữ lại, xóa giá + ghi chú.</summary>
    public List<string> SeGiu { get; set; } = new();
    public List<DongXemTruocVm> Dong { get; set; } = new();
    public List<string> CanhBao { get; set; } = new();
    public int SoCapNhat => Dong.Count(d => d.HanhDong == "capnhat");
    public int SoThem => Dong.Count(d => d.HanhDong == "them");
    public int SoKhongDoi => Dong.Count(d => d.HanhDong == "khongdoi");
    public int SoBo => Dong.Count(d => d.HanhDong == "bo");
}
