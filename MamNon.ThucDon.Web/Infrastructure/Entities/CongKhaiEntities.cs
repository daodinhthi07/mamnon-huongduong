namespace MamNon.ThucDon.Web.Infrastructure.Entities;

/// <summary>Một tuần thực đơn đã CÔNG KHAI cho phụ huynh (bản chụp — phụ huynh chỉ thấy bản này, không đọc thẳng bảng thực đơn nội bộ).
/// Công khai lại cùng tuần = thay bản chụp mới và tăng <see cref="PhienBan"/>.</summary>
public sealed class CongKhaiTuan
{
    public int Id { get; set; }
    /// <summary>Thứ 2 đầu tuần.</summary>
    public DateOnly TuanBatDau { get; set; }
    public int PhienBan { get; set; } = 1;
    public DateTime CongKhaiLuc { get; set; } = DateTime.UtcNow;
    public string? NguoiCongKhai { get; set; }
    public bool DangCongKhai { get; set; } = true;
    /// <summary>Các ngày nghỉ trong tuần, dạng "2026-11-14,..." (rỗng = không có).</summary>
    public string? NgayNghi { get; set; }

    public List<CongKhaiMon> Mons { get; set; } = new();
    /// <summary>Bản chụp "Chi tiết nguyên liệu &amp; giá" từng ngày (tab "Chi tiết" của trang phụ huynh).</summary>
    public List<CongKhaiChiTiet> ChiTiets { get; set; } = new();
}

/// <summary>Bản chụp chi tiết nguyên liệu + đơn giá + thành tiền của MỘT ngày (JSON, cùng dạng /chi-tiet-ngay/tong-hop), chụp lúc bấm "Công khai".</summary>
public sealed class CongKhaiChiTiet
{
    public int Id { get; set; }
    public int CongKhaiTuanId { get; set; }
    public DateOnly Ngay { get; set; }
    public string DuLieu { get; set; } = string.Empty;

    public CongKhaiTuan CongKhaiTuan { get; set; } = null!;
}

/// <summary>Một món trong bản chụp công khai: (ngày, khối, bữa, tên món). KHÔNG có định lượng / nguyên liệu / tiền.</summary>
public sealed class CongKhaiMon
{
    public int Id { get; set; }
    public int CongKhaiTuanId { get; set; }
    public DateOnly Ngay { get; set; }
    /// <summary>"Nhà trẻ" hoặc "Mẫu giáo".</summary>
    public string Khoi { get; set; } = string.Empty;
    public int ThuTuBua { get; set; }
    public string TenBua { get; set; } = string.Empty;
    public string TenMon { get; set; } = string.Empty;

    public CongKhaiTuan CongKhaiTuan { get; set; } = null!;
}
