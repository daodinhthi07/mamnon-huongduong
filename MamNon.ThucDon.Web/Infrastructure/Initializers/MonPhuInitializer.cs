using MamNon.ThucDon.Web.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>
/// Đưa các MÓN PHỤ (Phụ sáng / Phụ chiều) vào Danh mục món ăn để chọn được ở Trang chủ.
/// - đảm bảo có 2 nhóm "Phụ sáng", "Phụ chiều" (bảng nhom_bua_an);
/// - nạp sẵn các món phụ đã từng dùng (cong_khai_phu) + mặc định (Yakult, Sữa chua, Kem plant, Sữa Ellac Family).
/// Idempotent: chỉ THÊM món chưa có (theo TÊN trên toàn bảng vì mon_an.ten_mon là UNIQUE), không sửa/xoá/chuyển nhóm món đang có. Gọi SAU DamBaoBangCongKhaiAsync.
/// </summary>
public static class MonPhuInitializer
{
    public const string NhomPhuSang = "Phụ sáng";
    public const string NhomPhuChieu = "Phụ chiều";

    public static async Task DamBaoMonPhuAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThucDonDbContext>();

        var idSang = await DamBaoNhomAsync(db, NhomPhuSang, 2);
        var idChieu = await DamBaoNhomAsync(db, NhomPhuChieu, 10);

        var macDinhChieu = (app.Configuration["CongKhai:PhuChieuMacDinh"] ?? "Sữa Ellac Family").Trim();
        var sang = new List<string> { "Yakult", "Sữa chua", "Kem plant" };
        var chieu = new List<string> { macDinhChieu };
        sang.AddRange(await db.Database.SqlQuery<string>(
            $"SELECT DISTINCT TRIM(phu_sang) AS Value FROM cong_khai_phu WHERE TRIM(COALESCE(phu_sang,'')) <> ''").ToListAsync());
        chieu.AddRange(await db.Database.SqlQuery<string>(
            $"SELECT DISTINCT TRIM(phu_chieu) AS Value FROM cong_khai_phu WHERE TRIM(COALESCE(phu_chieu,'')) <> ''").ToListAsync());

        foreach (var ten in sang.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) await ThemMonAsync(db, ten, idSang);
        foreach (var ten in chieu.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) await ThemMonAsync(db, ten, idChieu);
    }

    private static async Task<int> DamBaoNhomAsync(ThucDonDbContext db, string ten, int thuTu)
    {
        var id = await db.Database.SqlQuery<int>($"SELECT id AS Value FROM nhom_bua_an WHERE ten_nhom = {ten}").ToListAsync();
        if (id.Count > 0) return id[0];
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO nhom_bua_an (ten_nhom, thu_tu) VALUES ({ten}, {thuTu})");
        return (await db.Database.SqlQuery<int>($"SELECT id AS Value FROM nhom_bua_an WHERE ten_nhom = {ten}").ToListAsync())[0];
    }

    private static Task<int> ThemMonAsync(ThucDonDbContext db, string ten, int nhomId) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR IGNORE INTO mon_an (ten_mon, ghi_chu, is_active, nhom_bua_an_id) SELECT {ten}, 'Món phụ', 1, {nhomId} WHERE NOT EXISTS (SELECT 1 FROM mon_an WHERE LOWER(TRIM(ten_mon)) = LOWER(TRIM({ten})))");
}
