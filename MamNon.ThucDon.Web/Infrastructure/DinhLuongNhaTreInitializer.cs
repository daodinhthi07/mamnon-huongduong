using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Công thức món có định lượng RIÊNG cho Nhà trẻ -> cần cột dinh_luong_nha_tre (NULL = dùng chung định lượng Mẫu giáo)
/// ở CẢ HAI bảng: dinh_luong_nguyen_lieu (chuẩn chợ TC) và dinh_luong_nguyen_lieu_theo_co_so (PH / ĐT).
/// Tự thêm cột lúc khởi động nếu chưa có (chạy lại vô hại). Phải gọi SAU DamBaoBangQuyTacNguyenLieuAsync (nơi tạo bảng theo chợ).</summary>
public static class DinhLuongNhaTreInitializer
{
    public static async Task DamBaoCotDinhLuongNhaTreAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThucDonDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            foreach (var bang in new[] { "dinh_luong_nguyen_lieu", "dinh_luong_nguyen_lieu_theo_co_so" })
            {
                await using (var kiemTra = conn.CreateCommand())
                {
                    kiemTra.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{bang}') WHERE name = 'dinh_luong_nha_tre'";
                    if (Convert.ToInt32(await kiemTra.ExecuteScalarAsync()) > 0) continue;
                }
                await using var them = conn.CreateCommand();
                them.CommandText = $"ALTER TABLE {bang} ADD COLUMN dinh_luong_nha_tre NUMERIC NULL";
                await them.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
