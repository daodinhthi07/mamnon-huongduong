using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class ChiTietNgayBuaVm
{
    public int BuaId { get; set; }
    public string TenBua { get; set; } = string.Empty;
    public int? NhomBuaAnId { get; set; }
    public int? MonAnId { get; set; }
}

/// <summary>
/// Trang xem/sửa "Bảng kê thực đơn - Chi tiền chợ" cho 1 (Cơ sở, Ngày) — chọn
/// NGÀY BẤT KỲ (không giới hạn theo tuần cố định, dùng được mãi qua các tháng
/// sau). Món ăn ở mục "1. Thực đơn" chọn được trực tiếp bằng dropdown (dùng
/// đúng danh mục món + công thức thật trong database); mỗi khi đổi món hoặc
/// đổi sĩ số, nguyên liệu ở các mục 2/3/4 tính lại NGAY qua AJAX, không phải
/// số liệu tĩnh chép từ Excel.
/// </summary>
public sealed class ChiTietNgayViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public int? CoSoId { get; set; }
    public string? TenCoSo { get; set; }
    public DateOnly Ngay { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public List<MonAn> DsMonAn { get; set; } = new();

    public int KhoiIdMauGiao { get; set; }
    public int KhoiIdNhaTre { get; set; }

    public List<ChiTietNgayBuaVm> BuaMauGiao { get; set; } = new();
    public List<ChiTietNgayBuaVm> BuaNhaTre { get; set; } = new();

    public int SoSuatAnSang { get; set; }
    public int SoSuatMauGiao { get; set; }
    public int SoSuatNhaTre { get; set; }

    public decimal TienAnSang { get; set; } = 10000;
    public decimal TienMauGiao { get; set; } = 29000;
    public decimal TienNhaTre { get; set; } = 29000;

    public bool ThuBayNghi { get; set; } = false;

    // ---- Mục "2. Theo dõi xuất kho gia vị hằng ngày" — LẤY TỪ PHIẾU XUẤT KHO THẬT,
    // KHÔNG tính tự động từ công thức món (xem lý do trong ChiTietNgayTinhToanService).
    public List<NguyenLieuBaCotDto> GiaViThat { get; set; } = new();
    public bool CoPhieuGiaVi { get; set; }
    public decimal TongTienGiaViAnSang => GiaViThat.Sum(x => x.TtAnSang);
    public decimal TongTienGiaViNhaTre => GiaViThat.Sum(x => x.TtNhaTre);
    public decimal TongTienGiaViMauGiao => GiaViThat.Sum(x => x.TtMauGiao);
}
