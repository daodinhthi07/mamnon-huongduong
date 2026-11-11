using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class DashboardBuaRowVm
{
    public int BuaId { get; set; }
    public string TenBua { get; set; } = string.Empty;

    /// <summary>Nhóm bữa của dòng này — dùng để lọc dropdown chỉ hiện món cùng nhóm.</summary>
    public int? NhomBuaAnId { get; set; }

    /// <summary>6 ô Thứ 2 -> Thứ 7, mỗi ô là MonAn.Id đã chọn (null nếu chưa chọn).</summary>
    public int?[] MonTheoNgay { get; set; } = new int?[6];
}

/// <summary>1 lưới thực đơn của 1 khối (Mẫu giáo hoặc Nhà trẻ) — Dashboard hiển thị cả 2 lưới cùng lúc.</summary>
public sealed class DashboardKhoiGridVm
{
    public int KhoiId { get; set; }
    public string TenKhoi { get; set; } = string.Empty;
    public List<DashboardBuaRowVm> CacBua { get; set; } = new();

    /// <summary>Theme màu ở View: Mẫu giáo = xanh lá, còn lại (Nhà trẻ) = hồng.</summary>
    public string ThemeClass => TenKhoi.Contains("Mẫu giáo") ? "dashboard-mau-giao" : "dashboard-nha-tre";
}

/// <summary>
/// Dashboard KHÔNG cho chọn Cơ sở/Khối nữa — thực đơn dùng chung cho cả 3 cơ sở
/// (đã xác nhận bằng dữ liệu thật: cùng ngày, cùng khối thì món ăn giống hệt nhau
/// giữa TC/PH/ĐT, chỉ số cháu khác nhau) — nên chỉ cần chọn TUẦN, và hiển thị lưới
/// cho MỌI khối cùng lúc thay vì chọn từng khối 1.
/// </summary>
public sealed class DashboardViewModel
{
    public List<MonAn> DsMonAn { get; set; } = new();

    /// <summary>Các món đang được chọn trong lưới của tuần (kể cả món đã ẩn / khác nhóm bữa) — để ô chọn luôn hiện đúng món đã lưu, không bị bỏ trống rồi lưu đè thành rỗng.</summary>
    public List<MonAn> MonDangDung { get; set; } = new();

    /// <summary>Thứ 2 đầu tuần đang xem.</summary>
    public DateOnly TuanBatDau { get; set; }

    public List<DashboardKhoiGridVm> CacKhoi { get; set; } = new();

    /// <summary>Thứ 7 của tuần đang xem là ngày nghỉ (trạng thái khác "Học bình thường").</summary>
    public bool ThuBayNghi { get; set; }

    /// <summary>Phụ sáng / Phụ chiều từng ngày (Thứ 2 → Thứ 7) — lưu riêng ở bảng cong_khai_phu (không nằm trong lưới món), dùng chung 2 khối.
    /// <c>PhuDaLuu[i]</c> = ngày đó đã có bản ghi (khi chưa có thì phụ chiều lấy mặc định như trang phụ huynh).</summary>
    public string[] PhuSang { get; set; } = new string[6] { "", "", "", "", "", "" };
    public string[] PhuChieu { get; set; } = new string[6] { "", "", "", "", "", "" };
    public bool[] PhuDaLuu { get; set; } = new bool[6];
    public string PhuChieuMacDinh { get; set; } = "";

    public static readonly string[] TenThuThuTu = { "THỨ 2", "THỨ 3", "THỨ 4", "THỨ 5", "THỨ 6", "THỨ 7" };
}
