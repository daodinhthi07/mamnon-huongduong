using Microsoft.AspNetCore.Identity;

namespace MamNon.ThucDon.Web.Identity;

/// <summary>
/// Tài khoản đăng nhập, mở rộng từ IdentityUser chuẩn của ASP.NET Core.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string HoTen { get; set; } = string.Empty;

    /// <summary>
    /// Cơ sở người dùng thuộc về (TC/PH/ĐT). NULL với Hiệu trưởng/Quản trị —
    /// những vai trò quản lý chung cả 3 cơ sở, không gắn cố định 1 nơi.
    /// </summary>
    public int? CoSoId { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Danh sách vai trò cố định, lấy đúng theo 4 chữ ký ở cuối mỗi bảng kê
/// chi tiền chợ trong file Excel gốc: Nhà bếp / Thủ kho / Kế toán / Hiệu trưởng.
/// </summary>
public static class VaiTro
{
    public const string NhaBep = "NhaBep";
    public const string ThuKho = "ThuKho";
    public const string KeToan = "KeToan";
    public const string HieuTruong = "HieuTruong";
    public const string QuanTri = "QuanTri";

    public static readonly IReadOnlyList<string> TatCa = new[]
    {
        NhaBep, ThuKho, KeToan, HieuTruong, QuanTri
    };
}
