using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Tạo 2 bảng của module "Công khai thực đơn" nếu CSDL chưa có (chạy lại vô hại, không đụng dữ liệu cũ).</summary>
public static class CongKhaiInitializer
{
    public static async Task DamBaoBangCongKhaiAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CongKhaiDbContext>();

        await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS cong_khai_tuan (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            tuan_bat_dau TEXT NOT NULL UNIQUE,
            phien_ban INTEGER NOT NULL DEFAULT 1,
            cong_khai_luc TEXT NOT NULL,
            nguoi_cong_khai TEXT NULL,
            dang_cong_khai INTEGER NOT NULL DEFAULT 1,
            ngay_nghi TEXT NULL)");

        await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS cong_khai_mon (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            cong_khai_tuan_id INTEGER NOT NULL REFERENCES cong_khai_tuan(id) ON DELETE CASCADE,
            ngay TEXT NOT NULL,
            khoi TEXT NOT NULL,
            thu_tu_bua INTEGER NOT NULL DEFAULT 0,
            ten_bua TEXT NOT NULL,
            ten_mon TEXT NOT NULL)");

        await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS cong_khai_phu (
            tuan_bat_dau TEXT NOT NULL,
            ngay INTEGER NOT NULL,
            phu_sang TEXT NULL,
            phu_chieu TEXT NULL,
            PRIMARY KEY (tuan_bat_dau, ngay))");

        await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_cong_khai_mon_tuan ON cong_khai_mon(cong_khai_tuan_id)");
    }
}
