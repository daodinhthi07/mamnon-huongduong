using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Bảng giá có thêm cột Ghi chú -> cần cột hang_hoa.ghi_chu. Schema nghiệp vụ không dùng EF Migrations nên
/// tự thêm cột lúc khởi động nếu chưa có (chạy lại vô hại).</summary>
public static class HangHoaGhiChuInitializer
{
    public static async Task DamBaoCotGhiChuHangHoaAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThucDonDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            await using (var kiemTra = conn.CreateCommand())
            {
                kiemTra.CommandText = "SELECT COUNT(*) FROM pragma_table_info('hang_hoa') WHERE name = 'ghi_chu'";
                if (Convert.ToInt32(await kiemTra.ExecuteScalarAsync()) > 0) return;
            }
            await using var them = conn.CreateCommand();
            them.CommandText = "ALTER TABLE hang_hoa ADD COLUMN ghi_chu TEXT NULL";
            await them.ExecuteNonQueryAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
