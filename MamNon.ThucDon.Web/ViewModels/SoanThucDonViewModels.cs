using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

/// <summary>Dữ liệu hiển thị ban đầu của trang Soạn thực đơn (danh mục dropdown + lựa chọn hiện có nếu có).</summary>
public sealed class SoanThucDonViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public List<KhoiHoc> DsKhoi { get; set; } = new();
    public List<NhomChiPhi> DsNhomChiPhi { get; set; } = new();
    public List<MonAn> DsMonAn { get; set; } = new();
    public List<LoaiBuaAn> DsBuaTheoKhoi { get; set; } = new();

    public int? CoSoId { get; set; }
    public int? KhoiId { get; set; }
    public int? NhomChiPhiId { get; set; }
    public DateOnly Ngay { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int SoSuatAn { get; set; }
    public decimal TienAn1Chau { get; set; }

    /// <summary>LoaiBuaAn.Id -> MonAn.Id đã chọn (nếu đã có dữ liệu lưu trước đó cho ngày này).</summary>
    public Dictionary<int, int?> MonDaChonTheoBua { get; set; } = new();
}

/// <summary>Payload JS gửi lên mỗi khi người dùng đổi lựa chọn — dùng để tính lại trực quan qua AJAX.</summary>
public sealed class TinhToanRequest
{
    public int? KhoiId { get; set; }
    public int? NhomChiPhiId { get; set; }
    public DateOnly Ngay { get; set; }
    public int SoSuatAn { get; set; }
    public decimal TienAn1Chau { get; set; }
    public Dictionary<int, int?> MonDaChonTheoBua { get; set; } = new();
}

public sealed record NguyenLieuDto(string TenHang, string? Dvt, decimal SoLuong, decimal ThanhTien);

public sealed record HangMucDto(string TenHangMuc, decimal ChiPhiThucTe, decimal? DinhMuc, decimal? ChenhLech, bool? VuotDinhMuc);

public sealed record NganSachDto(decimal SoTienDuocPhepChi, decimal TongChiThucTe, decimal ChenhLech);

public sealed class TinhToanResponse
{
    public List<NguyenLieuDto> NguyenLieu { get; set; } = new();
    public decimal TongTienNguyenLieu { get; set; }
    public List<HangMucDto> HangMuc { get; set; } = new();
    public NganSachDto? NganSach { get; set; }
}

/// <summary>Form gửi lên khi bấm "Lưu nháp" / "Gửi duyệt" — JS gom trạng thái hiện tại rồi submit form thường (không AJAX), để tận dụng redirect + TempData thông báo chuẩn MVC.</summary>
public sealed class LuuThucDonFormModel
{
    public int CoSoId { get; set; }
    public int KhoiId { get; set; }
    public int? NhomChiPhiId { get; set; }
    public DateOnly Ngay { get; set; }
    public int SoSuatAn { get; set; }
    public decimal TienAn1Chau { get; set; }

    /// <summary>Chuỗi dạng "buaId1:monId1,buaId2:monId2,..." — form thường không gửi được Dictionary trực tiếp.</summary>
    public string MonDaChonJson { get; set; } = "{}";

    public bool GuiDuyet { get; set; }
}
