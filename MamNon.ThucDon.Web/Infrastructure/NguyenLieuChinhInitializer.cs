using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Công thức món có cờ "nguyên liệu chính" -> cần cột dinh_luong_nguyen_lieu.la_nguyen_lieu_chinh (0/1, mặc định 0).
/// Tự thêm cột lúc khởi động nếu chưa có (chạy lại vô hại).</summary>
public static class NguyenLieuChinhInitializer
{
    public static async Task DamBaoCotNguyenLieuChinhAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThucDonDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            await using (var kiemTra = conn.CreateCommand())
            {
                kiemTra.CommandText = "SELECT COUNT(*) FROM pragma_table_info('dinh_luong_nguyen_lieu') WHERE name = 'la_nguyen_lieu_chinh'";
                if (Convert.ToInt32(await kiemTra.ExecuteScalarAsync()) > 0) return;
            }
            await using var them = conn.CreateCommand();
            them.CommandText = "ALTER TABLE dinh_luong_nguyen_lieu ADD COLUMN la_nguyen_lieu_chinh INTEGER NOT NULL DEFAULT 0";
            await them.ExecuteNonQueryAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
