using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.Services;

/// <summary>ThuTuMon = thứ tự (LoaiBuaAn.ThuTu) của món ĐẦU TIÊN trong thực đơn có dùng nguyên liệu này; TenMon = tên
/// các món dùng nguyên liệu này (theo thứ tự thực đơn) — để bảng kê xếp theo thứ tự món cho bếp dễ nắm bắt.</summary>
public sealed record KetQuaNguyenLieu(HangHoa HangHoa, decimal TongCanDung, decimal ThanhTien, int ThuTuMon = int.MaxValue, string? TenMon = null);

public sealed record KetQuaHangMuc(HangMucNganSach HangMuc, decimal ChiPhiThucTeTrenSuat, decimal? DinhMucTrenSuat)
{
    public decimal? ChenhLechTrenSuat => DinhMucTrenSuat is null ? null : ChiPhiThucTeTrenSuat - DinhMucTrenSuat;
    public bool? VuotDinhMuc => ChenhLechTrenSuat is null ? null : ChenhLechTrenSuat > 0;
}

public sealed record KetQuaNganSachNgay(NhomChiPhi Nhom, decimal SoTienDuocPhepChi, decimal TongChiThucTe)
{
    public decimal ChenhLech => SoTienDuocPhepChi - TongChiThucTe;
}
