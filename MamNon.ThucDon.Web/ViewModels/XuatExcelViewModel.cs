using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class XuatExcelViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public List<KhoiHoc> DsKhoi { get; set; } = new();

    public int? CoSoId { get; set; }
    public int? KhoiId { get; set; }
    public DateOnly Ngay { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}
