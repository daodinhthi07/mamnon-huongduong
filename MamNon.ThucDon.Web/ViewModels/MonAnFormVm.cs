using System.ComponentModel.DataAnnotations;
using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class MonAnRowVm
{
    public int Id { get; set; }
    public string TenMon { get; set; } = string.Empty;
    public string? TenNhomBuaAn { get; set; }
    public int SoNguyenLieu { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DongCongThucVm
{
    public int? HangHoaId { get; set; }
    public decimal SoLuong { get; set; }

    /// <summary>Định lượng/suất của khối NHÀ TRẺ. Để trống (null) = Nhà trẻ dùng chung định lượng Mẫu giáo. Chỉ hiện khi sửa món.</summary>
    public decimal? SoLuongNhaTre { get; set; }
}

/// <summary>
/// Form tạo/sửa 1 món ăn kèm công thức nguyên liệu. Vì nhà bếp biết "nấu cho
/// 250 cháu cần bao nhiêu" tự nhiên hơn "định lượng/1 suất", form cho nhập
/// SỐ LƯỢNG THEO 1 SỐ SUẤT THAM CHIẾU (đúng như ảnh chụp thực tế: 1 dòng
/// nguyên liệu ghi rõ số lượng cho ngày hôm đó) — hệ thống tự quy đổi ra
/// định lượng/suất = số lượng / số suất tham chiếu, rồi mới lưu.
/// </summary>
public sealed class MonAnFormVm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên món")]
    public string TenMon { get; set; } = string.Empty;

    public string? GhiChu { get; set; }

    /// <summary>Món này thuộc nhóm bữa nào (Sáng/Canh/Mặn/Xào/Tráng miệng/Xế/...) — dùng để
    /// lọc dropdown chọn món khi soạn thực đơn, tránh chọn nhầm món trái buổi/trái bữa.</summary>
    [Required(ErrorMessage = "Vui lòng chọn loại món (bữa)")]
    public int? NhomBuaAnId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Số suất tham chiếu phải lớn hơn 0")]
    public int SoSuatThamChieu { get; set; } = 1;

    public List<DongCongThucVm> CacDong { get; set; } = new() { new DongCongThucVm() };

    // ----- Công thức theo CƠ SỞ (chợ) — chỉ dùng ở màn hình SỬA món -----

    /// <summary>Cơ sở đang xem/sửa: "TC" (mặc định = công thức chuẩn, bảng chung), "PH", "ĐT".</summary>
    public string MaCoSo { get; set; } = "TC";

    /// <summary>Danh sách mã cơ sở cho dropdown (TC luôn đứng đầu).</summary>
    public List<string> DsMaCoSo { get; set; } = new() { "TC" };

    /// <summary>Cơ sở PH/ĐT: true = bỏ công thức riêng, dùng lại công thức chung (TC).</summary>
    public bool DungCongThucChung { get; set; }

    /// <summary>Chỉ để hiển thị: cơ sở đang chọn (PH/ĐT) hiện CÓ công thức riêng.</summary>
    public bool CoCongThucRieng { get; set; }

    /// <summary>Các cơ sở khác được tick "Áp dụng công thức này" — sao chép NGUYÊN công thức đang sửa sang các cơ sở đó.</summary>
    public List<string> ApDungChoCoSo { get; set; } = new();

    /// <summary>Chỉ để hiển thị: món đang có ghi chú "Món ăn nguyên liệu chuẩn" (mặc định không sửa).</summary>
    public bool MonChuan { get; set; }

    /// <summary>Phải tick mới được sửa món có ghi chú "Món ăn nguyên liệu chuẩn".</summary>
    public bool XacNhanSuaMonChuan { get; set; }

    public List<HangHoa> DsHangHoa { get; set; } = new();
    public List<NhomBuaAn> DsNhomBuaAn { get; set; } = new();
}
