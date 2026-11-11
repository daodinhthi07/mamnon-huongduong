using MamNon.ThucDon.Web.Infrastructure.Entities;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Tính chi phí thực tế/suất của từng hạng mục (Canh/Mặn/Xào/Tráng miệng/
/// Xế/Sữa/Gạo/Gia vị) từ các dòng ThucDon (đã Include LoaiBuaAn.HangMuc,
/// MonAn.DinhLuongNguyenLieus.HangHoa) trong 1 ngày của 1 khối, rồi so với
/// định mức — cho phép món ăn linh hoạt mỗi tuần nhưng vẫn kiểm soát được
/// tỷ lệ chi theo từng hạng mục.
/// </summary>
public sealed class HangMucNganSachCalculator
{
    /// <param name="thucDonTrongNgayCuaKhoi">Các dòng ThucDon cùng 1 khối, cùng 1 ngày.</param>
    /// <param name="dinhMucTrenSuatTheoHangMucId">Định mức đồng/suất, khóa theo HangMucNganSach.Id.</param>
    public IReadOnlyList<KetQuaHangMuc> TinhChiPhiTheoHangMuc(
        IEnumerable<ThucDonEntity> thucDonTrongNgayCuaKhoi,
        IReadOnlyDictionary<int, decimal> dinhMucTrenSuatTheoHangMucId)
    {
        // Chỉ xét các bữa có gắn hạng mục ngân sách (bỏ qua bữa SÁNG — thuộc nhóm chi phí "Ăn sáng" riêng)
        var buaCoHangMuc = thucDonTrongNgayCuaKhoi.Where(td => td.LoaiBuaAn.HangMucId is not null);

        var ketQua = new List<KetQuaHangMuc>();

        foreach (var nhom in buaCoHangMuc.GroupBy(td => td.LoaiBuaAn.HangMucId!.Value))
        {
            var hangMucId = nhom.Key;
            var hangMuc = nhom.First().LoaiBuaAn.HangMuc
                ?? throw new InvalidOperationException(
                    $"LoaiBuaAn.HangMuc chưa được Include cho HangMucId={hangMucId}.");

            var chiPhiTrenSuat = nhom
                .Where(td => td.MonAn is not null)
                .SelectMany(td => td.MonAn!.DinhLuongNguyenLieus)
                .Sum(dl => dl.DinhLuong * (dl.HangHoa.DonGiaGanNhat ?? 0m));

            var coDinhMuc = dinhMucTrenSuatTheoHangMucId.TryGetValue(hangMucId, out var dinhMuc);

            ketQua.Add(new KetQuaHangMuc(hangMuc, chiPhiTrenSuat, coDinhMuc ? dinhMuc : null));
        }

        return ketQua.OrderBy(k => k.HangMuc.ThuTu).ToList();
    }
}
