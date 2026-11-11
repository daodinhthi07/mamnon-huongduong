namespace MamNon.ThucDon.Web.ViewModels;

/// <summary>Payload JS gửi lên mỗi khi đổi món hoặc đổi sĩ số ở trang Chi tiết ngày.</summary>
public sealed class ChiTietNgayTinhToanRequest
{
    public int KhoiIdMauGiao { get; set; }
    public int KhoiIdNhaTre { get; set; }

    /// <summary>Cơ sở + ngày đang xem — để server biết ngày này đã có số SỬA/XÓA tay ở mục 3/4 chưa.</summary>
    public int CoSoId { get; set; }
    public DateOnly Ngay { get; set; }

    /// <summary>LoaiBuaAn.Id -> MonAn.Id đã chọn, cho từng khối. Bữa "SÁNG" dùng
    /// CHUNG cho cả 2 khối (lấy theo món của Mẫu giáo, 1 sĩ số ăn sáng duy nhất).</summary>
    public Dictionary<int, int?> MonMauGiao { get; set; } = new();
    public Dictionary<int, int?> MonNhaTre { get; set; } = new();

    public int SoSuatAnSang { get; set; }
    public int SoSuatMauGiao { get; set; }
    public int SoSuatNhaTre { get; set; }
}

/// <summary>1 dòng nguyên liệu "gia vị" — gộp từ cả 3 nguồn (sáng/nhà trẻ/mẫu giáo) theo đúng hàng hoá.</summary>
public sealed record NguyenLieuBaCotDto(
    int HangHoaId, string TenHang, string? Dvt,
    decimal SlAnSang, decimal SlNhaTre, decimal SlMauGiao, decimal DonGia,
    decimal TtAnSang, decimal TtNhaTre, decimal TtMauGiao);

/// <summary>1 dòng nguyên liệu mục "chính + xế" — gộp Nhà trẻ + Mẫu giáo theo đúng hàng hoá.</summary>
public sealed record NguyenLieuHaiCotDto(
    string TenHang, string? Dvt,
    decimal SlNhaTre, decimal SlMauGiao, decimal DonGia,
    decimal TtNhaTre, decimal TtMauGiao, int HangHoaId = 0, int ThuTuMon = int.MaxValue, string? TenMon = null);

/// <summary>Bản NHÁP (chưa bấm Lưu) của 1 (cơ sở, ngày) mà trình duyệt đang giữ — gửi kèm khi bấm
/// "Xuất Excel" để Excel ra đủ Thứ 2 → Thứ 7 mà không cần mở từng ngày bấm Lưu nháp. Trường nào
/// null thì lấy số đã lưu trong CSDL.</summary>
public sealed class DraftNgayCoSo
{
    public int CoSoId { get; set; }
    public DateOnly Ngay { get; set; }
    /// <summary>LoaiBuaAn.Id (dạng chuỗi) -> MonAn.Id đã chọn (null = để trống).</summary>
    public Dictionary<string, int?>? MonMauGiao { get; set; }
    public Dictionary<string, int?>? MonNhaTre { get; set; }
    public int? SoSuatAnSang { get; set; }
    public int? SoSuatMauGiao { get; set; }
    public int? SoSuatNhaTre { get; set; }
    public decimal? TienAnSang { get; set; }
    public decimal? TienMauGiao { get; set; }
    public decimal? TienNhaTre { get; set; }
}

/// <summary>Số liệu 3 mục (2 gia vị / 3 sáng / 4 chính+xế) đang hiện TRÊN TRANG của đúng 1 (cơ sở, ngày)
/// — gửi kèm khi bấm "Xuất Excel" để Excel ra y hệt màn hình, kể cả số vừa gõ chưa bấm Lưu. Mục nào
/// null thì Excel đọc từ cơ sở dữ liệu như bình thường.</summary>
public sealed record ExcelDuLieuTrenTrang(
    int CoSoId, DateOnly Ngay,
    List<NguyenLieuBaCotDto>? GiaVi, List<NguyenLieuMotCotDto>? Sang, List<NguyenLieuHaiCotDto>? Chinh);

/// <summary>1 dòng nguyên liệu mục "sáng" — có kèm đơn giá (khác NguyenLieuDto ở Soạn thực đơn).</summary>
public sealed record NguyenLieuMotCotDto(string TenHang, string? Dvt, decimal SoLuong, decimal DonGia, decimal ThanhTien, int HangHoaId = 0, int ThuTuMon = int.MaxValue, string? TenMon = null);

public sealed class ChiTietNgayTinhToanResponse
{
    public List<NguyenLieuMotCotDto> Sang { get; set; } = new();
    public List<NguyenLieuHaiCotDto> Chinh { get; set; } = new();

    public decimal TongTienSang { get; set; }
    public decimal TongTienChinhNhaTre { get; set; }
    public decimal TongTienChinhMauGiao { get; set; }

    /// <summary>True nếu ngày này ĐÃ có số lưu cố định (phiếu xuất kho) cho mục 3 / mục 4 —
    /// khi đó bảng hiện số đã lưu thay vì số ước lượng theo thực đơn.</summary>
    public bool CoPhieuSang { get; set; }
    public bool CoPhieuChinhXe { get; set; }

    /// <summary>Chỉ dùng khi xuất Excel: lý do KHÔNG cân đối được "Chênh lệch cuối ngày" về 0
    /// (VD lệch quá lớn do thiếu món) — hiện ở cạnh dòng Chênh lệch để người dùng kiểm tra thực đơn.</summary>
    public string? GhiChuCanDoi { get; set; }

    /// <summary>Khác null khi số lượng mục 3/4 được lấy theo ngày trong file Excel mẫu (thực đơn trùng ngày mẫu).</summary>
    public string? GhiChuMau { get; set; }

    /// <summary>Cảnh báo khi số đã lưu cố định (mục 4) có vẻ cũ so với thực đơn hiện tại (thiếu/0 so với ước lượng).</summary>
    public string? GhiChuLech { get; set; }
}

/// <summary>
/// Mục "2. Theo dõi xuất kho gia vị hằng ngày" — LUÔN liệt kê đủ danh sách hàng hoá đã
/// cấu hình ở /kho/dinh-muc-gia-vi (kể cả dòng = 0), để ngày nào cũng hiện CỐ ĐỊNH cùng 1
/// danh sách, không bị thiếu dòng. Ưu tiên phiếu xuất kho thật nếu ngày đó đã lập (ở
/// /kho/xuat-kho HOẶC lưu nhanh ngay tại trang Chi tiết ngày — xem
/// ChiTietNgayController.LuuGiaVi), chưa lập thì tự tính từ định mức cố định. Xem lý do
/// đầy đủ trong <see cref="MamNon.ThucDon.Web.Services.ChiTietNgayTinhToanService.LayGiaViThatAsync"/>.
/// Cột "Ăn sáng" đã GỘP SẴN 2 nhóm chi phí "Ăn sáng" (Mẫu giáo) + "Ăn sáng - Nhà trẻ"
/// làm 1, đúng quy ước hiển thị 3 cột đang dùng ở cả trang web lẫn Excel.
/// </summary>
public sealed class GiaViThatDto
{
    public List<NguyenLieuBaCotDto> DanhSach { get; set; } = new();

    /// <summary>True nếu ĐÃ có phiếu xuất kho "Gia vị hằng ngày" cho (cơ sở, ngày) này.</summary>
    public bool CoPhieu { get; set; }

    public decimal TongAnSang => DanhSach.Sum(x => x.TtAnSang);
    public decimal TongNhaTre => DanhSach.Sum(x => x.TtNhaTre);
    public decimal TongMauGiao => DanhSach.Sum(x => x.TtMauGiao);
}

/// <summary>
/// Mục "3. Chi tiền thực phẩm sáng" + "4. Chi tiền thực phẩm ăn chính + xế" — LẤY TỪ
/// PHIẾU XUẤT KHO THẬT (loại "Thực phẩm sáng" / "Thực phẩm chính + xế", nhập tay ở
/// /kho/xuat-kho), KHÔNG tính từ công thức món ăn — cùng nguyên tắc với
/// <see cref="GiaViThatDto"/> ở mục 2, và đúng như trang "/cho/chi-tiet" đang làm.
/// </summary>
public sealed class ThucPhamThatDto
{
    public List<NguyenLieuMotCotDto> Sang { get; set; } = new();
    public List<NguyenLieuHaiCotDto> Chinh { get; set; } = new();

    /// <summary>True nếu ĐÃ có phiếu xuất kho tương ứng cho (cơ sở, ngày) này.</summary>
    public bool CoPhieuSang { get; set; }
    public bool CoPhieuChinhXe { get; set; }

    public decimal TongTienSang => Sang.Sum(x => x.ThanhTien);
    public decimal TongTienChinhNhaTre => Chinh.Sum(x => x.TtNhaTre);
    public decimal TongTienChinhMauGiao => Chinh.Sum(x => x.TtMauGiao);
}
