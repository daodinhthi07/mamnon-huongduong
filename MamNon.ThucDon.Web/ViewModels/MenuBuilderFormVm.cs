using System.ComponentModel.DataAnnotations;

namespace MamNon.ThucDon.Web.ViewModels;

/// <summary>
/// Form nhập liệu cho /menu-builder — gộp từ project MenuBuilderApi (Swagger)
/// riêng biệt trước đó vào thẳng project MVC chính, dùng chung 1 con người
/// đăng nhập/phân quyền thay vì phải mở 2 ứng dụng khác nhau.
/// </summary>
public sealed class MenuBuilderFormVm
{
    [Required(ErrorMessage = "Vui lòng chọn file Excel tuần")]
    public IFormFile? File { get; set; }

    /// <summary>Ngày Thứ 2 đầu tuần, YYYY-MM-DD. Để trống nếu không muốn đổi ngày tháng trong file.</summary>
    public string? StartDate { get; set; }

    public int PhSang { get; set; }
    public int PhNhaTre { get; set; }
    public int PhMauGiao { get; set; }

    public int DtSang { get; set; }
    public int DtNhaTre { get; set; }
    public int DtMauGiao { get; set; }

    /// <summary>TÙY CHỌN — JSON 6 bộ [sáng,nhà_trẻ,mẫu_giáo] cho PH theo thứ tự Thứ 2->Thứ 7. Có thì dùng thay PhSang/PhNhaTre/PhMauGiao.</summary>
    public string? PhCountsPerDayJson { get; set; }

    /// <summary>TÙY CHỌN — tương tự PhCountsPerDayJson nhưng cho ĐT.</summary>
    public string? DtCountsPerDayJson { get; set; }

    /// <summary>TÙY CHỌN — ghi đè số cháu TC (để trống cả 3 ô để giữ nguyên số cháu TC có sẵn trong file).</summary>
    public int? TcSang { get; set; }
    public int? TcNhaTre { get; set; }
    public int? TcMauGiao { get; set; }

    /// <summary>TÙY CHỌN — ghi đè số cháu TC khác nhau từng ngày, JSON 6 bộ giống PhCountsPerDayJson.</summary>
    public string? TcCountsPerDayJson { get; set; }
}
