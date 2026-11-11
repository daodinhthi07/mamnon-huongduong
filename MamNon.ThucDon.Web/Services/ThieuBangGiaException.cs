namespace MamNon.ThucDon.Web.Services;

/// <summary>Xuất Excel bị chặn vì có nguyên liệu không có trong Bảng giá (hoặc chưa có đơn giá).
/// <see cref="Exception.Message"/> đã là câu thông báo cụ thể, hiển thị thẳng cho người dùng.</summary>
public sealed class ThieuBangGiaException : Exception
{
    public IReadOnlyList<string> Dong { get; }

    public ThieuBangGiaException(IReadOnlyList<string> dong)
        : base("Không xuất được Excel vì có nguyên liệu chưa có trong Bảng giá (hoặc chưa có đơn giá):\n"
               + string.Join("\n", dong.Select(d => "• " + d))
               + "\nVui lòng vào menu Bảng giá để thêm / cập nhật giá cho các mặt hàng trên, rồi xuất lại.")
    {
        Dong = dong;
    }
}
