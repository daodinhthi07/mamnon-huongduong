using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Tái hiện khối tính toán ở đầu mỗi bảng "Chợ ..." trong file Excel gốc:
///   Số tiền được phép chi chợ = Số tiền ăn trên 1 cháu x Xuất ăn trong ngày
///   Chênh lệch cuối ngày = Số tiền được phép chi chợ - Tổng chi tiền chợ trong ngày
/// "TongChiThucTe" nên lấy từ kết quả cộng dồn tự động (VD tổng ThanhTien
/// của ChiTietXuatKho, hoặc từ NguyenLieuCalculator), không gõ tay như file gốc.
/// </summary>
public sealed class NganSachNgayCalculator
{
    public KetQuaNganSachNgay TinhNganSach(DinhMucAnNgay dinhMuc, decimal tongChiThucTe)
        => new(dinhMuc.NhomChiPhi, dinhMuc.TienDuocPhepChi, tongChiThucTe);

    public IReadOnlyList<KetQuaNganSachNgay> TinhNganSachNhieuNhom(
        IEnumerable<DinhMucAnNgay> dinhMucCacNhom,
        IReadOnlyDictionary<int, decimal> tongChiThucTeTheoNhomChiPhiId)
    {
        return dinhMucCacNhom
            .Select(dm => TinhNganSach(
                dm,
                tongChiThucTeTheoNhomChiPhiId.TryGetValue(dm.NhomChiPhiId, out var tc) ? tc : 0m))
            .ToList();
    }
}
