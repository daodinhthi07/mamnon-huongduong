using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class QuyTacDongVm
{
    public int Id { get; set; }
    public string TenHang { get; set; } = "";
    public string? Dvt { get; set; }
    public string CoSo { get; set; } = "";
    public string LoaiNgay { get; set; } = "";
    public bool KhongDung { get; set; }
    public decimal SlAnSang { get; set; }
    public decimal SlNhaTre { get; set; }
    public decimal SlMauGiao { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; }
}

public sealed class QuyTacIndexVm
{
    public List<QuyTacDongVm> MoiNgay { get; set; } = new();
    public List<QuyTacDongVm> KhiCo { get; set; } = new();
}

public sealed class QuyTacFormVm
{
    public int Id { get; set; }
    public int HangHoaId { get; set; }
    /// <summary>Mã cơ sở được chọn (TC/PH/ĐT). Không chọn cơ sở nào = áp dụng cho mọi cơ sở.</summary>
    public List<string> CoSoChon { get; set; } = new();
    public int LoaiNgay { get; set; }
    public int CheDo { get; set; }
    public decimal SlAnSang { get; set; }
    public decimal SlNhaTre { get; set; }
    public decimal SlMauGiao { get; set; }
    public bool KhongDung { get; set; }
    public string? GhiChu { get; set; }
    public bool IsActive { get; set; } = true;

    public List<HangHoa> DsHangHoa { get; set; } = new();
    public List<CoSo> DsCoSo { get; set; } = new();
}
