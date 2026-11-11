using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Bộ nhớ đệm các "tiền tố tên hàng" của mặt hàng có quy tắc cố định — để các hàm static (cân đối, tách món...) biết
/// dòng nào KHÔNG được nhích/co giãn. Nạp lúc khởi động và sau mỗi lần lưu ở trang Quy tắc nguyên liệu.</summary>
public static class QuyTacCache
{
    private static volatile string[] _tienTo = Array.Empty<string>();
    private static volatile (string TenHang, string TenMon)[] _theoMon = Array.Empty<(string, string)>();
    public static IReadOnlyList<string> TienTo => _tienTo;
    /// <summary>Các cặp (tên mặt hàng, tên món) có quy tắc THEO MÓN — dòng đúng mặt hàng đó của đúng món đó là số cố định, không bị nhích khi cân đối.</summary>
    public static IReadOnlyList<(string TenHang, string TenMon)> TheoMon => _theoMon;

    public static async Task NapAsync(ThucDonDbContext db)
    {
        // Quy tắc THEO MÓN (mon_an_id có giá trị) KHÔNG được đưa vào danh sách tiền tố: nếu không, mọi dòng "Cà chua..." của món khác cũng bị khoá.
        var ten = await db.QuyTacNguyenLieus.AsNoTracking().Where(q => q.IsActive && q.CheDo == QuyTacNguyenLieu.KhiCoTrongThucDon && q.MonAnId == null)
            .Select(q => q.HangHoa.TenHang).Distinct().ToListAsync();
        _tienTo = ten.Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).ToArray();

        var theoMon = await db.QuyTacNguyenLieus.AsNoTracking().Where(q => q.IsActive && q.CheDo == QuyTacNguyenLieu.KhiCoTrongThucDon && q.MonAnId != null)
            .Select(q => new { Hang = q.HangHoa.TenHang, Mon = q.MonAn!.TenMon }).Distinct().ToListAsync();
        _theoMon = theoMon.Select(x => ((x.Hang ?? "").Trim(), (x.Mon ?? "").Trim())).Where(x => x.Item1.Length > 0 && x.Item2.Length > 0).ToArray();
    }
}

/// <summary>Tạo bảng quy_tac_nguyen_lieu nếu chưa có (CSDL không dùng EF Migrations) và nạp sẵn các quy tắc đang áp dụng
/// (trước đây viết cứng trong code) khi bảng còn trống. Chạy lại vô hại.</summary>
public static class QuyTacNguyenLieuInitializer
{
    public static async Task DamBaoBangQuyTacNguyenLieuAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThucDonDbContext>();

        await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS quy_tac_nguyen_lieu (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            hang_hoa_id INTEGER NOT NULL REFERENCES hang_hoa(id),
            ma_co_so TEXT NULL,
            loai_ngay INTEGER NOT NULL DEFAULT 0,
            che_do INTEGER NOT NULL DEFAULT 0,
            sl_an_sang NUMERIC NOT NULL DEFAULT 0,
            sl_nha_tre NUMERIC NOT NULL DEFAULT 0,
            sl_mau_giao NUMERIC NOT NULL DEFAULT 0,
            khong_dung INTEGER NOT NULL DEFAULT 0,
            ghi_chu TEXT NULL,
            is_active INTEGER NOT NULL DEFAULT 1)");

        // Định lượng riêng theo chợ (PH / ĐT) — xem DinhLuongNguyenLieuTheoCoSo.
        await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS dinh_luong_nguyen_lieu_theo_co_so (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            mon_an_id INTEGER NOT NULL REFERENCES mon_an(id) ON DELETE CASCADE,
            hang_hoa_id INTEGER NOT NULL REFERENCES hang_hoa(id),
            ma_co_so TEXT NOT NULL,
            dinh_luong DECIMAL(10,4) NOT NULL,
            ghi_chu TEXT NULL,
            UNIQUE(mon_an_id, hang_hoa_id, ma_co_so))");

        // Quy tắc THEO MÓN: thêm cột mon_an_id (NULL = quy tắc theo mặt hàng như cũ). Chạy lại vô hại.
        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            await using (var kiemTra = conn.CreateCommand())
            {
                kiemTra.CommandText = "SELECT COUNT(*) FROM pragma_table_info('quy_tac_nguyen_lieu') WHERE name = 'mon_an_id'";
                if (Convert.ToInt32(await kiemTra.ExecuteScalarAsync()) == 0)
                {
                    await using var them = conn.CreateCommand();
                    them.CommandText = "ALTER TABLE quy_tac_nguyen_lieu ADD COLUMN mon_an_id INTEGER NULL REFERENCES mon_an(id) ON DELETE CASCADE";
                    await them.ExecuteNonQueryAsync();
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (!await db.QuyTacNguyenLieus.AnyAsync())
        {
            var coHang = (await db.HangHoas.Select(h => h.Id).ToListAsync()).ToHashSet();
            const int M = QuyTacNguyenLieu.MoiNgay, K = QuyTacNguyenLieu.KhiCoTrongThucDon;
            var ds = new List<QuyTacNguyenLieu>();
            void Them(int hang, string? coSo, int ngay, int cheDo, decimal sang, decimal nt, decimal mg, bool khongDung = false, string? ghiChu = null)
            {
                if (!coHang.Contains(hang)) return;
                ds.Add(new QuyTacNguyenLieu { HangHoaId = hang, MaCoSo = coSo, LoaiNgay = ngay, CheDo = cheDo, SlAnSang = sang, SlNhaTre = nt, SlMauGiao = mg, KhongDung = khongDung, GhiChu = ghiChu });
            }

            // --- Mục 2: gia vị mỗi ngày (ăn sáng / nhà trẻ / mẫu giáo). 1 = Thứ 2–6, 2 = Thứ 7 ---
            //   hàng: 412 bột canh, 428 bột nêm, 450 dầu, 504 nước mắm, 539 xì dầu, 469 đường trắng
            (int Id, decimal[] Thuong, decimal[] Thu7)[] giaVi =
            {
                (412, new[] { 0.5m, 0.5m, 2m },   new[] { 0.2m, 0.2m, 1m }),
                (428, new[] { 0.2m, 0.2m, 0.6m }, new[] { 0.2m, 0.2m, 0.5m }),
                (450, new[] { 0.3m, 0.2m, 2.1m }, new[] { 0.2m, 0.1m, 0.9m }),
                (504, new[] { 0.3m, 0.2m, 1.6m }, new[] { 0.2m, 0.2m, 0.9m }),
                (539, new[] { 0.3m, 0.2m, 0.6m }, new[] { 0.2m, 0.2m, 0.4m }),
                (469, new[] { 0.3m, 0.2m, 0.6m }, new[] { 0.2m, 0.2m, 0.4m }),
            };
            foreach (var g in giaVi)
            {
                Them(g.Id, "TC,PH", 1, M, g.Thuong[0], g.Thuong[1], g.Thuong[2]);
                Them(g.Id, "TC,PH", 2, M, g.Thu7[0], g.Thu7[1], g.Thu7[2]);
                Them(g.Id, "ĐT", 0, M, 0.1m, 0.1m, 0.2m);
            }

            // --- Mục 3/4: cố định KHI CÓ trong thực đơn ---
            Them(188, "PH", 0, K, 0.2m, 0.1m, 0.2m, ghiChu: "Hành ngò — chỉ PH");
            Them(188, "TC", 0, K, 0, 0, 0, khongDung: true, ghiChu: "TC không ăn hành ngò");
            Them(187, "PH", 0, K, 0.2m, 0.1m, 0.2m, ghiChu: "Hành lá — chỉ PH");
            Them(187, "TC", 0, K, 0, 0, 0, khongDung: true, ghiChu: "TC không ăn hành lá");
            Them(225, "TC,PH", 0, K, 0, 0.2m, 0.3m);
            Them(225, "ĐT", 0, K, 0, 0.1m, 0.2m);
            Them(470, "TC,PH", 0, K, 0, 0.2m, 0.3m);
            Them(470, "ĐT", 0, K, 0, 0, 0, khongDung: true);
            Them(392, "TC,PH", 0, K, 0, 0.1m, 0.2m);
            Them(392, "ĐT", 0, K, 0, 0, 0, khongDung: true);
            Them(504, "TC,PH", 0, K, 0, 0.5m, 0.5m, ghiChu: "Nước mắm đệ nhị 800ml");
            Them(504, "ĐT", 0, K, 0, 0, 0, khongDung: true);
            Them(698, "TC,PH", 0, K, 0, 0, 0.5m);
            Them(698, "ĐT", 0, K, 0, 0, 0, khongDung: true);
            Them(694, "TC,PH", 0, K, 1, 1, 1, ghiChu: "Dầu mè trắng — 1 chai mỗi khối");
            Them(694, "ĐT", 0, K, 1, 0, 1, ghiChu: "Dầu mè trắng — 1 chai");
            Them(688, "TC,PH", 0, K, 0, 0.2m, 0.8m);
            Them(688, "ĐT", 0, K, 0, 0.1m, 0.2m);

            db.QuyTacNguyenLieus.AddRange(ds);
            await db.SaveChangesAsync();
        }
        await ThemQuyTacBoSungAsync(db);
        await QuyTacCache.NapAsync(db);
    }

    /// <summary>Quy tắc bổ sung (thêm nếu CHƯA có — chạy lại vô hại, không ghi đè số đã sửa ở trang /quy-tac-nguyen-lieu):
    ///  1) Mỡ heo — món nào có mỡ heo thì cố định: TC, PH = 0,5 kg; ĐT = 0,2 kg (bữa sáng 1 cột; bữa chính đặt ở cột Mẫu giáo, Nhà trẻ = 0 — theo cách file mẫu ghi số tổng của chợ ở cột Mẫu giáo).
    ///  2) Món "Thịt xúi mại sốt cà" (quy tắc THEO MÓN — cà chua / bánh mì còn dùng ở món khác nên không khoá theo mặt hàng):
    ///     TC, PH: bánh mì ổ lớn 8 ổ, cà chua 4 kg, cà chua lon 3 lon;  ĐT: bánh mì ổ lớn 3 ổ, cà chua 0,7 kg, cà chua lon 1 lon.</summary>
    private static async Task ThemQuyTacBoSungAsync(ThucDonDbContext db)
    {
        const int K = QuyTacNguyenLieu.KhiCoTrongThucDon;
        var coHang = (await db.HangHoas.Select(h => h.Id).ToListAsync()).ToHashSet();
        var monSoiId = await db.MonAns.Where(m => m.TenMon == "Thịt xúi mại sốt cà").Select(m => (int?)m.Id).FirstOrDefaultAsync();
        var monChaoId = await db.MonAns.Where(m => m.TenMon == "Cháo nấm").Select(m => (int?)m.Id).FirstOrDefaultAsync();
        var daCo = await db.QuyTacNguyenLieus.AsNoTracking().Where(q => q.CheDo == K)
            .Select(q => new { q.HangHoaId, q.MaCoSo, q.MonAnId }).ToListAsync();
        var them = new List<QuyTacNguyenLieu>();

        void Them(int hang, int? monId, string coSo, decimal mauGiao, string ghiChu, decimal anSang = 0m)
        {
            if (!coHang.Contains(hang)) return;
            if (daCo.Any(d => d.HangHoaId == hang && d.MonAnId == monId && string.Equals(d.MaCoSo, coSo, StringComparison.Ordinal))) return;
            them.Add(new QuyTacNguyenLieu { HangHoaId = hang, MonAnId = monId, MaCoSo = coSo, LoaiNgay = 0, CheDo = K, SlAnSang = anSang, SlNhaTre = 0, SlMauGiao = mauGiao, GhiChu = ghiChu });
        }

        // 1) Mỡ heo (hàng 12): theo mặt hàng
        Them(12, null, "TC,PH", 0.5m, "Mỡ heo cố định 0,5 kg (TC, PH)", anSang: 0.5m);
        Them(12, null, "ĐT", 0.2m, "Mỡ heo cố định 0,2 kg (ĐT)", anSang: 0.2m);

        // 2) Thịt xúi mại sốt cà: theo món
        if (monSoiId is { } mon)
        {
            const string gc = "Thịt xúi mại sốt cà — cố định theo món";
            Them(558, mon, "TC,PH", 8m, gc);   // Bánh mì ổ lớn Quốc Doanh (ổ)
            Them(135, mon, "TC,PH", 4m, gc);   // Cà chua (kg)
            Them(435, mon, "TC,PH", 3m, gc);   // Cà chua lon (lon)
            Them(558, mon, "ĐT", 3m, gc);
            Them(135, mon, "ĐT", 0.7m, gc);
            Them(435, mon, "ĐT", 1m, gc);
        }

        // 3) Cháo nấm (bữa SÁNG, 1 cột): TC, PH — đậu hà lan Metro 2 kg, cà rốt 4 kg;  ĐT — đậu hà lan Metro 0,5 kg, cà rốt 1 kg  (đậu ngự không có nên đổi sang đậu hà lan Metro, hàng 162)
        if (monChaoId is { } chao)
        {
            // Đã từng nạp quy tắc theo món bằng đậu ngự khô (369): đổi sang đậu hà lan Metro (162) để không còn đậu ngự (chạy lại vô hại).
            if (coHang.Contains(162))
            {
                await db.Database.ExecuteSqlRawAsync("UPDATE quy_tac_nguyen_lieu SET hang_hoa_id = 162 WHERE hang_hoa_id = 369 AND mon_an_id = {0} AND NOT EXISTS (SELECT 1 FROM quy_tac_nguyen_lieu x WHERE x.hang_hoa_id = 162 AND x.mon_an_id = {0} AND IFNULL(x.ma_co_so,'') = IFNULL(quy_tac_nguyen_lieu.ma_co_so,''))", chao);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM quy_tac_nguyen_lieu WHERE hang_hoa_id = 369 AND mon_an_id = {0}", chao);
                daCo = await db.QuyTacNguyenLieus.AsNoTracking().Where(q => q.CheDo == K)
                    .Select(q => new { q.HangHoaId, q.MaCoSo, q.MonAnId }).ToListAsync();
            }
            const string gc2 = "Cháo nấm — cố định theo món";
            Them(162, chao, "TC,PH", 0m, gc2, anSang: 2m);     // Đậu hà lan Metro
            Them(138, chao, "TC,PH", 0m, gc2, anSang: 4m);     // Cà rốt Đà lạt
            Them(162, chao, "ĐT", 0m, gc2, anSang: 0.5m);   // Đậu hà lan Metro
            Them(138, chao, "ĐT", 0m, gc2, anSang: 1m);
        }

        if (them.Count == 0) return;
        db.QuyTacNguyenLieus.AddRange(them);
        await db.SaveChangesAsync();
    }
}
