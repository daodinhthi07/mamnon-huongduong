using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MamNon.ThucDon.Web.ViewModels;

namespace MamNon.ThucDon.Web.Services;

/// <summary>Đọc / ghi file Excel bảng giá. Cột: Mã | TÊN HÀNG | ĐVT | GIÁ BÁN | Ghi chú | (Loại hàng). Cùng dạng với sheet "Bảng giá"
/// trong file mẫu nên có thể tải bảng giá xuống, sửa giá rồi nhập lại.</summary>
public static class BangGiaExcel
{
    public static string BoDau(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var d = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }

    /// <summary>Chuẩn hoá tên để so khớp: bỏ khoảng trắng thừa, chữ thường (giữ nguyên dấu).</summary>
    public static string ChuanTen(string? s) =>
        string.Join(" ", (s ?? "").Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static byte[] Xuat(IEnumerable<BangGiaRowVm> ds)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Bảng giá");
        string[] tieuDe = { "Mã", "TÊN HÀNG", "ĐVT", "GIÁ BÁN", "Ghi chú", "Loại hàng", "Đang dùng" };
        for (var i = 0; i < tieuDe.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = tieuDe[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2E2D2");
        }
        var r = 2;
        foreach (var h in ds)
        {
            ws.Cell(r, 1).Value = h.MaHang ?? "";
            ws.Cell(r, 2).Value = h.TenHang;
            ws.Cell(r, 3).Value = h.Dvt;
            ws.Cell(r, 4).Value = h.DonGia;
            ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(r, 5).Value = h.GhiChu ?? "";
            ws.Cell(r, 6).Value = h.LoaiHang;
            ws.Cell(r, 7).Value = h.IsActive ? "Có" : "Không";
            r++;
        }
        ws.Column(1).Width = 12; ws.Column(2).Width = 48; ws.Column(3).Width = 10;
        ws.Column(4).Width = 14; ws.Column(5).Width = 36; ws.Column(6).Width = 16; ws.Column(7).Width = 11;
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static bool DocGia(IXLCell c, out decimal gia)
    {
        gia = 0;
        if (c.IsEmpty()) return false;
        if (c.DataType == XLDataType.Number && c.TryGetValue<double>(out var d)) { gia = Math.Round((decimal)d, 2); return true; }
        var s = c.GetString().Trim();
        if (s.Length == 0) return false;
        s = s.Replace("đ", "", StringComparison.OrdinalIgnoreCase).Replace("vnd", "", StringComparison.OrdinalIgnoreCase)
             .Replace(" ", "").Replace("\u00A0", "");
        // 140.000 / 140,000 -> dấu phân cách hàng nghìn ; 27.5 / 27,5 -> số thập phân
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,3}([.,]\d{3})+$")) s = s.Replace(".", "").Replace(",", "");
        else s = s.Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out gia);
    }

    /// <summary>Đọc file. Trả về các dòng hợp lệ + danh sách cảnh báo (dòng bỏ qua, thiếu cột...). Lỗi cấu trúc -> ném InvalidOperationException.</summary>
    public static (List<DongBangGiaExcel> Dong, List<string> CanhBao) Doc(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.FirstOrDefault(w => BoDau(w.Name).Contains("bang gia")) ?? wb.Worksheets.First();

        // Tìm dòng tiêu đề (trong 20 dòng đầu): có ô "tên hàng" và ô chứa "giá"
        int hdr = 0, cTen = 0, cDvt = 0, cGia = 0, cMa = 0, cLoai = 0, cGhiChu = 0;
        for (var r = 1; r <= 20 && hdr == 0; r++)
        {
            int ten = 0, gia = 0;
            for (var c = 1; c <= 15; c++)
            {
                var t = BoDau(ws.Cell(r, c).GetString());
                if (t.Length == 0) continue;
                if (ten == 0 && (t == "ten hang" || t.StartsWith("ten hang") || t == "ten")) ten = c;
                if (gia == 0 && t.Contains("gia")) gia = c;
            }
            if (ten > 0 && gia > 0) { hdr = r; cTen = ten; cGia = gia; }
        }
        if (hdr == 0) throw new InvalidOperationException("Không tìm thấy dòng tiêu đề. File cần có cột \"TÊN HÀNG\" và cột giá (\"GIÁ BÁN\"/\"ĐƠN GIÁ\").");
        for (var c = 1; c <= 15; c++)
        {
            var t = BoDau(ws.Cell(hdr, c).GetString());
            if (t == "dvt" || t.StartsWith("don vi")) cDvt = c;
            else if (t == "ma" || t.StartsWith("ma hang") || t == "ma so") cMa = c;
            else if (t.StartsWith("loai")) cLoai = c;
            else if (t.StartsWith("ghi chu") || t == "note" || t.StartsWith("chu thich")) cGhiChu = c;
        }
        // File mẫu: ghi chú (VD "Tăng 10.000đ so với năm trước") nằm ngay sau cột GIÁ BÁN và không có tiêu đề
        if (cGhiChu == 0 && string.IsNullOrWhiteSpace(ws.Cell(hdr, cGia + 1).GetString())) cGhiChu = cGia + 1;
        // File mẫu: cột mã nằm ngay trước "TÊN HÀNG" và không có tiêu đề
        if (cMa == 0 && cTen > 1 && string.IsNullOrWhiteSpace(ws.Cell(hdr, cTen - 1).GetString())) cMa = cTen - 1;

        var dong = new List<DongBangGiaExcel>();
        var canhBao = new List<string>();
        var loaiHienTai = "Thực phẩm tươi";
        var last = ws.LastRowUsed()?.RowNumber() ?? hdr;
        for (var r = hdr + 1; r <= last; r++)
        {
            var ten = ws.Cell(r, cTen).GetString().Trim();
            if (ten.Length == 0) continue;
            var dvt = cDvt > 0 ? ws.Cell(r, cDvt).GetString().Trim() : "";
            var coGia = DocGia(ws.Cell(r, cGia), out var gia);
            if (dvt.Length == 0 && (!coGia || gia == 0))
            {
                // dòng tiêu đề nhóm (VD "Mặt hàng thịt heo", "Gia vị")
                loaiHienTai = BoDau(ten).Contains("gia vi") ? "Gia vị" : "Thực phẩm tươi";
                continue;
            }
            if (!coGia) { canhBao.Add($"Dòng {r}: \"{ten}\" không có giá hợp lệ — bỏ qua."); continue; }
            if (gia < 0) { canhBao.Add($"Dòng {r}: \"{ten}\" có giá âm — bỏ qua."); continue; }
            var loai = loaiHienTai;
            var ghiChu = cGhiChu > 0 ? ws.Cell(r, cGhiChu).GetString().Trim() : "";
            if (cLoai > 0)
            {
                var l = BoDau(ws.Cell(r, cLoai).GetString());
                if (l.Contains("gia vi")) loai = "Gia vị"; else if (l.StartsWith("khac")) loai = "Khác"; else if (l.Contains("thuc pham")) loai = "Thực phẩm tươi";
            }
            dong.Add(new DongBangGiaExcel
            {
                SoDong = r,
                MaHang = cMa > 0 ? ws.Cell(r, cMa).GetString().Trim() : null,
                TenHang = string.Join(" ", ten.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                Dvt = dvt.Length == 0 ? null : dvt,
                LoaiHang = loai,
                DonGia = gia,
                GhiChu = ghiChu.Length == 0 ? null : ghiChu,
            });
        }
        if (dong.Count == 0) throw new InvalidOperationException("Không đọc được dòng giá nào trong file.");
        return (dong, canhBao);
    }
}
