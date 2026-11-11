using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MamNon.ThucDon.Web.ViewModels;

namespace MamNon.ThucDon.Web.Services;

/// <summary>1 file Excel mẫu của 1 tuần (VD "TUẦN 5 THÁNG 9 E.xlsx") đã lưu trong thư mục mẫu.</summary>
public sealed record MauTuanInfo(string DuongDan, DateOnly ThuHai, int SoTuan, char KyHieu, int Thang, int Nam)
{
    public string Ten => $"Tuần {SoTuan} tháng {Thang}/{Nam} ({KyHieu})";
}

/// <summary>1 ô món trong sheet "Thực đơn tuần ok" của file mẫu.</summary>
public sealed class MauTuanMonCell
{
    /// <summary>1 = Mẫu giáo, 2 = Nhà trẻ.</summary>
    public int Khoi { get; set; }
    public string Nhan { get; set; } = string.Empty;
    /// <summary>0 = Thứ 2 ... 5 = Thứ 7.</summary>
    public int Ngay { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>Thực đơn của 1 tuần đọc từ file mẫu (để nạp vào hệ thống).</summary>
public sealed class MauTuanThucDon
{
    public List<MauTuanMonCell> Cells { get; set; } = new();
    public string? NguoiLap { get; set; }
    public string? NguoiDuyet { get; set; }
    /// <summary>Ngày nghỉ: ngày (0..5) -> (nghỉ lễ?, ghi chú).</summary>
    public Dictionary<int, (bool NghiLe, string GhiChu)> NgayNghi { get; set; } = new();
}

/// <summary>Số suất mặc định đọc từ file mẫu + các ngày thật sự có thực đơn.</summary>
public sealed class MauTuanChiTiet
{
    public List<int> NgayCoThucDon { get; set; } = new();

    /// <summary>Khóa cơ sở ("TC", "PH", "ĐT") -> ngày (0 = Thứ 2 ... 5 = Thứ 7) -> {Ăn sáng, Nhà trẻ, Mẫu giáo}.</summary>
    public Dictionary<string, Dictionary<int, double?[]>> Suat { get; set; } = new();

    /// <summary>Tiền ăn 1 cháu cùng cấu trúc với Suat: {Ăn sáng, Nhà trẻ, Mẫu giáo}.</summary>
    public Dictionary<string, Dictionary<int, double?[]>> Tien { get; set; } = new();
}

/// <summary>
/// Xuất Excel "y như file mẫu tuần": lấy nguyên file mẫu (giữ nguyên bố cục, công thức, định dạng), CHỈ đổi
/// (1) các ngày sang tuần mong muốn và (2) số suất ăn từng ngày; số lượng thực phẩm được co giãn theo tỷ lệ
/// số suất mới / cũ rồi cân đối để "Chênh lệch cuối ngày" về gần 0 như file mẫu. Làm trực tiếp trên XML của file
/// .xlsx nên mọi thứ khác (sheet, kiểu chữ, liên kết, hình) giữ nguyên.
/// </summary>
public sealed class MauTuanExcelService
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly string _thuMuc;

    public string ThuMuc => _thuMuc;

    public MauTuanExcelService(IConfiguration cauHinh, IWebHostEnvironment moiTruong)
    {
        var cauHinhThuMuc = cauHinh["MauTuan:ThuMuc"];
        _thuMuc = string.IsNullOrWhiteSpace(cauHinhThuMuc)
            ? Path.Combine(moiTruong.ContentRootPath, "App_Data", "MauTuan")
            : cauHinhThuMuc;
        Directory.CreateDirectory(_thuMuc);
    }

    // ===================================================== Danh sách file mẫu =====================================================

    public List<MauTuanInfo> LietKe()
    {
        var ds = new List<MauTuanInfo>();
        foreach (var duongDan in Directory.GetFiles(_thuMuc, "*.xlsx"))
        {
            try
            {
                var info = DocThongTin(duongDan, File.ReadAllBytes(duongDan));
                if (info != null) ds.Add(info);
            }
            catch (Exception)
            {
                // file hỏng / không phải file mẫu: bỏ qua
            }
        }
        return ds.OrderBy(x => x.ThuHai).ToList();
    }

    public MauTuanInfo? Tim(DateOnly thuHai) => LietKe().FirstOrDefault(x => x.ThuHai == thuHai);

    public (bool Ok, string? Loi, MauTuanInfo? Info) LuuMau(byte[] noiDung)
    {
        try
        {
            var tam = DocThongTin("", noiDung);
            if (tam == null) return (false, "Không tìm thấy ngày của tuần trong file (cần file thực đơn tuần theo mẫu).", null);
            var duongDan = Path.Combine(_thuMuc, "TUAN_" + tam.ThuHai.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx");
            File.WriteAllBytes(duongDan, noiDung);
            return (true, null, tam with { DuongDan = duongDan });
        }
        catch (Exception)
        {
            return (false, "File không đọc được (không phải file .xlsx hợp lệ).", null);
        }
    }

    private static MauTuanInfo? DocThongTin(string duongDan, byte[] noiDung)
    {
        var goi = DocGoi(noiDung);
        if (!goi.Files.TryGetValue("xl/sharedStrings.xml", out var ssBytes)) return null;
        var xml = Encoding.UTF8.GetString(ssBytes);
        var thuHai = TimThuHaiNguon(xml);
        if (thuHai == null) return null;
        var (nam, thang) = LichSuThucDonViewModel.ThangCuaTuan(thuHai.Value);
        var idx = LichSuThucDonViewModel.CacTuanCuaThang(nam, thang).IndexOf(thuHai.Value);
        if (idx < 0) idx = 0;
        return new MauTuanInfo(duongDan, thuHai.Value, idx + 1, (char)('A' + idx), thang, nam);
    }

    // ===================================================== Đọc số suất mặc định =====================================================

    public MauTuanChiTiet DocChiTiet(MauTuanInfo mau)
    {
        var goi = DocGoi(File.ReadAllBytes(mau.DuongDan));
        var ss = DocSharedStrings(goi);
        var chiTiet = new MauTuanChiTiet();
        chiTiet.NgayCoThucDon = NgayCoThucDon(goi, ss);
        foreach (var (ten, duongDan) in DocSheets(goi))
        {
            if (!ten.StartsWith("Chợ", StringComparison.Ordinal)) continue;
            var khoa = KhoaCoSo(ten);
            var sh = new SheetX(goi.Files[duongDan], ss);
            var theoNgay = new Dictionary<int, double?[]>();
            var tienTheoNgay = new Dictionary<int, double?[]>();
            foreach (var b in TimKhoi(sh))
            {
                if (theoNgay.ContainsKey(b.Ngay)) continue;
                var hang = sh.Rows[b.DongSuat];
                theoNgay[b.Ngay] = new[] { sh.Val(Lay(hang, 8)), sh.Val(Lay(hang, 9)), sh.Val(Lay(hang, 10)) };
                sh.Rows.TryGetValue(b.DongSuat - 1, out var hangTien);
                tienTheoNgay[b.Ngay] = new[] { sh.Val(Lay(hangTien, 8)), sh.Val(Lay(hangTien, 9)), sh.Val(Lay(hangTien, 10)) };
            }
            chiTiet.Suat[khoa] = theoNgay;
            chiTiet.Tien[khoa] = tienTheoNgay;
        }
        return chiTiet;
    }

    /// <summary>Đọc thực đơn (món từng ngày, từng bữa) trong sheet "Thực đơn tuần ok" của file mẫu.</summary>
    public MauTuanThucDon DocThucDon(MauTuanInfo mau) => DocThucDon(mau, File.ReadAllBytes(mau.DuongDan), false);

    /// <summary>Đọc tuần + thực đơn thẳng từ NỘI DUNG file Excel tải lên (không lưu file vào thư mục mẫu) — dùng để nạp thực đơn công khai.
    /// Tuần (Thứ 2) tự nhận từ ngày ghi trong file; null nếu không nhận ra tuần. <c>choCongKhai</c> = true: (1) lấy thêm dòng "Sữa" (dòng không nhãn ngay dưới XẾ) của Mẫu giáo;
    /// (2) file có nhiều nhóm tuổi Nhà trẻ (VD "6-18 T" và "18-24 T") thì bỏ khối 6-18 tháng, lấy khối đủ Canh / Mặn như các tuần khác.</summary>
    public (MauTuanInfo? Info, MauTuanThucDon ThucDon) DocThucDonTuNoiDung(byte[] noiDung, bool choCongKhai = false)
    {
        var info = DocThongTin("", noiDung);
        return info is null ? (null, new MauTuanThucDon()) : (info, DocThucDon(info, noiDung, choCongKhai));
    }

    private MauTuanThucDon DocThucDon(MauTuanInfo mau, byte[] noiDung, bool choCongKhai)
    {
        var goi = DocGoi(noiDung);
        var ss = DocSharedStrings(goi);
        var kq = new MauTuanThucDon();
        var duong = DocSheets(goi).FirstOrDefault(s => s.Ten.Trim() == "Thực đơn tuần ok").Duong;
        if (duong == null) return kq;
        var sh = new SheetX(goi.Files[duong], ss);
        var nhan = new HashSet<string> { "SÁNG", "CANH", "MẶN", "XÀO", "TRÁNG MIỆNG", "XẾ", "PHỤ SÁNG", "TRƯA", "CHÍNH CHIỀU" };
        if (choCongKhai) nhan.Add("CẢI THIỆN");   // dòng "Cải thiện" của chi nhánh mới (chỉ để công khai; không ảnh hưởng tính tiền chợ)
        var khoi = 0;
        var nhanTruoc = "";   // nhãn của dòng món liền trước (để nhận dòng "Sữa" không nhãn dưới XẾ của Mẫu giáo)
        foreach (var r in sh.Rows.Keys.OrderBy(x => x))
        {
            var hang = sh.Rows[r];
            var lab = (sh.Text(Lay(hang, 1)) ?? "").Trim();
            var up = lab.ToUpperInvariant();
            if (up.StartsWith("THỰC ĐƠN", StringComparison.Ordinal) && up.Contains("MẪU GIÁO")) { khoi = 1; nhanTruoc = ""; continue; }
            if (up.StartsWith("THỰC ĐƠN", StringComparison.Ordinal) && up.Contains("NHÀ TRẺ"))
            {
                khoi = choCongKhai && Regex.IsMatch(up, @"\b6\s*-\s*18") ? -1 : 2;   // -1 = bỏ qua khối này cho tới "Người lập"
                continue;
            }
            if (up.StartsWith("NGƯỜI LẬP", StringComparison.Ordinal))
            {
                if (khoi == 2) break;
                khoi = 0;
                continue;
            }
            var laDongSua = choCongKhai && khoi == 1 && up.Length == 0 && nhanTruoc == "XẾ";
            if (khoi <= 0 || (!nhan.Contains(up) && !laDongSua)) continue;
            if (laDongSua) up = "SỮA";
            nhanTruoc = up;
            for (var col = 2; col <= 7; col++)
            {
                var t = (sh.Text(Lay(hang, col)) ?? "").Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("nghỉ", StringComparison.OrdinalIgnoreCase))
                {
                    var ngayNghi = col - 2;
                    var m = Regex.Match(t, @"(\d{1,2})/(\d{1,2})");
                    if (m.Success && ThuNgay(m.Groups[1].Value, m.Groups[2].Value, mau.ThuHai.Year.ToString(CultureInfo.InvariantCulture), out var dn))
                    {
                        var idx = dn.DayNumber - mau.ThuHai.DayNumber;
                        if (idx >= 0 && idx <= 5) ngayNghi = idx;
                    }
                    kq.NgayNghi[ngayNghi] = (m.Success, m.Success ? "Nghỉ lễ " + m.Value : t);
                    continue;
                }
                kq.Cells.Add(new MauTuanMonCell { Khoi = khoi, Nhan = up, Ngay = col - 2, Text = t });
            }
        }

        // vùng chữ ký: người lập (cột A) và hiệu trưởng (cột E)
        foreach (var r in sh.Rows.Keys.OrderBy(x => x))
        {
            if (r < 10 || r > 40) continue;
            var hang = sh.Rows[r];
            var a = (sh.Text(Lay(hang, 1)) ?? "").Trim();
            var e = (sh.Text(Lay(hang, 5)) ?? "").Trim();
            if (kq.NguoiLap == null && a.StartsWith("Nguyễn", StringComparison.Ordinal)) kq.NguoiLap = a;
            if (kq.NguoiDuyet == null && r >= 17 && e.Length > 0 && !e.StartsWith("Ngũ", StringComparison.Ordinal) && !e.StartsWith("Hiệu", StringComparison.Ordinal)) kq.NguoiDuyet = e;
        }
        return kq;
    }

    private static string KhoaCoSo(string tenSheet) => tenSheet.Trim().Replace(" ok", "").Replace("Chợ ", "").Trim();

    private static List<int> NgayCoThucDon(XPackage goi, List<string> ss)
    {
        var kq = new HashSet<int>();
        var sheets = DocSheets(goi);
        var duongDan = sheets.FirstOrDefault(s => s.Ten.Trim() == "Thực đơn tuần ok").Duong;
        if (duongDan == null) return kq.ToList();
        var sh = new SheetX(goi.Files[duongDan], ss);
        var nhan = new HashSet<string> { "SÁNG", "CANH", "MẶN", "XÀO", "TRÁNG MIỆNG", "XẾ", "PHỤ SÁNG", "TRƯA", "CHÍNH CHIỀU" };
        foreach (var r in sh.Rows.Keys.OrderBy(x => x))
        {
            if (r < 7 || r >= 60) continue;
            var hang = sh.Rows[r];
            var lab = (sh.Text(Lay(hang, 1)) ?? "").Trim().ToUpperInvariant();
            if (lab.StartsWith("NGƯỜI LẬP", StringComparison.Ordinal) && r > 20) break;
            if (!nhan.Contains(lab)) continue;
            for (var col = 2; col <= 7; col++)
            {
                var t = (sh.Text(Lay(hang, col)) ?? "").Trim();
                if (t.Length > 0 && !t.StartsWith("nghỉ", StringComparison.OrdinalIgnoreCase)) kq.Add(col - 2);
            }
        }
        return kq.OrderBy(x => x).ToList();
    }

    // ===================================================== Tạo file mới =====================================================

    /// <param name="suatMoi">Khóa "TC|1" (cơ sở|ngày 0..5) -> {Ăn sáng, Nhà trẻ, Mẫu giáo}; phần tử null = giữ nguyên.</param>
    /// <param name="tienMoi">Khóa "TC|1" -> {Ăn sáng, Nhà trẻ, Mẫu giáo} tiền ăn 1 cháu; phần tử null = giữ nguyên.</param>
    public byte[] TaoFile(MauTuanInfo mau, DateOnly thuHaiMoi, IDictionary<string, double?[]> suatMoi, List<string> canhBao,
        IDictionary<string, double?[]>? tienMoi = null)
    {
        var goi = DocGoi(File.ReadAllBytes(mau.DuongDan));
        var ss = DocSharedStrings(goi);
        var cu = mau.ThuHai;

        foreach (var (ten, duongDan) in DocSheets(goi))
        {
            if (!ten.StartsWith("Chợ", StringComparison.Ordinal)) continue;
            var khoa = KhoaCoSo(ten);
            var sh = new SheetX(goi.Files[duongDan], ss);
            var khoi = TimKhoi(sh);
            var ghiDe = new Dictionary<(int, int), double>();   // số suất mới theo ô tiêu đề (mọi ngày)
            var cu_ = new Dictionary<int, double?[]>();
            for (var i = 0; i < khoi.Count; i++)
            {
                var b = khoi[i];
                var hang = sh.Rows[b.DongSuat];
                cu_[i] = new[] { sh.Val(Lay(hang, 8)), sh.Val(Lay(hang, 9)), sh.Val(Lay(hang, 10)) };
                var coSuat = suatMoi.TryGetValue(khoa + "|" + b.Ngay, out var moi);
                double?[]? tm = null;
                var coTien = tienMoi != null && tienMoi.TryGetValue(khoa + "|" + b.Ngay, out tm);
                if (!coSuat && !coTien) continue;
                sh.Rows.TryGetValue(b.DongSuat - 1, out var hangTien0);
                for (var k = 0; k < 3; k++)
                {
                    if (coSuat && moi![k] != null && Lay(hang, 8 + k) != null) ghiDe[(b.DongSuat, 8 + k)] = moi[k]!.Value;
                    if (coTien && tm![k] != null && Lay(hangTien0, 8 + k) != null) ghiDe[(b.DongSuat - 1, 8 + k)] = tm[k]!.Value;
                }
            }

            Func<string, double> layGiaTri = diaChi =>
            {
                var (dong, cot) = TachDiaChi(diaChi);
                if (ghiDe.TryGetValue((dong, cot), out var v)) return v;
                sh.Rows.TryGetValue(dong, out var hangKhac);
                return sh.Val(Lay(hangKhac, cot)) ?? 0.0;
            };

            for (var i = 0; i < khoi.Count; i++)
            {
                var b = khoi[i];
                var coSuat = suatMoi.TryGetValue(khoa + "|" + b.Ngay, out var moi);
                double?[]? tm = null;
                var coTien = tienMoi != null && tienMoi.TryGetValue(khoa + "|" + b.Ngay, out tm);
                if (!coSuat && !coTien)
                {
                    // Ngày không đổi số suất/tiền: vẫn chốt các dòng "gói" về số nguyên.
                    foreach (var r in b.M2) foreach (var k in new[] { 4, 5, 6 }) LamTronGoi(sh, sh.Rows[r], k, layGiaTri, 1.0);
                    foreach (var r in b.M3) LamTronGoi(sh, sh.Rows[r], 4, layGiaTri, 1.0);
                    foreach (var r in b.M4) { LamTronGoi(sh, sh.Rows[r], 4, layGiaTri, 1.0); LamTronGoi(sh, sh.Rows[r], 5, layGiaTri, 1.0); }
                    continue;
                }
                moi ??= new double?[3];
                tm ??= new double?[3];
                var hang = sh.Rows[b.DongSuat];
                sh.Rows.TryGetValue(b.DongSuat - 1, out var hangTien);
                var cuSuat = cu_[i];
                var hieuLuc = new double?[3];
                var tyLe = new double[3];
                var tyLeTien = new double[3];   // tiền ăn 1 cháu đổi => thực phẩm (mục 3, 4) co giãn theo cả tỷ lệ tiền
                for (var k = 0; k < 3; k++)
                {
                    hieuLuc[k] = moi[k] ?? cuSuat[k];
                    tyLe[k] = (moi[k] == null || cuSuat[k] == null || cuSuat[k] == 0) ? 1.0 : moi[k]!.Value / cuSuat[k]!.Value;
                    var o = Lay(hang, 8 + k);
                    if (moi[k] != null && o != null) sh.SetNum(o, moi[k]!.Value);

                    var oTien = Lay(hangTien, 8 + k);
                    var cuTien = sh.Val(oTien);
                    tyLeTien[k] = (tm[k] == null || cuTien == null || cuTien == 0) ? 1.0 : tm[k]!.Value / cuTien.Value;
                    if (tm[k] != null && oTien != null)
                    {
                        oTien.Element(M + "f")?.Remove();   // đặt tiền cố định, không để công thức ghi đè
                        sh.SetNum(oTien, tm[k]!.Value);
                    }
                }

                foreach (var r in b.M2)
                {
                    var c = sh.Rows[r];
                    CoGian(sh, c, 4, tyLe[0]); CoGian(sh, c, 5, tyLe[1]); CoGian(sh, c, 6, tyLe[2]);
                }
                foreach (var r in b.M3) CoGian(sh, sh.Rows[r], 4, tyLe[0] * tyLeTien[0]);
                foreach (var r in b.M4)
                {
                    var c = sh.Rows[r];
                    CoGian(sh, c, 4, tyLe[1] * tyLeTien[1]); CoGian(sh, c, 5, tyLe[2] * tyLeTien[2]);
                }
                // GÓI: mọi dòng thực phẩm có đơn vị "gói" (kể cả ô đang là công thức, hoặc khi không đổi số suất)
                // đều chốt về số nguyên: dưới 0,5 xuống, từ 0,5 lên.
                foreach (var r in b.M2)
                    for (var k = 0; k < 3; k++) LamTronGoi(sh, sh.Rows[r], 4 + k, layGiaTri, tyLe[k]);
                foreach (var r in b.M3) LamTronGoi(sh, sh.Rows[r], 4, layGiaTri, tyLe[0] * tyLeTien[0]);
                foreach (var r in b.M4)
                {
                    var c = sh.Rows[r];
                    LamTronGoi(sh, c, 4, layGiaTri, tyLe[1] * tyLeTien[1]);
                    LamTronGoi(sh, c, 5, layGiaTri, tyLe[2] * tyLeTien[2]);
                }
                CanDoi(sh, b, hieuLuc, layGiaTri, canhBao, khoa);
            }
            goi.Files[duongDan] = sh.ToBytes();
        }

        // ---- đổi ngày (1 lượt) trong chuỗi dùng chung và các sheet ----
        foreach (var ten in goi.Order.ToList())
        {
            var laChuoi = ten == "xl/sharedStrings.xml";
            var laSheet = ten.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && ten.EndsWith(".xml", StringComparison.Ordinal);
            if (!laChuoi && !laSheet) continue;
            var xml = new UTF8Encoding(false).GetString(goi.Files[ten]);
            var moiXml = ThayNgay(xml, cu, thuHaiMoi);
            if (!ReferenceEquals(moiXml, xml) && moiXml != xml) goi.Files[ten] = new UTF8Encoding(false).GetBytes(moiXml);
        }
        BatTinhLaiKhiMo(goi);
        return GhiGoi(goi);
    }

    /// <summary>Số suất / tiền ăn / số lượng vừa ghi là số cố định, nhưng các ô công thức phụ thuộc vào chúng (Số tiền được phép chi, Thành tiền,
    /// Tổng cộng, Gạo = suất × định mức...) vẫn còn GIÁ TRỊ CŨ lưu sẵn trong file. Bắt Excel tính lại toàn bộ khi mở (fullCalcOnLoad)
    /// và bỏ calcChain cũ (một số ô công thức đã bị thay bằng số nên chuỗi tính cũ không còn đúng).</summary>
    private static void BatTinhLaiKhiMo(XPackage goi)
    {
        var utf8 = new UTF8Encoding(false);
        if (goi.Files.TryGetValue("xl/workbook.xml", out var wb))
        {
            var xml = utf8.GetString(wb);
            xml = Regex.Replace(xml, @"<calcPr\b([^>]*?)/>",
                m => m.Groups[1].Value.Contains("fullCalcOnLoad", StringComparison.Ordinal) ? m.Value : "<calcPr" + m.Groups[1].Value + " fullCalcOnLoad=\"1\"/>",
                RegexOptions.CultureInvariant);
            goi.Files["xl/workbook.xml"] = utf8.GetBytes(xml);
        }
        if (goi.Files.Remove("xl/calcChain.xml"))
        {
            goi.Order.Remove("xl/calcChain.xml");
            if (goi.Files.TryGetValue("[Content_Types].xml", out var ct))
                goi.Files["[Content_Types].xml"] = utf8.GetBytes(Regex.Replace(utf8.GetString(ct), @"<Override\b[^>]*calcChain[^>]*/>", "", RegexOptions.CultureInvariant));
            if (goi.Files.TryGetValue("xl/_rels/workbook.xml.rels", out var rel))
                goi.Files["xl/_rels/workbook.xml.rels"] = utf8.GetBytes(Regex.Replace(utf8.GetString(rel), @"<Relationship\b[^>]*calcChain[^>]*/>", "", RegexOptions.CultureInvariant));
        }
    }

    private static void CoGian(SheetX sh, Dictionary<int, XElement> hang, int cot, double tyLe)
    {
        var c = Lay(hang, cot);
        if (c == null || tyLe == 1.0) return;
        var f = sh.Formula(c);
        if (f != null)
        {
            // công thức dạng 1060/G107 hoặc IF(OR(G17=0),0,2160/G17): co giãn tử số (số tiền cố định)
            var m = Regex.Match(f, @"^\s*([0-9.]+)\s*/\s*G\d+\s*$");
            if (!m.Success) m = Regex.Match(f, @"^\s*IF\(OR\(G\d+=0\),0,([0-9.]+)/G\d+\)\s*$");
            if (m.Success)
            {
                var so = m.Groups[1].Value;
                var moi = (double.Parse(so, CultureInfo.InvariantCulture) * tyLe).ToString("0.##########", CultureInfo.InvariantCulture);
                var fe = c.Element(M + "f");
                if (fe != null)
                {
                    var idx = f.IndexOf(so, StringComparison.Ordinal);
                    fe.Value = f.Substring(0, idx) + moi + f.Substring(idx + so.Length);
                }
            }
            return;
        }
        var x = sh.Num(c);
        if (x == null || x == 0) return;
        sh.SetNum(c, LamTron(x.Value * tyLe, x.Value, sh.Text(Lay(hang, 3))));
    }

    /// <summary>Dòng có ĐVT "gói": thay ô số lượng (công thức hoặc số lẻ) bằng số nguyên đã làm tròn.</summary>
    private static void LamTronGoi(SheetX sh, Dictionary<int, XElement> hang, int cot, Func<string, double> layGiaTri, double tyLeDaCo)
    {
        if (!ChiTietNgayTinhToanService.LaDonViGoi(sh.Text(Lay(hang, 3)))) return;
        var c = Lay(hang, cot);
        if (c == null) return;
        var f = sh.Formula(c);
        double? v;
        if (f != null)
        {
            v = Fx.Evaluate(f, layGiaTri);
            if (v == null) { var cu = sh.Val(c); v = cu == null ? null : cu * tyLeDaCo; }   // công thức lạ: lấy số cũ co giãn theo tỷ lệ
        }
        else v = sh.Num(c);
        if (v == null) return;
        var tron = Math.Round(v.Value + 1e-9, 0, MidpointRounding.AwayFromZero);
        if (f == null && Math.Abs(v.Value - tron) < 1e-9) return;   // đã là số nguyên
        c.Element(M + "f")?.Remove();
        sh.SetNum(c, tron);
    }

    private static double LamTron(double x, double goc, string? dvt)
    {
        // GÓI: dưới 0,5 làm tròn xuống, từ 0,5 trở lên làm tròn lên (luôn ra số nguyên).
        if (ChiTietNgayTinhToanService.LaDonViGoi(dvt)) return Math.Round(x + 1e-9, 0, MidpointRounding.AwayFromZero);
        var d = (dvt ?? "").Trim().ToLowerInvariant();
        var nguyen = Math.Abs(goc - Math.Round(goc)) < 1e-9 && d != "kg" && d != "chai" && d != "lít" && d != "l";
        if (nguyen) return Math.Ceiling(x - 1e-9);   // các đơn vị đếm khác (hộp, quả...) vẫn tròn lên
        return x >= 0.15 ? Math.Round(x + 1e-12, 1, MidpointRounding.AwayFromZero) : Math.Round(x + 1e-12, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class DongChinh
    {
        public int Dong; public int Cot; public double Gia; public double Luong; public double Buoc; public double Goc;
    }

    /// <summary>Đẩy chênh lệch cuối ngày về gần 0: chỉnh từng 0,1 (hoặc 1 đơn vị nguyên) ở dòng thực phẩm có thay đổi tương đối nhỏ nhất.</summary>
    private static void CanDoi(SheetX sh, KhoiNgay b, double?[] suat, Func<string, double> layGiaTri, List<string> canhBao, string khoa)
    {
        sh.Rows.TryGetValue(b.DongSuat - 1, out var dongTien);
        var tien = new double?[] { sh.Val(Lay(dongTien, 8)), sh.Val(Lay(dongTien, 9)), sh.Val(Lay(dongTien, 10)) };
        var khongHoTro = 0;
        var loi0 = khongHoTro;   // công thức không tính được (kể cả ở mục gia vị) => ngày đó chỉ co giãn tỷ lệ
        var giaVi = new double[3];
        foreach (var r in b.M2)
        {
            var c = sh.Rows[r];
            var gia = sh.Val(Lay(c, 7)) ?? 0;
            for (var i = 0; i < 3; i++) giaVi[i] += HieuLuc(sh, c, 4 + i, layGiaTri, ref khongHoTro) * gia;
        }
        for (var i = 0; i < 3; i++)
        {
            if (tien[i] == null || tien[i] == 0 || suat[i] == null || suat[i] == 0) continue;
            var nganSach = tien[i]!.Value * suat[i]!.Value;
            var dsDong = i == 0 ? b.M3 : b.M4;
            var cot = i == 0 ? 4 : (i == 1 ? 4 : 5);
            var cotGia = i == 0 ? 5 : 6;
            var dong = new List<DongChinh>();
            var codinh = 0.0;
            foreach (var r in dsDong)
            {
                var c = sh.Rows[r];
                var gia = sh.Val(Lay(c, cotGia)) ?? 0;
                var o = Lay(c, cot);
                var q = HieuLuc(sh, c, cot, layGiaTri, ref khongHoTro);
                var ten = (sh.Text(Lay(c, 2)) ?? "").ToLowerInvariant();
                var coDinh = o == null || sh.Formula(o) != null || sh.Num(o) == null || q == 0 || ten.Contains("sữa ellac");
                if (coDinh) { codinh += q * gia; continue; }
                var dvt = (sh.Text(Lay(c, 3)) ?? "").Trim().ToLowerInvariant();
                var laGoi = ChiTietNgayTinhToanService.LaDonViGoi(dvt);
                var buoc = (laGoi || (Math.Abs(q - Math.Round(q)) < 1e-9 && dvt != "kg" && dvt != "chai" && dvt != "lít" && dvt != "l")) ? 1.0 : 0.1;
                dong.Add(new DongChinh { Dong = r, Cot = cot, Gia = gia, Luong = q, Buoc = buoc, Goc = q });
            }
            var chenh = nganSach - (giaVi[i] + codinh + dong.Sum(d => d.Gia * d.Luong));
            if (khongHoTro > loi0)
            {
                canhBao.Add($"{khoa} ngày {b.Ngay + 2}: có công thức đặc biệt/lỗi trong file mẫu nên chỉ co giãn theo tỷ lệ, chưa cân đối.");
                continue;
            }
            for (var vong = 0; vong < 4000; vong++)
            {
                DongChinh? tot = null; var tot_d = 0.0; var tot_rel = double.MaxValue;
                foreach (var d in dong)
                {
                    var buoc = chenh > 0 ? d.Buoc : -d.Buoc;
                    if (d.Luong + buoc < 0) continue;
                    if (Math.Abs(chenh - buoc * d.Gia) >= Math.Abs(chenh)) continue;
                    var rel = Math.Abs(d.Luong + buoc - d.Goc) / Math.Max(d.Goc, 1e-9);
                    if (rel < tot_rel) { tot_rel = rel; tot = d; tot_d = buoc; }
                }
                if (tot == null) break;
                tot.Luong = Math.Round(tot.Luong + tot_d, 4);
                chenh -= tot_d * tot.Gia;
            }
            foreach (var d in dong)
                if (Math.Abs(d.Luong - d.Goc) > 1e-9) sh.SetNum(sh.Rows[d.Dong][d.Cot], d.Luong);
        }
    }

    private static double HieuLuc(SheetX sh, Dictionary<int, XElement> hang, int cot, Func<string, double> layGiaTri, ref int khongHoTro)
    {
        var c = Lay(hang, cot);
        if (c == null) return 0.0;
        var f = sh.Formula(c);
        if (f == null) return sh.Val(c) ?? 0.0;
        var v = Fx.Evaluate(f, layGiaTri);
        if (v == null) { khongHoTro++; return sh.Val(c) ?? 0.0; }
        return v.Value;
    }

    // ===================================================== Phát hiện khối ngày =====================================================

    private sealed class KhoiNgay
    {
        public int Ngay; public int DongSuat;
        public List<int> M2 = new(); public List<int> M3 = new(); public List<int> M4 = new();
    }

    private static readonly Regex ThuRx = new(@"Thứ\s+(hai|ba|tư|năm|sáu|bảy),\s*ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int ChiSoThu(string ten)
    {
        switch (ten.ToLowerInvariant())
        {
            case "hai": return 0;
            case "ba": return 1;
            case "tư": return 2;
            case "năm": return 3;
            case "sáu": return 4;
            default: return 5;
        }
    }

    private static List<KhoiNgay> TimKhoi(SheetX sh)
    {
        var kq = new List<KhoiNgay>();
        int mode = 0; KhoiNgay? cur = null; int? ngay = null;
        foreach (var r in sh.Rows.Keys.OrderBy(x => x))
        {
            var hang = sh.Rows[r];
            var j = string.Join(" ", hang.Values.Select(c => sh.Text(c)).Where(t => !string.IsNullOrEmpty(t)));
            var mt = ThuRx.Match(j);
            if (mt.Success) { ngay = ChiSoThu(mt.Groups[1].Value); cur = null; mode = 0; }
            if (j.Contains("Xuất ăn trong ngày") && ngay != null)
            {
                cur = new KhoiNgay { Ngay = ngay.Value, DongSuat = r };
                kq.Add(cur); mode = 0; continue;
            }
            var daDoiMode = false;
            if (j.Contains("2. Theo dõi xuất kho gia vị")) { mode = 2; daDoiMode = true; }
            else if (j.Contains("3. Chi tiền thực phẩm sáng")) { mode = 3; daDoiMode = true; }
            else if (j.Contains("4. Chi tiền thực phẩm ăn chính")) { mode = 4; daDoiMode = true; }
            if (daDoiMode) continue;
            var b = sh.Text(Lay(hang, 2)) ?? "";
            if (b.Trim().StartsWith("tổng", StringComparison.OrdinalIgnoreCase)) { mode = 0; continue; }
            if (cur != null && mode != 0)
            {
                var k = sh.Text(Lay(hang, 11));
                var a = sh.Val(Lay(hang, 1));
                if (!string.IsNullOrWhiteSpace(k) || a != null)
                {
                    if (mode == 4 || sh.Text(Lay(hang, 2)) != null || !string.IsNullOrWhiteSpace(k))
                    {
                        if (mode == 2) cur.M2.Add(r); else if (mode == 3) cur.M3.Add(r); else cur.M4.Add(r);
                    }
                }
            }
        }
        return kq;
    }

    private static XElement? Lay(Dictionary<int, XElement>? hang, int cot)
    {
        if (hang == null) return null;
        return hang.TryGetValue(cot, out var c) ? c : null;
    }

    private static (int Dong, int Cot) TachDiaChi(string diaChi)
    {
        var m = Regex.Match(diaChi, @"^([A-Z]+)(\d+)$");
        if (!m.Success) return (0, 0);
        return (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), CotSo(m.Groups[1].Value));
    }

    internal static int CotSo(string chu)
    {
        var n = 0;
        foreach (var ch in chu.ToUpperInvariant())
            if (ch >= 'A' && ch <= 'Z') n = n * 26 + (ch - 'A' + 1);
        return n;
    }

    // ===================================================== Gói xlsx và sheet =====================================================

    private sealed class XPackage
    {
        public Dictionary<string, byte[]> Files = new();
        public List<string> Order = new();
    }

    private static XPackage DocGoi(byte[] noiDung)
    {
        var goi = new XPackage();
        using var ms = new MemoryStream(noiDung);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            using var buf = new MemoryStream();
            s.CopyTo(buf);
            goi.Files[e.FullName] = buf.ToArray();
            goi.Order.Add(e.FullName);
        }
        return goi;
    }

    private static byte[] GhiGoi(XPackage goi)
    {
        using var ra = new MemoryStream();
        using (var zip = new ZipArchive(ra, ZipArchiveMode.Create, true))
        {
            foreach (var ten in goi.Order)
            {
                var entry = zip.CreateEntry(ten, CompressionLevel.Optimal);
                using var w = entry.Open();
                var b = goi.Files[ten];
                w.Write(b, 0, b.Length);
            }
        }
        return ra.ToArray();
    }

    private static List<string> DocSharedStrings(XPackage goi)
    {
        var ds = new List<string>();
        if (!goi.Files.TryGetValue("xl/sharedStrings.xml", out var b)) return ds;
        var doc = XDocument.Load(new MemoryStream(b));
        foreach (var si in doc.Root!.Elements(M + "si"))
            ds.Add(string.Concat(si.Descendants(M + "t").Select(t => t.Value)));
        return ds;
    }

    private static List<(string Ten, string Duong)> DocSheets(XPackage goi)
    {
        var kq = new List<(string, string)>();
        var wb = XDocument.Load(new MemoryStream(goi.Files["xl/workbook.xml"]));
        var rel = XDocument.Load(new MemoryStream(goi.Files["xl/_rels/workbook.xml.rels"]));
        var map = new Dictionary<string, string>();
        foreach (var r in rel.Root!.Elements(PR + "Relationship"))
            map[(string)r.Attribute("Id")!] = (string)r.Attribute("Target")!;
        foreach (var s in wb.Root!.Descendants(M + "sheet"))
        {
            var id = (string?)s.Attribute(R + "id");
            if (id == null || !map.TryGetValue(id, out var target)) continue;
            target = target.TrimStart('/');
            kq.Add((((string?)s.Attribute("name")) ?? "", target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target));
        }
        return kq;
    }

    private sealed class SheetX
    {
        public XDocument Doc;
        public Dictionary<int, Dictionary<int, XElement>> Rows = new();
        private readonly List<string> _ss;

        public SheetX(byte[] xml, List<string> ss)
        {
            _ss = ss;
            Doc = XDocument.Load(new MemoryStream(xml));
            foreach (var row in Doc.Descendants(M + "row"))
            {
                var r = int.Parse((string)row.Attribute("r")!, CultureInfo.InvariantCulture);
                var d = new Dictionary<int, XElement>();
                foreach (var c in row.Elements(M + "c"))
                {
                    var diaChi = (string?)c.Attribute("r") ?? "";
                    var m = Regex.Match(diaChi, @"^[A-Z]+");
                    if (m.Success) d[CotSo(m.Value)] = c;
                }
                Rows[r] = d;
            }
        }

        public string? Text(XElement? c)
        {
            if (c == null) return null;
            var t = (string?)c.Attribute("t");
            var v = c.Element(M + "v");
            if (t == "s" && v != null && int.TryParse(v.Value, out var i) && i >= 0 && i < _ss.Count) return _ss[i];
            if (t == "str" && v != null) return v.Value;
            if (t == "inlineStr") return string.Concat(c.Descendants(M + "t").Select(x => x.Value));
            return null;
        }

        public double? Val(XElement? c)
        {
            if (c == null) return null;
            var t = (string?)c.Attribute("t");
            var v = c.Element(M + "v");
            if ((t == null || t == "n") && v != null && double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return x;
            return null;
        }

        public double? Num(XElement? c) => (c == null || c.Element(M + "f") != null) ? null : Val(c);

        public string? Formula(XElement? c)
        {
            var f = c?.Element(M + "f");
            return f == null ? null : f.Value;
        }

        public void SetNum(XElement c, double x)
        {
            c.Elements(M + "is").Remove();
            c.Attribute("t")?.Remove();
            var v = c.Element(M + "v");
            if (v == null) { v = new XElement(M + "v"); c.Add(v); }
            v.Value = x.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            Doc.Save(ms);
            return ms.ToArray();
        }
    }

    // ===================================================== Đổi ngày =====================================================

    private static readonly Regex KhoangRx = new(@"(\d{1,2})/(\d{1,2})/(\d{4})(\s*)\((\s*)(\d{1,2})/(\d{1,2})/(\d{4})(\s*-\s*)(\d{1,2})/(\d{1,2})/(\d{4})(\s*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex ThuNgayRx = new(@"(Thứ\s+(hai|ba|tư|năm|sáu|bảy),\s*ngày\s+)(\d{1,2})(\s+tháng\s+)(\d{1,2})(\s+năm\s+)(\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NgayRx = new(@"(ngày\s+)(\d{1,2})(\s+tháng\s+)(\d{1,2})(\s+năm\s+)(\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GiaViRx = new(@"(ĐỊNH LƯỢNG GIA VỊ tuần\s+)(\d{1,2})/(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NghiLeRx = new(@"(NGHỈ LỄ\s+)(\d{1,2})/(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ThuNgay(string d, string m, string y, out DateOnly ngay)
    {
        ngay = default;
        if (!int.TryParse(d, out var dd) || !int.TryParse(m, out var mm) || !int.TryParse(y, out var yy)) return false;
        if (yy < 1900 || yy > 2200 || mm < 1 || mm > 12 || dd < 1 || dd > DateTime.DaysInMonth(yy, mm)) return false;
        ngay = new DateOnly(yy, mm, dd);
        return true;
    }

    private static string Rong(string goc, int v) => goc.Length >= 2 ? v.ToString("00", CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    internal static DateOnly? TimThuHaiNguon(string xml)
    {
        var m = KhoangRx.Match(xml);
        if (m.Success && ThuNgay(m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value, out var d1))
            return LichSuThucDonViewModel.ThuHaiCuaTuan(d1);
        var phieu = new Dictionary<DateOnly, int>();
        foreach (Match n in NgayRx.Matches(xml))
        {
            if (!ThuNgay(n.Groups[2].Value, n.Groups[4].Value, n.Groups[6].Value, out var d)) continue;
            var t = LichSuThucDonViewModel.ThuHaiCuaTuan(d);
            phieu[t] = (phieu.TryGetValue(t, out var so) ? so : 0) + 1;
        }
        if (phieu.Count == 0) return null;
        return phieu.OrderByDescending(x => x.Value).First().Key;
    }

    /// <summary>Một lượt duy nhất: gom các đoạn ngày từ văn bản GỐC (ưu tiên khoảng tuần > "Thứ X, ngày…" > "ngày…" > nhãn),
    /// bỏ đoạn chồng lấn rồi thay từ cuối về đầu — nên một ngày không bao giờ bị đổi hai lần.</summary>
    internal static string ThayNgay(string xml, DateOnly cu, DateOnly moi)
    {
        var (nam, thang) = LichSuThucDonViewModel.ThangCuaTuan(moi);
        var soTuan = Math.Max(1, LichSuThucDonViewModel.CacTuanCuaThang(nam, thang).IndexOf(moi) + 1);

        DateOnly? NgayMoi(DateOnly d0)
        {
            var lech = d0.DayNumber - cu.DayNumber;
            return (lech < -10 || lech > 15) ? null : moi.AddDays(lech);
        }
        string DinhDang(string gd, string gm, DateOnly x) =>
            Rong(gd, x.Day) + "/" + Rong(gm, x.Month) + "/" + x.Year.ToString("0000", CultureInfo.InvariantCulture);

        var cacDoan = new List<(int Bd, int Dai, string Moi)>();
        void ThuThap(Regex rx, Func<Match, string?> xuLy)
        {
            foreach (Match m in rx.Matches(xml))
            {
                var bd = m.Index; var kt = m.Index + m.Length;
                var chong = false;
                foreach (var d in cacDoan) if (bd < d.Bd + d.Dai && kt > d.Bd) { chong = true; break; }
                if (chong) continue;
                var r = xuLy(m);
                if (r != null) cacDoan.Add((bd, m.Length, r));
            }
        }

        ThuThap(KhoangRx, m =>
        {
            if (!ThuNgay(m.Groups[6].Value, m.Groups[7].Value, m.Groups[8].Value, out var d1)) return null;
            if (!ThuNgay(m.Groups[10].Value, m.Groups[11].Value, m.Groups[12].Value, out var d2)) return null;
            var n1 = NgayMoi(d1); var n2 = NgayMoi(d2);
            if (n1 == null || n2 == null) return null;
            var sn = m.Groups[1].Value.Length >= 2 ? soTuan.ToString("00", CultureInfo.InvariantCulture) : soTuan.ToString(CultureInfo.InvariantCulture);
            return sn + "/" + thang.ToString("00", CultureInfo.InvariantCulture) + "/" + nam.ToString("0000", CultureInfo.InvariantCulture)
                + m.Groups[4].Value + "(" + m.Groups[5].Value + DinhDang(m.Groups[6].Value, m.Groups[7].Value, n1.Value)
                + m.Groups[9].Value + DinhDang(m.Groups[10].Value, m.Groups[11].Value, n2.Value) + m.Groups[13].Value + ")";
        });
        ThuThap(ThuNgayRx, m =>
        {
            var x = moi.AddDays(ChiSoThu(m.Groups[2].Value));
            return m.Groups[1].Value + Rong(m.Groups[3].Value, x.Day) + m.Groups[4].Value + Rong(m.Groups[5].Value, x.Month)
                + m.Groups[6].Value + x.Year.ToString("0000", CultureInfo.InvariantCulture);
        });
        ThuThap(NgayRx, m =>
        {
            if (!ThuNgay(m.Groups[2].Value, m.Groups[4].Value, m.Groups[6].Value, out var d0)) return null;
            var x = NgayMoi(d0);
            if (x == null) return null;
            return m.Groups[1].Value + Rong(m.Groups[2].Value, x.Value.Day) + m.Groups[3].Value + Rong(m.Groups[4].Value, x.Value.Month)
                + m.Groups[5].Value + x.Value.Year.ToString("0000", CultureInfo.InvariantCulture);
        });
        ThuThap(GiaViRx, m => m.Groups[1].Value + soTuan.ToString(CultureInfo.InvariantCulture) + "/" + thang.ToString(CultureInfo.InvariantCulture));
        ThuThap(NghiLeRx, m =>
        {
            if (!ThuNgay(m.Groups[2].Value, m.Groups[3].Value, cu.Year.ToString(CultureInfo.InvariantCulture), out var d0)) return null;
            var x = NgayMoi(d0);
            return x == null ? null : m.Groups[1].Value + x.Value.Day.ToString(CultureInfo.InvariantCulture) + "/" + x.Value.Month.ToString(CultureInfo.InvariantCulture);
        });

        var sb = new StringBuilder(xml);
        foreach (var d in cacDoan.OrderByDescending(x => x.Bd))
        {
            sb.Remove(d.Bd, d.Dai);
            sb.Insert(d.Bd, d.Moi);
        }
        return sb.ToString();
    }
}

/// <summary>Bộ tính công thức nhỏ cho các công thức trong file mẫu: số, ô (A1 / $A$1), + - * /, ngoặc, IF, OR, so sánh.
/// Gặp công thức khác (tham chiếu sheet khác, #REF!, hàm lạ) thì trả về null.</summary>
internal sealed class Fx
{
    private static readonly Regex Tok = new(@"\G\s*(?:(\d+(?:\.\d+)?)|(\$?[A-Z]{1,3}\$?\d+)|(IF|OR)\b|(<>|>=|<=|[-+*/(),=<>]))", RegexOptions.CultureInvariant);
    private readonly List<(char K, string S, double N)> _t = new();
    private int _i;
    private readonly Func<string, double> _get;
    private bool _ok = true;

    private Fx(string text, Func<string, double> get)
    {
        _get = get;
        var s = text.Trim();
        var pos = 0;
        while (pos < s.Length)
        {
            var m = Tok.Match(s, pos);
            if (!m.Success || m.Length == 0) { _ok = false; break; }
            if (m.Groups[1].Success) _t.Add(('n', "", double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
            else if (m.Groups[2].Success) _t.Add(('r', m.Groups[2].Value.Replace("$", ""), 0));
            else if (m.Groups[3].Success) _t.Add(('f', m.Groups[3].Value, 0));
            else _t.Add(('o', m.Groups[4].Value, 0));
            pos = m.Index + m.Length;
            while (pos < s.Length && s[pos] == ' ') pos++;
        }
    }

    public static double? Evaluate(string text, Func<string, double> get)
    {
        var f = new Fx(text, get);
        if (!f._ok) return null;
        try
        {
            var v = f.Expr();
            return f._i == f._t.Count ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private (char K, string S, double N) Peek() => _i < _t.Count ? _t[_i] : ('\0', "", 0);

    private (char K, string S, double N) Eat(string? mong = null)
    {
        var p = Peek();
        if (p.K == '\0' || (mong != null && p.S != mong)) throw new InvalidOperationException();
        _i++;
        return p;
    }

    private double Expr()
    {
        var v = Term();
        while (Peek().K == 'o' && (Peek().S == "+" || Peek().S == "-"))
        {
            var o = Eat().S; var r = Term();
            v = o == "+" ? v + r : v - r;
        }
        return v;
    }

    private double Term()
    {
        var v = Fac();
        while (Peek().K == 'o' && (Peek().S == "*" || Peek().S == "/"))
        {
            var o = Eat().S; var r = Fac();
            if (o == "*") v *= r; else v = r == 0 ? 0.0 : v / r;
        }
        return v;
    }

    private bool Cond()
    {
        var a = Expr();
        var p = Peek();
        if (p.K == 'o' && (p.S == "=" || p.S == "<>" || p.S == ">" || p.S == "<" || p.S == ">=" || p.S == "<="))
        {
            Eat();
            var b = Expr();
            switch (p.S)
            {
                case "=": return a == b;
                case "<>": return a != b;
                case ">": return a > b;
                case "<": return a < b;
                case ">=": return a >= b;
                default: return a <= b;
            }
        }
        return a != 0;
    }

    private double Fac()
    {
        var t = Eat();
        if (t.K == 'n') return t.N;
        if (t.K == 'r') return _get(t.S);
        if (t.K == 'o' && t.S == "-") return -Fac();
        if (t.K == 'o' && t.S == "(")
        {
            var x = Expr();
            Eat(")");
            return x;
        }
        if (t.K == 'f' && t.S == "OR")
        {
            Eat("(");
            var res = Cond();
            while (Peek().K == 'o' && Peek().S == ",")
            {
                Eat(",");
                var c = Cond();
                res = res || c;
            }
            Eat(")");
            return res ? 1.0 : 0.0;
        }
        if (t.K == 'f' && t.S == "IF")
        {
            Eat("(");
            var c = Cond();
            Eat(",");
            var a = Expr();
            Eat(",");
            var b = Expr();
            Eat(")");
            return c ? a : b;
        }
        throw new InvalidOperationException();
    }
}
