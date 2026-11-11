using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

/// <summary>1 dòng thực đơn (Bữa -> Món) hiển thị ở đầu trang chi tiết chợ.</summary>
public sealed class ChiTietChoBuaMonVm
{
    public string TenBua { get; set; } = string.Empty;
    public string? TenMon { get; set; }
}

/// <summary>
/// 1 dòng hàng hóa ở mục "2. Xuất kho gia vị hằng ngày" — pivot theo 3 nhóm chi phí
/// (Ăn sáng / Nhà trẻ / Mẫu giáo), đúng 3 cột số lượng + 3 cột thành tiền như file Excel gốc.
/// </summary>
public sealed class ChiTietChoGiaViVm
{
    public string TenHang { get; set; } = string.Empty;
    public string? Dvt { get; set; }
    public string? MaHang { get; set; }
    public decimal DonGia { get; set; }

    public decimal SlAnSang { get; set; }
    public decimal SlNhaTre { get; set; }
    public decimal SlMauGiao { get; set; }

    public decimal TtAnSang { get; set; }
    public decimal TtNhaTre { get; set; }
    public decimal TtMauGiao { get; set; }
}

/// <summary>1 dòng hàng hóa ở mục "3. Chi tiền thực phẩm buổi sáng" — không tách nhóm.</summary>
public sealed class ChiTietChoThucPhamSangVm
{
    public string TenHang { get; set; } = string.Empty;
    public string? Dvt { get; set; }
    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }
    public decimal ThanhTien { get; set; }
}

/// <summary>
/// 1 dòng hàng hóa ở mục "4. Chi tiền thực phẩm ăn chính + xế" — pivot theo 2 nhóm
/// (Nhà trẻ / Mẫu giáo), đúng cấu trúc file Excel gốc.
/// </summary>
public sealed class ChiTietChoChinhXeVm
{
    public string TenHang { get; set; } = string.Empty;
    public string? Dvt { get; set; }
    public string? MaHang { get; set; }
    public decimal DonGia { get; set; }

    public decimal SlNhaTre { get; set; }
    public decimal SlMauGiao { get; set; }
    public decimal TtNhaTre { get; set; }
    public decimal TtMauGiao { get; set; }

    public decimal TongSl => SlNhaTre + SlMauGiao;
    public decimal TongTt => TtNhaTre + TtMauGiao;
}

/// <summary>1 dòng đối chiếu ngân sách theo nhóm chi phí (Ăn sáng / Nhà trẻ / Mẫu giáo).</summary>
public sealed class ChiTietChoDinhMucVm
{
    public string TenNhom { get; set; } = string.Empty;
    public decimal TienAn1Chau { get; set; }
    public int SoSuatAn { get; set; }
    public decimal DuocPhepChi { get; set; }
    public decimal ThucChi { get; set; }
    public decimal ChenhLech => DuocPhepChi - ThucChi;
}

/// <summary>
/// Dữ liệu cho trang "Chi tiết chợ" (/cho/chi-tiet) — bản dựng lại đúng bố cục 4 mục của
/// bảng kê "Chợ TC/PH/ĐT" trong file Excel gốc, nhưng lấy từ dữ liệu THẬT trong CSDL:
/// thực đơn (bảng thuc_don), định mức ăn ngày (dinh_muc_an_ngay) và phiếu xuất kho
/// (phieu_xuat_kho/chi_tiet_xuat_kho, đã tách sẵn theo 3 loại xuất kho tương ứng 3 mục
/// 2/3/4) — KHÔNG suy luận ngược từ công thức món ăn, vì đó là đúng nguồn dữ liệu mà
/// trang Xuất kho (/kho/xuat-kho) đã ghi nhận hằng ngày.
/// </summary>
public sealed class ChiTietChoViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public int? CoSoId { get; set; }
    public string? TenCoSo { get; set; }

    public DateOnly Ngay { get; set; }

    private static readonly string[] TenCacThu = { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" };

    /// <summary>Tên thứ trong tuần của <see cref="Ngay"/> (vd. "Thứ hai") — dùng chung
    /// cho cả trang đầy đủ lẫn panel nhúng trong Dashboard, tránh tính trùng 2 nơi.</summary>
    public string TenThu => TenCacThu[(int)Ngay.DayOfWeek];

    /// <summary>False nếu ngày này chưa có bản ghi ngày học (ngay_hoc) cho cơ sở đang chọn.</summary>
    public bool CoNgayHoc { get; set; }

    public List<ChiTietChoBuaMonVm> ThucDonMauGiao { get; set; } = new();
    public List<ChiTietChoBuaMonVm> ThucDonNhaTre { get; set; } = new();

    public List<ChiTietChoDinhMucVm> DinhMuc { get; set; } = new();
    public decimal TongDuocPhepChi => DinhMuc.Sum(d => d.DuocPhepChi);
    public decimal TongThucChi => DinhMuc.Sum(d => d.ThucChi);
    public decimal TongChenhLech => DinhMuc.Sum(d => d.ChenhLech);

    public List<ChiTietChoGiaViVm> GiaVi { get; set; } = new();
    public List<ChiTietChoThucPhamSangVm> ThucPhamSang { get; set; } = new();
    public List<ChiTietChoChinhXeVm> ThucPhamChinhXe { get; set; } = new();

    /// <summary>True nếu mục 2 đang hiện số TỰ TÍNH từ định mức cố định
    /// (dinh_muc_gia_vi_chuan) vì ngày này CHƯA lập phiếu xuất kho "Gia vị hằng ngày"
    /// thật — xem ChiTietNgayTinhToanService.LayGiaViThatAsync.</summary>
    public bool GiaViTuDinhMuc { get; set; }

    // ---- Số suất ăn — "gốc" là số đã lưu trong dinh_muc_an_ngay; "hiện tại" là số
    // đang xem (có thể do người dùng tự nhập để xem thử kịch bản khác, xem
    // ChoController.TaoViewModelAsync). Số lượng ở GiaVi/ThucPhamSang/ThucPhamChinhXe
    // ở trên đã được co giãn theo đúng tỉ lệ Hiện tại/Gốc của từng nhóm khi 2 số này
    // khác nhau — KHÔNG phải số liệu gốc trong phiếu xuất kho nữa.
    public int AnSangGoc { get; set; }
    public int NhaTreGoc { get; set; }
    public int MauGiaoGoc { get; set; }
    public int AnSangHienTai { get; set; }
    public int NhaTreHienTai { get; set; }
    public int MauGiaoHienTai { get; set; }

    /// <summary>True nếu người dùng đã đổi số suất khác với số đã lưu trong CSDL.</summary>
    public bool DaTuyChinhSuat =>
        AnSangHienTai != AnSangGoc || NhaTreHienTai != NhaTreGoc || MauGiaoHienTai != MauGiaoGoc;

    public string? NguoiLap { get; set; }
    public string? ThuKho { get; set; }
    public string? KeToan { get; set; }
    public string? HieuTruong { get; set; }
}
