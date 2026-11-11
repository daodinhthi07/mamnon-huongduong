using System.Data;
using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Services;

/// <summary>Ghi chú của bảng giá (VD "Tăng 10.000đ so với năm trước"). Lưu trong bảng riêng bang_gia_ghi_chu (tự tạo khi dùng lần đầu)
/// nên KHÔNG phải sửa entity HangHoa, DbContext hay Program.cs — chức năng bảng giá tự chứa trong các file BangGia*.</summary>
public static class BangGiaGhiChu
{
    private static volatile bool _daTao;

    public static async Task DamBaoBangAsync(ThucDonDbContext db)
    {
        if (_daTao) return;
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS bang_gia_ghi_chu (hang_hoa_id INTEGER NOT NULL PRIMARY KEY, ghi_chu TEXT NOT NULL)");
        _daTao = true;
    }

    /// <summary>Toàn bộ ghi chú: hang_hoa_id -> nội dung.</summary>
    public static async Task<Dictionary<int, string>> LayTatCaAsync(ThucDonDbContext db)
    {
        await DamBaoBangAsync(db);
        var kq = new Dictionary<int, string>();
        var conn = db.Database.GetDbConnection();
        var daMo = conn.State == ConnectionState.Open;
        if (!daMo) await db.Database.OpenConnectionAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT hang_hoa_id, ghi_chu FROM bang_gia_ghi_chu";
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) kq[Convert.ToInt32(rd.GetValue(0))] = rd.GetString(1);
        }
        finally
        {
            if (!daMo) await db.Database.CloseConnectionAsync();
        }
        return kq;
    }

    /// <summary>Đặt ghi chú cho 1 mặt hàng; rỗng/null thì xóa ghi chú.</summary>
    public static async Task DatAsync(ThucDonDbContext db, int hangHoaId, string? ghiChu)
    {
        await DamBaoBangAsync(db);
        if (string.IsNullOrWhiteSpace(ghiChu))
            await db.Database.ExecuteSqlRawAsync("DELETE FROM bang_gia_ghi_chu WHERE hang_hoa_id = {0}", hangHoaId);
        else
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO bang_gia_ghi_chu (hang_hoa_id, ghi_chu) VALUES ({0}, {1}) " +
                "ON CONFLICT(hang_hoa_id) DO UPDATE SET ghi_chu = excluded.ghi_chu", hangHoaId, ghiChu.Trim());
    }

    public static async Task XoaTatCaAsync(ThucDonDbContext db)
    {
        await DamBaoBangAsync(db);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM bang_gia_ghi_chu");
    }
}
