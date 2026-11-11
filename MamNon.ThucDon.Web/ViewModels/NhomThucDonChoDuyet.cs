namespace MamNon.ThucDon.Web.ViewModels;

public sealed class NhomThucDonChoDuyet
{
    public int CoSoId { get; set; }
    public int KhoiId { get; set; }
    public int NgayHocId { get; set; }
    public string TenCoSo { get; set; } = string.Empty;
    public string TenKhoi { get; set; } = string.Empty;
    public DateOnly Ngay { get; set; }
    public string? NguoiLap { get; set; }
    public DateOnly? NgayLap { get; set; }
    public List<(string TenBua, string? TenMon)> CacBua { get; set; } = new();
}
