using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class DongNhapVm
{
    public int? HangHoaId { get; set; }
    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }
}

public sealed class NhapKhoViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public List<HangHoa> DsHangHoa { get; set; } = new();

    public int? CoSoId { get; set; }
    public DateOnly NgayNhap { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? NguoiLap { get; set; }
    public List<DongNhapVm> CacDong { get; set; } = new() { new DongNhapVm() };
}

public sealed class DongXuatVm
{
    public int? HangHoaId { get; set; }
    public int? NhomChiPhiId { get; set; }
    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }
}

public sealed class XuatKhoViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public List<HangHoa> DsHangHoa { get; set; } = new();
    public List<NhomChiPhi> DsNhomChiPhi { get; set; } = new();

    public int? CoSoId { get; set; }
    public DateOnly NgayXuat { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public LoaiXuatKho LoaiXuat { get; set; } = LoaiXuatKho.ThucPhamChinhXe;
    public string? NguoiLap { get; set; }
    public List<DongXuatVm> CacDong { get; set; } = new() { new DongXuatVm() };
}

/// <summary>1 dòng định mức gia vị cố định của 1 hàng hoá — 4 cột định lượng/suất cố
/// định theo 4 nhóm chi phí (thay vì Dictionary, để binding form POST đơn giản, vì luôn
/// có ĐÚNG 4 nhóm này, không đổi).</summary>
public sealed class DongDinhMucGiaViVm
{
    public int HangHoaId { get; set; }
    public string TenHang { get; set; } = string.Empty;
    public string? Dvt { get; set; }
    public decimal AnSang { get; set; }
    public decimal AnSangNhaTre { get; set; }
    public decimal NhaTre { get; set; }
    public decimal MauGiao { get; set; }
}

public sealed class DinhMucGiaViViewModel
{
    public List<DongDinhMucGiaViVm> CacDong { get; set; } = new();
}
