namespace MamNon.ThucDon.Web.ViewModels;

/// <summary>1 ô của lưới lịch sử: tên món (null = chưa có) hoặc ngày nghỉ.</summary>
public sealed class LichSuOVm
{
    public string? TenMon { get; set; }
    public bool Nghi { get; set; }
}

public sealed class LichSuBuaRowVm
{
    public string TenBua { get; set; } = string.Empty;

    /// <summary>6 ô Thứ 2 -> Thứ 7.</summary>
    public LichSuOVm[] Cac { get; } = Enumerable.Range(0, 6).Select(_ => new LichSuOVm()).ToArray();
}

/// <summary>Lưới thực đơn của 1 khối trong 1 tuần (cùng kiểu với Dashboard: Mẫu giáo xanh lá, Nhà trẻ hồng).</summary>
public sealed class LichSuKhoiVm
{
    public string TenKhoi { get; set; } = string.Empty;
    public List<LichSuBuaRowVm> CacBua { get; set; } = new();
    public string ThemeClass => TenKhoi.Contains("Mẫu giáo") ? "dashboard-mau-giao" : "dashboard-nha-tre";
}

public sealed class LichSuTuanVm
{
    /// <summary>Thứ tự tuần trong tháng: 1, 2, 3...</summary>
    public int SoTuan { get; set; }

    /// <summary>Ký hiệu tuần: 1 = A, 2 = B, 3 = C, 4 = D, 5 = E (như tên file "TUẦN 5 THÁNG 9 E").</summary>
    public char ChuCai { get; set; }

    public DateOnly TuanBatDau { get; set; }
    public DateOnly TuanKetThuc => TuanBatDau.AddDays(5);

    /// <summary>"Đã duyệt" / "Chờ duyệt" / "Nháp" / "Chưa có thực đơn".</summary>
    public string TrangThai { get; set; } = "Chưa có thực đơn";
    public bool CoThucDon { get; set; }

    /// <summary>Tuần nằm vắt qua 2 tháng (VD 28/09 - 03/10) — hiện chú thích để khỏi nhầm.</summary>
    public bool VatQuaHaiThang => TuanBatDau.Month != TuanKetThuc.Month;

    public string TrangThaiClass => TrangThai switch
    {
        "Đã duyệt" => "ls-tt-duyet",
        "Chờ duyệt" => "ls-tt-cho",
        "Nháp" => "ls-tt-nhap",
        _ => "ls-tt-chua",
    };

    public List<LichSuKhoiVm> CacKhoi { get; set; } = new();

    /// <summary>Thứ 2 của tuần trong file Excel mẫu đã lưu (null = chưa có file mẫu cho tuần này).</summary>
    public DateOnly? MauThuHai { get; set; }
    public bool CoMau => MauThuHai.HasValue;
}

public sealed class LichSuLanDungVm
{
    public DateOnly Ngay { get; set; }
    public char Tuan { get; set; }
}

/// <summary>Thống kê 1 món đã dùng trong tháng (gộp cả bản "(xế)" cùng tên).</summary>
public sealed class LichSuMonDaDungVm
{
    public string TenMon { get; set; } = string.Empty;
    public string? TenNhom { get; set; }
    public List<LichSuLanDungVm> LanDung { get; set; } = new();
    public DateOnly? LanCuoiTruocThang { get; set; }
    public int SoNgay => LanDung.Count;
}

public sealed class LichSuThucDonViewModel
{
    public int Nam { get; set; }
    public int Thang { get; set; }
    public string? TenCoSoDaiDien { get; set; }
    public List<LichSuTuanVm> CacTuan { get; set; } = new();
    public List<LichSuMonDaDungVm> MonDaDung { get; set; } = new();

    /// <summary>Tên các file mẫu tuần đã tải lên (hiện ở cuối trang).</summary>
    public List<string> CacMau { get; set; } = new();

    /// <summary>Gợi ý ngày Thứ 2 của tuần mới = Thứ 2 tuần sau.</summary>
    public DateOnly GoiYTuanMoi { get; set; }

    public string ThangInput => $"{Nam:D4}-{Thang:D2}";
    public string ThangTruoc => new DateOnly(Nam, Thang, 1).AddMonths(-1).ToString("yyyy-MM");
    public string ThangSau => new DateOnly(Nam, Thang, 1).AddMonths(1).ToString("yyyy-MM");

    public static readonly string[] TenThu = { "THỨ 2", "THỨ 3", "THỨ 4", "THỨ 5", "THỨ 6", "THỨ 7" };

    // ---------- Quy tắc chia tuần theo tháng (khớp tên file mẫu) ----------
    // Tuần = Thứ 2 -> Thứ 7. Tuần thuộc THÁNG có từ 3 ngày (trong 6 ngày Thứ 2 -> Thứ 7) trở lên; đủ 3/3 thì tính
    // về tháng của Thứ 2. VD: 31/08-05/09 = tuần A tháng 9; 28/09-03/10 = tuần E tháng 9 (3/3, về tháng 9);
    // 05/10-10/10 = tuần A tháng 10. Muốn đổi cách chia chỉ cần sửa hàm ThangCuaTuan bên dưới.

    /// <summary>Tên file Excel xuất theo tuần — giống trang Lịch sử: "TUẦN {số tuần} THÁNG {tháng} {A,B,C…}.xlsx". Mọi nút Xuất Excel dùng chung.</summary>
    public static string TenFileTuan(DateOnly ngayBatKy)
    {
        var thuHai = ThuHaiCuaTuan(ngayBatKy);
        var (nam, th) = ThangCuaTuan(thuHai);
        var idx = Math.Max(0, CacTuanCuaThang(nam, th).IndexOf(thuHai));
        return $"TUẦN {idx + 1} THÁNG {th} {(char)('A' + idx)}.xlsx";
    }

    public static DateOnly ThuHaiCuaTuan(DateOnly ngay) => ngay.AddDays(-(((int)ngay.DayOfWeek + 6) % 7));

    public static (int Nam, int Thang) ThangCuaTuan(DateOnly thuHai)
    {
        var soNgayCungThangVoiThuHai = Enumerable.Range(0, 6).Count(i => thuHai.AddDays(i).Month == thuHai.Month);
        var ngayQuyDinh = soNgayCungThangVoiThuHai >= 3 ? thuHai : thuHai.AddDays(5);
        return (ngayQuyDinh.Year, ngayQuyDinh.Month);
    }

    /// <summary>Danh sách Thứ 2 của các tuần thuộc tháng (theo thứ tự: A, B, C, D, E...).</summary>
    public static List<DateOnly> CacTuanCuaThang(int nam, int thang)
    {
        var dauThang = new DateOnly(nam, thang, 1);
        var ds = new List<DateOnly>();
        var gioiHan = dauThang.AddMonths(1).AddDays(7);
        for (var t = ThuHaiCuaTuan(dauThang).AddDays(-7); t <= gioiHan; t = t.AddDays(7))
        {
            var (n, m) = ThangCuaTuan(t);
            if (n == nam && m == thang) ds.Add(t);
        }
        return ds;
    }
}
