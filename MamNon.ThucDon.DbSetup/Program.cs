// Công cụ 1 lần: tự động chạy 4 file trong Database/ vào đúng file .db mà
// project MamNon.ThucDon.Web đang dùng — thay cho việc cài sqlite3 CLI hoặc
// DB Browser rồi copy-dán tay từng file.
//
// CÁCH DÙNG: đứng ở thư mục gốc solution (nơi có MamNon.ThucDon.sln), chạy:
//     dotnet run --project MamNon.ThucDon.DbSetup
//
// Chạy lại nhiều lần vẫn an toàn: file nào đã áp dụng rồi (bảng/cột đã tồn
// tại) sẽ chỉ in cảnh báo màu vàng rồi bỏ qua, không dừng cả chương trình.

using Microsoft.Data.Sqlite;

// AppContext.BaseDirectory nằm ở .../MamNon.ThucDon.DbSetup/bin/Debug/net8.0/
// -> lùi 4 cấp để ra thư mục gốc solution (nơi chứa Database/, MamNon.ThucDon.Web/...).
var thuMucGoc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var duongDanDb = Path.Combine(thuMucGoc, "MamNon.ThucDon.Web", "kho_thuc_pham_mam_non.db");
var thuMucSql = Path.Combine(thuMucGoc, "Database");

var cacFileTheoThuTu = new[]
{
    "01_kho_thuc_pham_mam_non.sql",
    "02_thuc_don_mon_an.sql",
    "03_dinh_muc_ty_le_ngan_sach.sql",
    "04_nhom_bua_an.sql",
    "05_seed_hang_hoa.sql",
    "06_seed_mon_an_tu_thuc_don.sql",
    "07_gop_khoi_nha_tre.sql",
};

Console.WriteLine($"Thư mục gốc solution phát hiện được : {thuMucGoc}");
Console.WriteLine($"File database sẽ ghi vào            : {duongDanDb}");
Console.WriteLine($"Thư mục chứa file SQL                : {thuMucSql}");
Console.WriteLine();

if (!File.Exists(duongDanDb))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("KHÔNG tìm thấy file database ở đường dẫn trên.");
    Console.WriteLine("-> Chạy 'dotnet ef database update -c ApplicationDbContext -p MamNon.ThucDon.Web -s MamNon.ThucDon.Web' trước đã (bước tạo bảng đăng nhập), nó sẽ tự tạo file .db này.");
    Console.ResetColor();
    return;
}

using var ketNoi = new SqliteConnection($"Data Source={duongDanDb}");
ketNoi.Open();

foreach (var tenFile in cacFileTheoThuTu)
{
    var duongDanFile = Path.Combine(thuMucSql, tenFile);
    Console.WriteLine($"=== Đang chạy {tenFile} ===");

    if (!File.Exists(duongDanFile))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  Không tìm thấy file {duongDanFile} — DỪNG LẠI, kiểm tra lại thư mục Database/.");
        Console.ResetColor();
        return;
    }

    var noiDungSql = File.ReadAllText(duongDanFile);

    try
    {
        using var lenh = ketNoi.CreateCommand();
        lenh.CommandText = noiDungSql;
        lenh.ExecuteNonQuery();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  OK.");
        Console.ResetColor();
    }
    catch (SqliteException ex) when (
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Bỏ qua — có vẻ file này đã được chạy trước đó rồi ({ex.Message}).");
        Console.ResetColor();
    }
    catch (SqliteException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  LỖI THẬT SỰ, dừng lại tại đây: {ex.Message}");
        Console.WriteLine("  Gửi lại đúng dòng lỗi này để được hỗ trợ tiếp.");
        Console.ResetColor();
        return;
    }
}

Console.WriteLine();
Console.WriteLine("=== Danh sách bảng hiện có trong database sau khi chạy xong ===");
using (var lenhListTable = ketNoi.CreateCommand())
{
    lenhListTable.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
    using var reader = lenhListTable.ExecuteReader();
    while (reader.Read())
    {
        Console.WriteLine($"  - {reader.GetString(0)}");
    }
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine();
Console.WriteLine("XONG. Nếu thấy các bảng mon_an, hang_hoa, thuc_don, nhom_bua_an ở danh sách trên là thành công.");
Console.WriteLine("Giờ có thể chạy lại: dotnet run --project MamNon.ThucDon.Web");
Console.ResetColor();
