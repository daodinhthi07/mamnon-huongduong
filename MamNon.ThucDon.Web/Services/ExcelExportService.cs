using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

/// <summary>
/// Sinh file Excel (.xlsx) cho 1 ngày của 1 khối, gồm 2 sheet: "Thực đơn"
/// (đúng bố cục bữa × món trong file gốc) và "Chợ" (nhu cầu nguyên liệu +
/// đối chiếu ngân sách, tra giá theo mã hàng, kèm bảng phân bổ định mức và
/// bình quân thực tế mỗi cháu).
/// </summary>
public sealed class ExcelExportService
{
    private readonly ThucDonDbContext _db;
    private readonly NguyenLieuCalculator _nguyenLieuCalc;
    private readonly NganSachNgayCalculator _nganSachCalc;
    private readonly ChiTietNgayTinhToanService _tinhToanSvc;
    private readonly ILogger<ExcelExportService>? _logger;

    public ExcelExportService(
        ThucDonDbContext db,
        NguyenLieuCalculator nguyenLieuCalc,
        NganSachNgayCalculator nganSachCalc,
        ChiTietNgayTinhToanService tinhToanSvc,
        ILogger<ExcelExportService>? logger = null)
    {
        _logger = logger;
        _db = db;
        _nguyenLieuCalc = nguyenLieuCalc;
        _nganSachCalc = nganSachCalc;
        _tinhToanSvc = tinhToanSvc;
    }

    private const string TenDonVi = "UBND PHƯỜNG NGŨ HÀNH SƠN";

    private static string ThuTrongTuan(DateOnly ngay) => ngay.DayOfWeek switch
    {
        DayOfWeek.Monday => "Thứ hai",
        DayOfWeek.Tuesday => "Thứ ba",
        DayOfWeek.Wednesday => "Thứ tư",
        DayOfWeek.Thursday => "Thứ năm",
        DayOfWeek.Friday => "Thứ sáu",
        DayOfWeek.Saturday => "Thứ bảy",
        _ => "Chủ nhật",
    };

    /// <summary>
    /// Xuất ĐÚNG bố cục file Excel gốc cho 1 ngày — 1 workbook gồm nhiều sheet: "Thực đơn"
    /// (món của ngày đó, dùng chung mọi cơ sở) + 1 sheet "Chợ {Mã cơ sở}" cho MỖI cơ sở
    /// active (TC/PH/ĐT — mỗi cơ sở sĩ số/tiền ăn khác nhau nên bảng "Chợ" khác nhau), y
    /// hệt cấu trúc nhiều-sheet của file mẫu (sheet "Thực đơn tuần ok" + "Chợ TC" + "Chợ
    /// PH" + "Chợ ĐT"). Dùng cho nút "Xuất Excel" ở trang Chi tiết ngày.
    ///
    /// Xuất ĐỦ cả tuần (Thứ 2 → Thứ 7) cho mọi cơ sở. Mục 2/3/4 của TỪNG ngày ra y hệt tab
    /// ngày đó trên trang Chi tiết ngày: số ước lượng theo thực đơn + sĩ số đã lưu, hoặc số đã
    /// lưu cố định nếu ngày đó có Sửa/Xóa/phiếu xuất kho. Riêng (cơ sở, ngày) đang mở trên trang
    /// được ghi đè bằng số đang hiện trên màn hình (<paramref name="trenTrang"/>).
    /// </summary>
    public async Task<byte[]> XuatBangKeChoNgayAsync(
        DateOnly ngay, ExcelDuLieuTrenTrang? trenTrang = null, IReadOnlyList<DraftNgayCoSo>? drafts = null, bool canDoi = true)
    {
        drafts ??= new List<DraftNgayCoSo>();
        var db = _db;

        var khoiMauGiao = await db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Mẫu giáo");
        var khoiNhaTre = await db.KhoiHocs.FirstOrDefaultAsync(k => k.TenKhoi == "Nhà trẻ");
        if (khoiMauGiao is null || khoiNhaTre is null)
        {
            throw new InvalidOperationException("Chưa có đủ 2 khối 'Mẫu giáo' và 'Nhà trẻ' — chạy đủ script Database/04, 07 trước.");
        }

        var dsCoSo = await db.CoSos.Where(x => x.IsActive).OrderBy(x => x.MaCoSo).ToListAsync();
        // Thứ tự sheet như file mẫu: TC, PH, ĐT (không phải theo bảng chữ cái của mã cơ sở).
        var thuTuSheetCho = new[] { "TC", "PH", "ĐT" };
        dsCoSo = dsCoSo
            .OrderBy(c => { var i = Array.IndexOf(thuTuSheetCho, c.MaCoSo.Trim().ToUpperInvariant()); return i < 0 ? 99 : i; })
            .ThenBy(c => c.MaCoSo)
            .ToList();

        // Bản nháp chưa lưu (trình duyệt gửi lên) — dựng sẵn bữa ăn/món để thay cho số trong CSDL.
        var dsBuaMg = await db.LoaiBuaAns.Where(b => b.KhoiId == khoiMauGiao.Id).OrderBy(b => b.ThuTu).ToListAsync();
        var dsBuaNt = await db.LoaiBuaAns.Where(b => b.KhoiId == khoiNhaTre.Id).OrderBy(b => b.ThuTu).ToListAsync();
        var monIdTrongDraft = drafts
            .SelectMany(d => (d.MonMauGiao?.Values ?? Enumerable.Empty<int?>()).Concat(d.MonNhaTre?.Values ?? Enumerable.Empty<int?>()))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var monTheoId = (await db.MonAns.Where(m => monIdTrongDraft.Contains(m.Id))
                .Include(m => m.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
                .ToListAsync())
            .ToDictionary(m => m.Id);

        List<ThucDonEntity> XayThucDonTuDraft(int khoiId, List<LoaiBuaAn> dsBua, Dictionary<string, int?> monDaChon) =>
            dsBua.Select(bua =>
            {
                int? monId = monDaChon.TryGetValue(bua.Id.ToString(), out var m) ? m : null;
                return new ThucDonEntity
                {
                    KhoiId = khoiId,
                    LoaiBuaAnId = bua.Id,
                    LoaiBuaAn = bua,
                    MonAnId = monId,
                    MonAn = monId is not null && monTheoId.TryGetValue(monId.Value, out var mon) ? mon : null,
                };
            }).ToList();

        // Món chiều: XẾ Mẫu giáo = CHÍNH CHIỀU Nhà trẻ; bên nào trống thì lấy theo bên kia (bản nháp).
        var xeMgId = dsBuaMg.FirstOrDefault(b => b.TenBua == "XẾ")?.Id.ToString();
        var chinhChieuNtId = dsBuaNt.FirstOrDefault(b => b.TenBua == "CHÍNH CHIỀU")?.Id.ToString();
        Dictionary<string, int?> MonMauGiaoBoSung(DraftNgayCoSo d)
        {
            var mg = new Dictionary<string, int?>(d.MonMauGiao ?? new());
            if (xeMgId is not null && chinhChieuNtId is not null && d.MonNhaTre is not null
                && (!mg.TryGetValue(xeMgId, out var vMg) || vMg is null)
                && d.MonNhaTre.TryGetValue(chinhChieuNtId, out var vNt) && vNt is not null)
                mg[xeMgId] = vNt;
            return mg;
        }

        // Bữa SÁNG của Nhà trẻ luôn giống Mẫu giáo: ép món sáng Nhà trẻ của bản nháp = món sáng Mẫu giáo.
        Dictionary<string, int?> MonNhaTreTheoMauGiao(DraftNgayCoSo d)
        {
            var nt = new Dictionary<string, int?>(d.MonNhaTre ?? new());
            if (xeMgId is not null && chinhChieuNtId is not null && d.MonMauGiao is not null
                && (!nt.TryGetValue(chinhChieuNtId, out var vNt0) || vNt0 is null)
                && d.MonMauGiao.TryGetValue(xeMgId, out var vMg0) && vMg0 is not null)
                nt[chinhChieuNtId] = vMg0;
            var sangMg = dsBuaMg.FirstOrDefault(b => b.TenBua == "SÁNG");
            var sangNt = dsBuaNt.FirstOrDefault(b => b.TenBua == "SÁNG");
            if (sangMg is not null && sangNt is not null && d.MonMauGiao is not null)
                nt[sangNt.Id.ToString()] = d.MonMauGiao.TryGetValue(sangMg.Id.ToString(), out var m) ? m : null;
            return nt;
        }

        DraftNgayCoSo? TimDraft(int coSoId, DateOnly ngayCan) =>
            drafts.FirstOrDefault(d => d.CoSoId == coSoId && d.Ngay == ngayCan);

        using var workbook = new XLWorkbook();

        // ----- Sheet "Thực đơn" — ĐÚNG mẫu "Thực đơn tuần ok" gốc: cả tuần (Thứ 2 → Thứ
        // 7, trường nghỉ Chủ nhật nên không có cột này) chứa ngày đang xuất, món lấy từ cơ
        // sở ĐẦU TIÊN có dữ liệu ngày đó (món giống hệt nhau ở mọi cơ sở — xem Luu()). -----
        var thuHai = ngay.AddDays(-(((int)ngay.DayOfWeek + 6) % 7));
        var cacNgayTrongTuan = Enumerable.Range(0, 6).Select(i => thuHai.AddDays(i)).ToList();

        var duLieuTuan = new List<(DateOnly Ngay, TrangThaiNgay TrangThai, string? GhiChu, List<ThucDonEntity> Mg, List<ThucDonEntity> Nt)>();
        foreach (var ngayTrongTuan in cacNgayTrongTuan)
        {
            NgayHoc? ngayHocTmp = null;
            List<ThucDonEntity> mg = new(), nt = new();

            // Bản nháp chưa lưu của ngày này (món giống nhau ở mọi cơ sở) được ưu tiên hơn số trong CSDL.
            var draftThucDon = drafts.FirstOrDefault(d => d.Ngay == ngayTrongTuan && (d.MonMauGiao is not null || d.MonNhaTre is not null));
            if (draftThucDon is not null)
            {
                mg = XayThucDonTuDraft(khoiMauGiao.Id, dsBuaMg, MonMauGiaoBoSung(draftThucDon));
                nt = XayThucDonTuDraft(khoiNhaTre.Id, dsBuaNt, MonNhaTreTheoMauGiao(draftThucDon));
                var ngayHocDraft = await db.NgayHocs.FirstOrDefaultAsync(n => n.Ngay == ngayTrongTuan);
                duLieuTuan.Add((ngayTrongTuan, ngayHocDraft?.TrangThai ?? TrangThaiNgay.HocBinhThuong, ngayHocDraft?.GhiChu, mg, nt));
                continue;
            }

            foreach (var coSo in dsCoSo)
            {
                ngayHocTmp = await db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == ngayTrongTuan);
                if (ngayHocTmp is null) continue;

                var cacThucDonTmp = await db.ThucDons
                    .Where(t => t.CoSoId == coSo.Id && t.NgayHocId == ngayHocTmp.Id)
                    .Include(t => t.LoaiBuaAn)
                    .Include(t => t.MonAn)
                    .ToListAsync();

                mg = cacThucDonTmp.Where(t => t.KhoiId == khoiMauGiao.Id).OrderBy(t => t.LoaiBuaAn.ThuTu).ToList();
                nt = cacThucDonTmp.Where(t => t.KhoiId == khoiNhaTre.Id).OrderBy(t => t.LoaiBuaAn.ThuTu).ToList();
                if (mg.Count > 0 || nt.Count > 0) break;
            }
            duLieuTuan.Add((ngayTrongTuan, ngayHocTmp?.TrangThai ?? TrangThaiNgay.HocBinhThuong, ngayHocTmp?.GhiChu, mg, nt));
        }
        GhiSheetThucDonTuan(workbook, thuHai, duLieuTuan);

        // Bảng giá: mã hàng (ma_hang, nếu thiếu/trùng thì HH{Id}) -> tên/ĐVT/đơn giá — dùng cho VLOOKUP trong các sheet Chợ.
        var bangGia = new BangGia();
        {
            // Mặt hàng đang hoạt động được ưu tiên giữ mã gốc; mặt hàng đã tắt trùng mã sẽ nhận HH{Id} (tránh "Thịt vai" cũ chiếm mã tv của "Thịt vai có da")
            var dsHang = await db.HangHoas.Include(h => h.Dvt).OrderByDescending(h => h.IsActive).ThenBy(h => h.Id).ToListAsync();
            var daDung = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in dsHang)
            {
                if (ChiTietNgayTinhToanService.LaMatHangCoDinh(h.TenHang)) continue;
                var ma = (h.MaHang ?? "").Trim();
                if (ma.Length == 0 || !daDung.Add(ma)) { ma = $"HH{h.Id}"; daDung.Add(ma); }
                bangGia.Theo[h.Id] = (ma, h.TenHang, h.Dvt?.TenDvt ?? "", h.DonGiaGanNhat ?? 0);
            }
        }

        // Hàng hoá được đánh dấu "nguyên liệu chính" trong công thức món (dồn tiền dư / thiếu vào đây trước).
        var hangChinhDb = (await db.DinhLuongNguyenLieus.AsNoTracking().Where(x => x.LaNguyenLieuChinh).Select(x => x.HangHoaId).Distinct().ToListAsync()).ToHashSet();

        // ----- 1 sheet "Chợ {Mã}" cho mỗi cơ sở active, MỖI sheet xếp lần lượt đủ 6 khối
        // (Thứ 2 → Thứ 7 của tuần chứa ngày đang xuất) — đúng cấu trúc nhiều-khối/sheet
        // của file mẫu (mỗi sheet "Chợ TC/PH/ĐT" gốc có sẵn 6 khối, 1 khối/ngày). -----
        // Gom dữ liệu cả tuần để ghi 2 sheet "Gia vị" (định lượng gia vị tuần) và "Đồ khô" (sĩ số dự kiến) như file mẫu.
        var giaViTuan = new Dictionary<string, Dictionary<string, decimal>>();
        var suatTheoNgay = new Dictionary<(string CoSo, DateOnly Ngay), (int Sang, int Nt, int Mg)>();

        foreach (var coSo in dsCoSo)
        {
            var ws = workbook.Worksheets.Add($"Chợ {coSo.MaCoSo}");
            ws.Style.Font.FontName = "Times New Roman";
            ws.Style.Font.FontSize = 12;
            var doRong = new (string Cot, double Rong)[]
            {
                ("A", 7.9), ("B", 21.1), ("C", 8.7), ("D", 8), ("E", 8.9), ("F", 11.1),
                ("G", 11.4), ("H", 9.6), ("I", 9.6), ("J", 11.2), ("K", 12),
                ("L", 18), ("M", 12), ("N", 12), ("O", 35),
            };
            foreach (var (cot, rong) in doRong) ws.Column(cot).Width = rong;

            // ----- Cài đặt trang in giống file mẫu: A4 dọc, tỷ lệ 85%, lề hẹp, dòng cao 19,8, mỗi NGÀY một trang -----
            ws.RowHeight = 19.8;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.Scale = 85;
            ws.PageSetup.Margins.Left = 0.39;
            ws.PageSetup.Margins.Right = 0;
            ws.PageSetup.Margins.Top = 0.525;
            ws.PageSetup.Margins.Bottom = 0.41;
            ws.PageSetup.Margins.Header = 0;
            ws.PageSetup.Margins.Footer = 0;
            ws.SheetView.View = XLSheetViewOptions.PageLayout;   // mở ở chế độ Page Layout như file mẫu

            var hangKeTiep = 1;
            foreach (var ngayTrongTuan in cacNgayTrongTuan)
            {
                var ngayHoc = await db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == ngayTrongTuan);

                // Include thêm công thức món (DinhLuongNguyenLieus) để mục 3/4 của MỖI ngày trong
                // tuần tính được số ước lượng y hệt số đang hiện ở tab ngày đó trên trang.
                var cacThucDon = ngayHoc is null
                    ? new List<ThucDonEntity>()
                    : await db.ThucDons
                        .Where(t => t.CoSoId == coSo.Id && t.NgayHocId == ngayHoc.Id)
                        .Include(t => t.LoaiBuaAn)
                        .Include(t => t.MonAn)
                            .ThenInclude(m => m!.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
                        .ToListAsync();

                var thucDonMg = cacThucDon.Where(t => t.KhoiId == khoiMauGiao.Id).OrderBy(t => t.LoaiBuaAn.ThuTu).ToList();
                var thucDonNt = cacThucDon.Where(t => t.KhoiId == khoiNhaTre.Id).OrderBy(t => t.LoaiBuaAn.ThuTu).ToList();

                var dinhMucCacNhom = await db.DinhMucAnNgays
                    .Include(d => d.NhomChiPhi)
                    .Where(d => d.CoSoId == coSo.Id && d.Ngay == ngayTrongTuan)
                    .ToListAsync();
                var dmAnSang = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Ăn sáng");
                var dmMauGiao = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Mẫu giáo");
                var dmNhaTre = dinhMucCacNhom.FirstOrDefault(d => d.NhomChiPhi.TenNhom == "Nhà trẻ");

                // Bản nháp chưa lưu của đúng (cơ sở, ngày) này (nếu có) thay cho số trong CSDL:
                // món đã chọn, sĩ số, tiền ăn/cháu.
                var draft = TimDraft(coSo.Id, ngayTrongTuan);
                if (draft is not null)
                {
                    if (draft.MonMauGiao is not null) thucDonMg = XayThucDonTuDraft(khoiMauGiao.Id, dsBuaMg, MonMauGiaoBoSung(draft));
                    if (draft.MonNhaTre is not null) thucDonNt = XayThucDonTuDraft(khoiNhaTre.Id, dsBuaNt, MonNhaTreTheoMauGiao(draft));

                    DinhMucAnNgay? GopDinhMuc(DinhMucAnNgay? daLuu, int? soSuat, decimal? tien, decimal tienMacDinh) =>
                        soSuat is null && tien is null
                            ? daLuu
                            : new DinhMucAnNgay
                            {
                                CoSoId = coSo.Id,
                                Ngay = ngayTrongTuan,
                                SoSuatAn = soSuat ?? daLuu?.SoSuatAn ?? 0,
                                TienAn1Chau = tien ?? daLuu?.TienAn1Chau ?? tienMacDinh,
                            };
                    dmAnSang = GopDinhMuc(dmAnSang, draft.SoSuatAnSang, draft.TienAnSang, 10000);
                    dmMauGiao = GopDinhMuc(dmMauGiao, draft.SoSuatMauGiao, draft.TienMauGiao, 29000);
                    dmNhaTre = GopDinhMuc(dmNhaTre, draft.SoSuatNhaTre, draft.TienNhaTre, 29000);
                }

                // Mục 2/3/4 của TỪNG ngày trong tuần ra đúng như tab ngày đó đang hiện trên trang:
                //  - Mục 2: định mức × sĩ số, đè bằng phiếu gia vị đã Sửa/Xóa (LayGiaViThatAsync).
                //  - Mục 3/4: số ước lượng theo thực đơn đã lưu × sĩ số đã lưu; nếu ngày đó đã có số
                //    lưu cố định (Sửa/Xóa hoặc phiếu xuất kho thật) thì dùng số đã lưu thay thế.
                var giaViThat = await _tinhToanSvc.LayGiaViThatAsync(
                    coSo.Id, ngayTrongTuan, dmAnSang?.SoSuatAn, dmNhaTre?.SoSuatAn, dmMauGiao?.SoSuatAn,
                    sangLaChao: thucDonMg.Any(t => t.LoaiBuaAn.TenBua == "SÁNG" && t.MonAn != null
                        && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)),
                    xeChaoNhaTre: thucDonNt.Any(t => t.LoaiBuaAn.TenBua != "SÁNG" && t.MonAn != null
                        && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)),
                    xeChaoMauGiao: thucDonMg.Any(t => t.LoaiBuaAn.TenBua != "SÁNG" && t.MonAn != null
                        && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)));

                var buaSang = thucDonMg.Where(t => t.LoaiBuaAn.TenBua == "SÁNG")
                    .Select(t => (t, dmAnSang?.SoSuatAn ?? 0));
                var ketQuaSang = _nguyenLieuCalc.TinhNhuCauNguyenLieu(buaSang, maCoSo: coSo.MaCoSo);
                var ketQuaMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
                    thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, dmMauGiao?.SoSuatAn ?? 0)), boSungTrangMieng: 5, maCoSo: coSo.MaCoSo);   // tráng miệng Mẫu giáo: số trẻ + 5 (đúng file mẫu: =J7+5)
                var ketQuaNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
                    thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, dmNhaTre?.SoSuatAn ?? 0)), boSungTrangMieng: 1, maCoSo: coSo.MaCoSo);   // tráng miệng Nhà trẻ: số trẻ + 1 (đúng file mẫu: =I7+1)
                var laCoSoTc = string.Equals(coSo.MaCoSo.Trim(), "TC", StringComparison.OrdinalIgnoreCase);
                // Nhu cầu nguyên liệu TÁCH THEO TỪNG MÓN (không gộp giữa các món) — để nhận ra phần của món Súp (Tinh) và tách mục 4 thành từng món.
                var nhuCauMonMg = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
                    thucDonMg.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, dmMauGiao?.SoSuatAn ?? 0)), boSungTrangMieng: 5, tachTheoMon: true, maCoSo: coSo.MaCoSo);
                var nhuCauMonNt = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
                    thucDonNt.Where(t => t.LoaiBuaAn.TenBua != "SÁNG").Select(t => (t, dmNhaTre?.SoSuatAn ?? 0)), boSungTrangMieng: 1, tachTheoMon: true, maCoSo: coSo.MaCoSo);
                var kq = _tinhToanSvc.Tinh(ketQuaSang, ketQuaNt, ketQuaMg,
                    dmNhaTre?.SoSuatAn ?? 0, dmMauGiao?.SoSuatAn ?? 0, maCoSo: coSo.MaCoSo, soSuatAnSang: dmAnSang?.SoSuatAn ?? 0,
                    sangLaChao: thucDonMg.Any(t => t.LoaiBuaAn.TenBua == "SÁNG" && t.MonAn != null && t.MonAn.TenMon.StartsWith("Cháo", StringComparison.OrdinalIgnoreCase)),
                    nhuCauTheoMonNt: nhuCauMonNt, nhuCauTheoMonMg: nhuCauMonMg, ngay: ngayTrongTuan);
                var uocLuongChinh = kq.Chinh.ToList();   // số ước lượng theo thực đơn — để so với số đã lưu cố định (phát hiện phiếu cũ)

                var thucPhamThat = await _tinhToanSvc.LayThucPhamThatAsync(coSo.Id, ngayTrongTuan, coSo.MaCoSo);
                if (thucPhamThat.CoPhieuSang)
                {
                    kq.Sang = thucPhamThat.Sang;
                    kq.TongTienSang = thucPhamThat.TongTienSang;
                }
                if (thucPhamThat.CoPhieuChinhXe)
                {
                    kq.Chinh = ChiTietNgayTinhToanService.SapXepTheoThucDon(thucPhamThat.Chinh, uocLuongChinh);
                    kq.TongTienChinhNhaTre = thucPhamThat.TongTienChinhNhaTre;
                    kq.TongTienChinhMauGiao = thucPhamThat.TongTienChinhMauGiao;
                }

                // Số liệu đang hiện TRÊN TRANG của đúng (cơ sở, ngày) đang xem -> dùng thay cho số
                // đọc từ CSDL, để Excel ra y hệt màn hình (kể cả số vừa gõ chưa bấm Lưu).
                if (trenTrang is not null && trenTrang.CoSoId == coSo.Id && trenTrang.Ngay == ngayTrongTuan)
                {
                    if (trenTrang.GiaVi is not null)
                        giaViThat = new GiaViThatDto { DanhSach = trenTrang.GiaVi.OrderBy(x => x.TenHang).ToList(), CoPhieu = true };
                    if (trenTrang.Sang is not null)
                    {
                        kq.Sang = trenTrang.Sang.ToList();   // giữ đúng thứ tự trên màn hình (theo thứ tự món)
                        kq.TongTienSang = kq.Sang.Sum(x => x.ThanhTien);
                    }
                    if (trenTrang.Chinh is not null)
                    {
                        kq.Chinh = trenTrang.Chinh.ToList();   // giữ đúng thứ tự trên màn hình (theo thứ tự món)
                        kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
                        kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
                    }
                }

                // Tách nguyên liệu dùng chung giữa nhiều món ("Canh A + Gà sốt B + Cháo C") thành từng dòng riêng cho từng món.
                _tinhToanSvc.TachTheoMon(kq, nhuCauMonNt, nhuCauMonMg, coSo.MaCoSo);

                if (laCoSoTc)
                {
                    var hanhTimGiaVi = giaViThat.DanhSach
                        .Where(x => x.TenHang.StartsWith("Hành tím", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var nhomHanh in hanhTimGiaVi.GroupBy(x => x.HangHoaId))
                    {
                        var h = nhomHanh.First();
                        var index = kq.Chinh.FindIndex(x => x.HangHoaId == nhomHanh.Key
                            || string.Equals(x.TenHang, h.TenHang, StringComparison.OrdinalIgnoreCase));
                        var dongHanh = new NguyenLieuHaiCotDto(
                            h.TenHang, h.Dvt,
                            nhomHanh.Sum(x => x.SlNhaTre), nhomHanh.Sum(x => x.SlMauGiao), h.DonGia,
                            nhomHanh.Sum(x => x.TtNhaTre), nhomHanh.Sum(x => x.TtMauGiao), h.HangHoaId,
                            index >= 0 ? kq.Chinh[index].ThuTuMon : int.MaxValue,
                            index >= 0 ? kq.Chinh[index].TenMon : null);

                        if (index >= 0)
                        {
                            if (!thucPhamThat.CoPhieuChinhXe)
                                kq.Chinh[index] = dongHanh;
                        }
                        else if (dongHanh.SlNhaTre != 0 || dongHanh.SlMauGiao != 0)
                        {
                            kq.Chinh.Add(dongHanh);
                        }
                    }

                    kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
                    kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
                }
                giaViThat.DanhSach = giaViThat.DanhSach
                    .Where(x => !ChiTietNgayTinhToanService.LaHanhRauThom(x.TenHang))
                    .ToList();

                // "Chênh lệch cuối ngày" = 0 như file mẫu: file mẫu do nhân viên chỉnh tay số lượng cho tổng chi
                // = đúng định mức. Ở đây tự cân bằng bằng cách co giãn đều số lượng thực phẩm của từng nhóm
                // (Ăn sáng / Nhà trẻ / Mẫu giáo); gia vị giữ nguyên theo định mức. Bỏ qua mục đã có số lưu
                // cố định (Sửa/Xóa hoặc phiếu xuất kho thật) và nhóm lệch quá lớn (thiếu món) để không che lỗi.
                // Gạo xuất gia vị theo quy tắc cố định của bảng kê (ăn sáng 0; Nhà trẻ 0,055 kg/suất; Mẫu giáo 0,065 kg/suất) — đặt NGAY
                // TRƯỚC khi cân đối để cân đối tính đúng số gạo sẽ ghi ra Excel (trước đây Excel ghi đè gạo SAU khi cân => dư tiền).
                if (canDoi)
                    CanDoiTheoDinhMuc(kq, giaViThat, dmAnSang, dmNhaTre, dmMauGiao,
                        canSang: !thucPhamThat.CoPhieuSang, canChinh: !thucPhamThat.CoPhieuChinhXe,
                        oTrongNhaTre: string.Join(", ", dsBuaNt.Where(b => b.TenBua != "SÁNG" && b.TenBua != "XÀO" && b.TenBua != "XẾ" && !thucDonNt.Any(t => t.LoaiBuaAnId == b.Id && t.MonAnId != null)).Select(b => b.TenBua)),
                        oTrongMauGiao: string.Join(", ", dsBuaMg.Where(b => b.TenBua != "SÁNG" && b.TenBua != "XÀO" && !thucDonMg.Any(t => t.LoaiBuaAnId == b.Id && t.MonAnId != null)).Select(b => b.TenBua)),
                        monManNhaTre: thucDonNt.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "MẶN")?.MonAn?.TenMon,
                        monManMauGiao: thucDonMg.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "MẶN")?.MonAn?.TenMon,
                        tenMonXeNhaTre: thucDonNt.Where(t => t.LoaiBuaAn.TenBua is "XẾ" or "CHÍNH CHIỀU" && t.MonAn != null).Select(t => ChiTietNgayTinhToanService.TenMonHienThi(t.MonAn!.TenMon)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        tenMonXeMauGiao: thucDonMg.Where(t => t.LoaiBuaAn.TenBua is "XẾ" or "CHÍNH CHIỀU" && t.MonAn != null).Select(t => ChiTietNgayTinhToanService.TenMonHienThi(t.MonAn!.TenMon)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        tenMonTrangMiengNhaTre: thucDonNt.Where(t => t.LoaiBuaAn.TenBua == "TRÁNG MIỆNG" && t.MonAn != null).Select(t => ChiTietNgayTinhToanService.TenMonHienThi(t.MonAn!.TenMon)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        tenMonTrangMiengMauGiao: thucDonMg.Where(t => t.LoaiBuaAn.TenBua == "TRÁNG MIỆNG" && t.MonAn != null).Select(t => ChiTietNgayTinhToanService.TenMonHienThi(t.MonAn!.TenMon)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        soSuatNhaTre: dmNhaTre?.SoSuatAn ?? 0, soSuatMauGiao: dmMauGiao?.SoSuatAn ?? 0);

                if (thucPhamThat.CoPhieuChinhXe)
                {
                    var canhBaoCu = ChiTietNgayTinhToanService.PhatHienPhieuCu(uocLuongChinh, thucPhamThat.Chinh);
                    if (canhBaoCu is not null)
                        kq.GhiChuCanDoi = string.IsNullOrEmpty(kq.GhiChuCanDoi) ? canhBaoCu : kq.GhiChuCanDoi + " | " + canhBaoCu;
                }

                // BƯỚC CUỐI: còn dư tiền (Chênh lệch cuối ngày > 0) thì cộng thêm gạo 0,5 kg cho Nhà trẻ / Mẫu giáo rồi phân bổ
                // phần còn lại vào số lượng thực phẩm (nhích từng 0,1 kg) và dồn phần lẻ cuối vào muối => Chênh lệch = 0.
                // Không đụng vào ngày đã có số lưu cố định (phiếu xuất kho thật) hoặc số đang hiện trên trang.
                var dangHienTrenTrang = trenTrang is not null && trenTrang.CoSoId == coSo.Id && trenTrang.Ngay == ngayTrongTuan;
                if (canDoi && !dangHienTrenTrang && !giaViThat.CoPhieu)
                    SanVeKhong(kq, giaViThat, dmAnSang, dmNhaTre, dmMauGiao,
                        canSang: !thucPhamThat.CoPhieuSang, canChinh: !thucPhamThat.CoPhieuChinhXe,
                        monManNhaTre: thucDonNt.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "MẶN")?.MonAn?.TenMon,
                        monManMauGiao: thucDonMg.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "MẶN")?.MonAn?.TenMon,
                        hangChinhDb: hangChinhDb);

                // Bảng kê không hiện dòng toàn số 0 (mã hàng trùng loại / dòng đã xóa) cho gọn như file mẫu.
                kq.Sang = kq.Sang
                    .Where(x => !ChiTietNgayTinhToanService.LaHanhBiAnTaiTc(x.TenHang, coSo.MaCoSo))
                    .Where(x => x.SoLuong != 0)
                    .ToList();
                // Hành tím, hành ngò, hành tây, ngò (nếu có) luôn nằm DƯỚI CÙNG nguyên liệu ăn sáng (mục 3), dù số lấy từ màn hình hay phiếu đã lưu.
                kq.Sang = ChiTietNgayTinhToanService.XepHanhNgoXuongCuoiAnSang(kq.Sang);
                kq.TongTienSang = kq.Sang.Sum(x => x.ThanhTien);
                kq.Chinh = kq.Chinh
                    .Where(x => !ChiTietNgayTinhToanService.LaHanhBiAnTaiTc(x.TenHang, coSo.MaCoSo))
                    .Where(x => x.SlNhaTre != 0 || x.SlMauGiao != 0)
                    .ToList();
                kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
                kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
                giaViThat.DanhSach = giaViThat.DanhSach.Where(x => x.SlAnSang != 0 || x.SlNhaTre != 0 || x.SlMauGiao != 0).ToList();

                suatTheoNgay[(coSo.MaCoSo.Trim(), ngayTrongTuan)] = (dmAnSang?.SoSuatAn ?? 0, dmNhaTre?.SoSuatAn ?? 0, dmMauGiao?.SoSuatAn ?? 0);
                CongGiaViTuan(giaViTuan, coSo.MaCoSo.Trim(), giaViThat, kq);

                bangGia.BoiCanh = $"{coSo.MaCoSo.Trim()} {ngayTrongTuan:dd/MM}";
                hangKeTiep = GhiKhoiChoMotNgay(ws, hangKeTiep, coSo.TenCoSo, ngayTrongTuan, thucDonMg, thucDonNt, dmAnSang, dmMauGiao, dmNhaTre, kq, giaViThat, bangGia, coSo.MaCoSo);
                // Mỗi ngày bắt đầu một trang mới (ngắt trang sau khối của ngày này, trừ ngày cuối).
                if (ngayTrongTuan != cacNgayTrongTuan.Last())
                    ws.PageSetup.AddHorizontalPageBreak(hangKeTiep - 1);
            }

            // Chỉ in cột A:J (cột K–O là mã hàng, bảng phụ và dòng cảnh báo "Chưa cân đối").
            ws.PageSetup.PrintAreas.Add($"A1:J{Math.Max(1, hangKeTiep - 1)}");
        }

        // Kiểm tra bảng giá TRƯỚC khi ghi file: có nguyên liệu không có trong Bảng giá (hoặc chưa có giá) thì dừng và báo cụ thể.
        var dongThieu = bangGia.DongThieu();
        if (dongThieu.Count > 0) throw new ThieuBangGiaException(dongThieu);

        GhiSheetBangGia(workbook, bangGia);
        GhiSheetGiaVi(workbook, thuHai, giaViTuan);
        GhiSheetDoKho(workbook, thuHai, suatTheoNgay);

        // Tính sẵn kết quả công thức (VLOOKUP, thành tiền, tổng cộng, "Số tiền được phép chi"...) để file mở ở chế độ chỉ-xem
        // (Protected View / xem trước / điện thoại) vẫn thấy đủ số theo số suất vừa nhập, không phải bấm "Enable Editing" mới có.
        try { workbook.RecalculateAllFormulas(); }
        catch (Exception ex)
        {
            // Không tính trước được thì Excel vẫn tự tính khi mở file; nhưng chế độ xem trước / Protected View sẽ trống số -> ghi log để biết.
            _logger?.LogWarning(ex, "Không tính sẵn được kết quả công thức khi xuất bảng kê chợ tuần {Tuan}", thuHai);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return DoiBangMauThemeGiongFileMau(stream.ToArray());
    }

    /// <summary>Bảng giá để các dòng trong bảng kê TRA CỨU bằng VLOOKUP theo mã hàng (cột K) giống file mẫu:
    /// đổi giá ở sheet "Bảng giá" thì đơn giá + thành tiền + tổng chi + chênh lệch tự tính lại.</summary>
    private sealed class BangGia
    {
        public const string TenSheet = "Bảng giá";
        public const string VungTra = "'Bảng giá'!$A$3:$D$3000";
        public Dictionary<int, (string Ma, string Ten, string Dvt, decimal Gia)> Theo { get; } = new();

        /// <summary>Nơi đang ghi (VD "TC 28/10") — gán trước khi ghi từng khối ngày, để báo lỗi chỉ rõ nguyên liệu thiếu nằm ở đâu.</summary>
        public string BoiCanh { get; set; } = "";
        private readonly Dictionary<string, (string Ten, string Dvt, string LyDo, SortedSet<string> Cho)> _thieu = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mỗi nguyên liệu thiếu 1 câu: tên (ĐVT) — lý do — ở đâu dùng.</summary>
        public List<string> DongThieu() => _thieu.Values.OrderBy(v => v.Ten, StringComparer.CurrentCultureIgnoreCase)
            .Select(v => $"{v.Ten}{(string.IsNullOrWhiteSpace(v.Dvt) ? "" : $" ({v.Dvt})")} — {v.LyDo} — dùng ở: "
                + string.Join(", ", v.Cho.Take(6)) + (v.Cho.Count > 6 ? $", … (+{v.Cho.Count - 6})" : "")).ToList();

        private void KiemTra(int hangHoaId, string ten, string? dvt, decimal gia)
        {
            if (string.IsNullOrWhiteSpace(ten) || ChiTietNgayTinhToanService.LaMatHangCoDinh(ten)) return;
            string? lyDo = null;
            if (hangHoaId > 0 && !Theo.ContainsKey(hangHoaId)) lyDo = "không có trong Bảng giá";
            else if (hangHoaId > 0 && Theo[hangHoaId].Gia <= 0) lyDo = "chưa có đơn giá trong Bảng giá";
            else if (hangHoaId <= 0 && gia <= 0) lyDo = "không có trong Bảng giá";
            if (lyDo is null) return;
            var khoa = hangHoaId > 0 ? "#" + hangHoaId : ten.Trim();
            if (!_thieu.TryGetValue(khoa, out var v)) { v = (ten.Trim(), dvt ?? "", lyDo, new SortedSet<string>(StringComparer.Ordinal)); _thieu[khoa] = v; }
            if (BoiCanh.Length > 0) v.Cho.Add(BoiCanh);
        }

        /// <summary>Chỉ dùng VLOOKUP khi bảng giá cho ra ĐÚNG tên/ĐVT/đơn giá đang hiện — nếu khác (VD số đã lưu
        /// cố định theo giá cũ) thì để số cứng để tổng tiền không bị đổi ngầm.</summary>
        public string? MaNeuTraDuoc(int hangHoaId, string ten, string? dvt, decimal gia)
        {
            KiemTra(hangHoaId, ten, dvt, gia);
            return hangHoaId > 0 && Theo.TryGetValue(hangHoaId, out var h)
                && h.Gia == gia && h.Ten == ten && string.Equals(h.Dvt ?? "", dvt ?? "", StringComparison.Ordinal)
                    ? h.Ma : null;
        }
    }

    private static void GhiMaVaTra(IXLWorksheet ws, int hang, string ma, int cotTen, int cotDvt, int cotGia)
    {
        ws.Cell(hang, 11).Value = ma;
        ws.Cell(hang, 11).Style.Font.FontSize = 8;
        ws.Cell(hang, 11).Style.Font.FontColor = XLColor.FromHtml("#9A9486");
        ws.Cell(hang, cotTen).FormulaA1 = $"=VLOOKUP($K{hang},{BangGia.VungTra},2,0)";
        ws.Cell(hang, cotDvt).FormulaA1 = $"=VLOOKUP($K{hang},{BangGia.VungTra},3,0)";
        ws.Cell(hang, cotGia).FormulaA1 = $"=VLOOKUP($K{hang},{BangGia.VungTra},4,0)";
    }

    private static void GhiSheetBangGia(XLWorkbook workbook, BangGia bg)
    {
        var ws = workbook.Worksheets.Add(BangGia.TenSheet);
        ws.Cell(1, 1).Value = "BẢNG GIÁ (tra cứu bằng mã hàng — đổi giá ở cột D, các sheet Chợ tự tính lại)";
        ws.Cell(1, 1).Style.Font.Bold = true;
        var tieuDe = new[] { "Mã hàng", "Tên hàng", "ĐVT", "Đơn giá" };
        for (var i = 0; i < tieuDe.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = tieuDe[i]; c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2E2D2");
        }
        var r = 3;
        foreach (var h in bg.Theo.Values.OrderBy(x => x.Ten))
        {
            ws.Cell(r, 1).Value = h.Ma;
            ws.Cell(r, 2).Value = h.Ten;
            ws.Cell(r, 3).Value = h.Dvt;
            ws.Cell(r, 4).Value = h.Gia;
            ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0";
            r++;
        }
        ws.Column(1).Width = 14; ws.Column(2).Width = 60; ws.Column(3).Width = 8; ws.Column(4).Width = 12;
        ws.SheetView.FreezeRows(2);
    }

    private static readonly (string Ten, decimal NhaTre, decimal MauGiao)[] PhanBoDinhMuc =
    {
        ("Canh", 3000m, 3000m),
        ("Mặn", 14730m, 12990m),
        ("Xào", 0m, 1600m),
        ("Gạo", 770m, 910m),
        ("Gia vị", 750m, 750m),
        ("Tráng miệng", 2250m, 2250m),   // bảng tỷ lệ chuẩn: tổng Nhà trẻ / Mẫu giáo = 29.000đ (3.000+14.730/12.990+0/1.600+770/910+750+2.250+4.500+3.000)
        ("Xế", 4500m, 4500m),
        ("Sữa", 3000m, 3000m),
    };

    private static string CongDongCongThuc(IEnumerable<int> hang, int cot)
    {
        var cacHang = hang.Distinct().Order().ToArray();
        if (cacHang.Length == 0) return "0";

        var vung = new List<string>();
        var batDau = cacHang[0];
        var cuoi = batDau;
        foreach (var hienTai in cacHang.Skip(1))
        {
            if (hienTai == cuoi + 1)
            {
                cuoi = hienTai;
                continue;
            }

            vung.Add(batDau == cuoi ? $"{XLHelper.GetColumnLetterFromNumber(cot)}{batDau}" : $"{XLHelper.GetColumnLetterFromNumber(cot)}{batDau}:{XLHelper.GetColumnLetterFromNumber(cot)}{cuoi}");
            batDau = cuoi = hienTai;
        }

        vung.Add(batDau == cuoi ? $"{XLHelper.GetColumnLetterFromNumber(cot)}{batDau}" : $"{XLHelper.GetColumnLetterFromNumber(cot)}{batDau}:{XLHelper.GetColumnLetterFromNumber(cot)}{cuoi}");
        return $"SUM({string.Join(",", vung)})";
    }

    private static string CongThucBinhQuan(IEnumerable<int> hang, int cotThanhTien, int cotSiSo, int hangSiSo)
    {
        var tong = CongDongCongThuc(hang, cotThanhTien);
        var cotSiSoChu = XLHelper.GetColumnLetterFromNumber(cotSiSo);
        return tong == "0"
            ? "=0"
            : $"=IF(${cotSiSoChu}${hangSiSo}=0,0,{tong}/${cotSiSoChu}${hangSiSo})";
    }

    private static void GhiBangPhanTich(
        IXLWorksheet ws,
        int hangBatDau,
        int hangSiSo,
        int hangTongGiaVi,
        IReadOnlyList<(int Hang, string TenHang, string? TenMon)> dongGiaVi,
        IReadOnlyList<(int Hang, string TenHang, string? TenMon)> dongThucPham,
        IReadOnlyDictionary<string, string> nhomMon)
    {
        static string? Nhom(string? tenMon, IReadOnlyDictionary<string, string> nhomMon)
        {
            if (string.IsNullOrWhiteSpace(tenMon)) return null;
            foreach (var mon in tenMon.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var tenMonChuanHoa = ChiTietNgayTinhToanService.TenMonHienThi(mon);
                if (nhomMon.TryGetValue(tenMonChuanHoa, out var nhom)) return nhom;
            }
            return null;
        }

        static bool LaHanhTim(string tenHang) =>
            tenHang.StartsWith("Hành tím", StringComparison.OrdinalIgnoreCase);

        var canh = dongThucPham
            .Where(x => Nhom(x.TenMon, nhomMon) == "Canh" || LaHanhTim(x.TenHang))
            .Select(x => x.Hang);
        var man = dongThucPham
            .Where(x => Nhom(x.TenMon, nhomMon) == "Mặn" && !LaHanhTim(x.TenHang))
            .Select(x => x.Hang);
        var xao = dongThucPham.Where(x => Nhom(x.TenMon, nhomMon) == "Xào").Select(x => x.Hang);
        var trangMieng = dongThucPham.Where(x => Nhom(x.TenMon, nhomMon) == "Tráng miệng").Select(x => x.Hang);
        var xe = dongThucPham.Where(x => Nhom(x.TenMon, nhomMon) == "Xế"
            && !ChiTietNgayTinhToanService.LaMatHangCoDinh(x.TenHang)).Select(x => x.Hang);
        var sua = dongThucPham.Where(x => ChiTietNgayTinhToanService.LaMatHangCoDinh(x.TenHang)).Select(x => x.Hang);
        var gao = dongGiaVi.Where(x => x.TenHang.Contains("Gạo", StringComparison.OrdinalIgnoreCase)).Select(x => x.Hang);
        var hanhTim = dongGiaVi.Where(x => LaHanhTim(x.TenHang)).Select(x => x.Hang);

        var dongNhom = new Dictionary<string, IEnumerable<int>>(StringComparer.Ordinal)
        {
            ["Canh"] = canh,
            ["Mặn"] = man,
            ["Xào"] = xao,
            ["Gạo"] = gao,
            ["Gia vị"] = Array.Empty<int>(),
            ["Tráng miệng"] = trangMieng,
            ["Xế"] = xe,
            ["Sữa"] = sua,
        };

        var coNhoRuby = dongThucPham.Any(x => Nhom(x.TenMon, nhomMon) == "Tráng miệng"
            && ChiTietNgayTinhToanService.TenMonHienThi(x.TenMon).Trim().StartsWith("Nho Ruby", StringComparison.OrdinalIgnoreCase));
        var cotTen = 12;
        var cotNhaTre = 13;
        var cotMauGiao = 14;
        var hangBangDinhMuc = hangBatDau;
        var hangBangThucTe = hangBatDau + 12;

        void TaoBang(int hang, string tieuDe, bool laBangThucTe)
        {
            ws.Range(hang, cotTen, hang, cotMauGiao).Merge();
            ws.Cell(hang, cotTen).Value = tieuDe;
            ws.Cell(hang, cotTen).Style.Font.Bold = true;
            ws.Cell(hang, cotTen).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(hang, cotTen, hang, cotMauGiao).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2E2D2");

            var hangTieuDe = hang + 1;
            ws.Cell(hangTieuDe, cotTen).Value = "Hạng mục";
            ws.Cell(hangTieuDe, cotNhaTre).Value = "Nhà trẻ";
            ws.Cell(hangTieuDe, cotMauGiao).Value = "Mẫu giáo";
            ws.Range(hangTieuDe, cotTen, hangTieuDe, cotMauGiao).Style.Font.Bold = true;
            ws.Range(hangTieuDe, cotTen, hangTieuDe, cotMauGiao).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E3DC");

            var r = hang + 2;
            foreach (var (ten, nhaTre, mauGiao) in PhanBoDinhMuc)
            {
                ws.Cell(r, cotTen).Value = ten;
                if (laBangThucTe)
                {
                    var hangNguon = dongNhom[ten];
                    if (ten == "Gia vị")
                    {
                        var hangGao = dongNhom["Gạo"];
                        var tongGaoNt = CongDongCongThuc(hangGao, 9);
                        var tongGaoMg = CongDongCongThuc(hangGao, 10);
                        var tongHanhTimNt = CongDongCongThuc(hanhTim, 9);
                        var tongHanhTimMg = CongDongCongThuc(hanhTim, 10);
                        ws.Cell(r, cotNhaTre).FormulaA1 = $"=IF($I${hangSiSo}=0,0,(I{hangTongGiaVi}-{tongGaoNt}-{tongHanhTimNt})/$I${hangSiSo})";
                        ws.Cell(r, cotMauGiao).FormulaA1 = $"=IF($J${hangSiSo}=0,0,(J{hangTongGiaVi}-{tongGaoMg}-{tongHanhTimMg})/$J${hangSiSo})";
                    }
                    else if (ten == "Canh")
                    {
                        var tongCanhNt = $"{CongDongCongThuc(hangNguon, 7)}+{CongDongCongThuc(hanhTim, 9)}";
                        var tongCanhMg = $"{CongDongCongThuc(hangNguon, 8)}+{CongDongCongThuc(hanhTim, 10)}";
                        ws.Cell(r, cotNhaTre).FormulaA1 = $"=IF($I${hangSiSo}=0,0,({tongCanhNt})/$I${hangSiSo})";
                        ws.Cell(r, cotMauGiao).FormulaA1 = $"=IF($J${hangSiSo}=0,0,({tongCanhMg})/$J${hangSiSo})";
                    }
                    else
                    {
                        ws.Cell(r, cotNhaTre).FormulaA1 = CongThucBinhQuan(hangNguon,
                            ten is "Gạo" ? 9 : 7, 9, hangSiSo);
                        ws.Cell(r, cotMauGiao).FormulaA1 = CongThucBinhQuan(hangNguon,
                            ten is "Gạo" ? 10 : 8, 10, hangSiSo);
                    }
                }
                else
                {
                    var dmTm = ten == "Tráng miệng" && coNhoRuby;   // ngày có Nho Ruby: định mức tráng miệng 2.500đ
                    ws.Cell(r, cotNhaTre).Value = dmTm ? 2500m : nhaTre;
                    ws.Cell(r, cotMauGiao).Value = dmTm ? 2500m : mauGiao;
                }
                r++;
            }

            ws.Cell(r, cotTen).Value = "Tổng cộng";
            ws.Cell(r, cotNhaTre).FormulaA1 = $"=SUM(M{hang + 2}:M{r - 1})";
            ws.Cell(r, cotMauGiao).FormulaA1 = $"=SUM(N{hang + 2}:N{r - 1})";
            ws.Range(r, cotTen, r, cotMauGiao).Style.Font.Bold = true;
            ws.Range(hangTieuDe, cotTen, r, cotMauGiao).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(hangTieuDe, cotTen, r, cotMauGiao).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(hang + 2, cotNhaTre, r, cotMauGiao).Style.NumberFormat.Format = "#,##0";
        }

        TaoBang(hangBangDinhMuc, "Phân bố định mức (đ/cháu)", laBangThucTe: false);
        TaoBang(hangBangThucTe, "Bình quân thực tế mỗi cháu (đ/cháu)", laBangThucTe: true);
    }

    /// <summary>File mẫu "Thực đơn tuần ok" dùng bảng màu (theme) "Office 2007-2010": màu nhấn 2 = đỏ
    /// C0504D, màu nhấn 3 = xanh lá 9BBB59. Kiểu bảng Excel (TableStyleMedium4 / Medium10) lấy màu từ
    /// theme của file, mà ClosedXML ghi theme Office mới (cam/xám) -> không có bước này thì bảng ra
    /// màu cam/xám thay vì xanh lá/đỏ như file mẫu. Chỉ thay 2 mã màu trong theme1.xml của file vừa
    /// tạo, mọi thứ khác giữ nguyên.</summary>
    // ---------------------------------------------------------------------------------------
    // CÂN ĐỐI THEO ĐỊNH MỨC
    // ---------------------------------------------------------------------------------------
    private const decimal NguongLechToiDa = 0.15m;   // lệch > 15% định mức => không cân (thường do thiếu món)

    /// <summary>Co giãn LIÊN TỤC các dòng tính theo cân để tổng = mục tiêu — chỉ dùng cho nhóm đã có số lưu cố định
    /// (không đệm được bằng dòng Muối). Trả null nếu lệch quá <see cref="NguongLechToiDa"/> hoặc hệ số ngoài 0,5–2.</summary>
    private static decimal[]? SoLuongSauCanDoi(IReadOnlyList<(decimal Sl, decimal Gia, string? Dvt)> dong, decimal mucTieu, decimal tongDinhMuc)
    {
        if (dong.Count == 0 || mucTieu <= 0 || tongDinhMuc <= 0) return null;
        var hienTai = dong.Sum(d => d.Sl * d.Gia);
        if (hienTai <= 0) return null;
        if (Math.Abs(mucTieu - hienTai) > NguongLechToiDa * tongDinhMuc) return null;

        var coGian = dong.Select(d => ChiTietNgayTinhToanService.DonViCan(d.Dvt)).ToArray();
        var tienCoGian = dong.Where((d, i) => coGian[i]).Sum(d => d.Sl * d.Gia);
        var tienCoDinh = hienTai - tienCoGian;

        decimal k;
        if (tienCoGian > 0 && (mucTieu - tienCoDinh) / tienCoGian is var k1 && k1 >= 0.5m && k1 <= 2m)
        {
            k = k1;
        }
        else
        {
            k = mucTieu / hienTai;
            if (k < 0.5m || k > 2m) return null;
            coGian = dong.Select(_ => true).ToArray();
        }
        return dong.Select((d, i) => coGian[i] ? d.Sl * k : d.Sl).ToArray();
    }

    /// <summary>Cách làm của nhân viên bếp (file mẫu): lấy ĐỊNH MỨC mỗi trẻ × số trẻ → làm tròn theo đơn vị tính
    /// (kg → 0,1 ; gói/chai/hộp/quả… → tròn LÊN số nguyên) → phần LẺ CUỐI CÙNG để tổng = đúng định mức thì dồn vào
    /// dòng MUỐI. Không co giãn khẩu phần: chỉ khi còn dư/thiếu thì nhích từng 0,1 kg, dàn đều giữa các mặt hàng
    /// tính theo kg (ưu tiên mặt hàng ít bị chỉnh nhất) cho đến khi phần lẻ đủ nhỏ để muối hấp thụ.
    /// Trả null nếu không cân được (lệch quá 15% hoặc không có dòng kg để nhích).</summary>
    private static (decimal[] ThucPham, decimal[] GiaVi)? LamTronVaCanDoiNhom(
        IReadOnlyList<(decimal Sl, decimal Gia, string? Dvt)> thucPham,
        IReadOnlyList<(decimal Sl, decimal Gia, string? Dvt)> giaVi, int muoi, decimal mucTieu, bool[]? giuNguyenGiaVi = null, bool[]? dongMan = null)
    {
        if (muoi < 0 || giaVi[muoi].Gia <= 0 || mucTieu <= 0) return null;
        var hienTai = thucPham.Sum(x => x.Sl * x.Gia) + giaVi.Sum(x => x.Sl * x.Gia);
        if (Math.Abs(mucTieu - hienTai) > NguongLechToiDa * mucTieu) return null;

        // 1) định mức × số trẻ -> làm tròn theo đơn vị tính (gia vị cũng vậy, riêng muối là phần đệm)
        var f = thucPham.Select(x => ChiTietNgayTinhToanService.LamTronTheoDvt(x.Sl, x.Dvt)).ToArray();
        // Gia vị: làm tròn 0,1 như sổ xuất gia vị của file mẫu (KHÔNG tròn lên số nguyên theo chai/gói).
        var g = giaVi.Select((x, i) => i == muoi ? 0m : (giuNguyenGiaVi is not null && giuNguyenGiaVi[i]) ? x.Sl : Math.Round(x.Sl, 1, MidpointRounding.AwayFromZero)).ToArray();
        var gia = thucPham.Select(x => x.Gia).ToArray();
        var goc = thucPham.Select(x => x.Sl).ToArray();
        var laKg = thucPham.Select(x => ChiTietNgayTinhToanService.DonViCan(x.Dvt)).ToArray();
        const decimal Buoc = 0.1m;
        var nhich = new int[f.Length];             // số bước 0,1 đã nhích (+/-) của từng dòng

        decimal Du()
        {
            decimal t = mucTieu;
            for (var i = 0; i < f.Length; i++) t -= f[i] * gia[i];
            for (var i = 0; i < g.Length; i++) t -= g[i] * giaVi[i].Gia;
            return t;
        }

        // Chọn dòng kg ít bị chỉnh nhất (tính theo % so với định mức), thoả điều kiện; ưu tiên bước tiền lớn khi hoà.
        // Có món MẶN (dongMan): tiền thừa / thiếu chỉ nhích ở dòng của món Mặn, các món khác giữ ĐÚNG định lượng chuẩn.
        // Món Mặn không đủ chỗ nhích thì mới dùng tới dòng khác.
        int ChonDong(Func<int, bool> hopLe)
        {
            if (dongMan is not null && dongMan.Any(b => b))
            {
                var m = ChonDongTrong(hopLe, true);
                if (m >= 0) return m;
            }
            return ChonDongTrong(hopLe, false);
        }

        int ChonDongTrong(Func<int, bool> hopLe, bool chiMan)
        {
            int best = -1; var bestRatio = decimal.MaxValue; decimal bestCost = 0;
            for (var i = 0; i < f.Length; i++)
            {
                if (chiMan && !dongMan![i]) continue;
                if (!laKg[i] || goc[i] <= 0 || !hopLe(i)) continue;
                var ratio = Math.Abs(nhich[i]) * Buoc / Math.Max(goc[i], 0.1m);
                var cost = Buoc * gia[i];
                if (ratio < bestRatio || (ratio == bestRatio && cost > bestCost)) { best = i; bestRatio = ratio; bestCost = cost; }
            }
            return best;
        }

        const decimal TranDemMuoi = 5000m;          // phần lẻ cuối cùng cho muối ≤ 5.000đ (~1,5 gói), như file mẫu
        var du = Du();
        var guard = 0;
        while (du < 0 && guard++ < 2000)            // chi quá tay -> bớt 0,1 kg
        {
            var i = ChonDong(k => f[k] >= Buoc && Buoc * gia[k] <= -du);
            if (i < 0) i = ChonDong(k => f[k] >= Buoc);
            if (i < 0) break;
            f[i] -= Buoc; nhich[i]--; du += Buoc * gia[i];
        }
        while (du > TranDemMuoi && guard++ < 4000) // còn dư nhiều -> thêm 0,1 kg
        {
            var i = ChonDong(k => Buoc * gia[k] <= du);
            if (i < 0) break;
            f[i] += Buoc; nhich[i]++; du -= Buoc * gia[i];
        }
        if (du < 0 || du > 3 * TranDemMuoi) return null;   // không đệm được bằng muối một cách hợp lý

        g[muoi] = du / giaVi[muoi].Gia;             // phần lẻ cuối cùng -> muối: tổng = đúng định mức
        return (f, g);
    }

    private const decimal GaoNhaTreMotSuat = 0.055m;
    private const decimal GaoMauGiaoMotSuat = 0.065m;

    private static bool LaDongGao(string? tenHang) => (tenHang ?? "").Contains("Gạo", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------------------
    // ĐƯA "CHÊNH LỆCH CUỐI NGÀY" VỀ 0
    // ---------------------------------------------------------------------------------------
    private const decimal GaoThemToiDa = 0.5m;   // kg gạo cộng thêm cho Nhà trẻ / Mẫu giáo khi còn dư tiền
    private const decimal BuocNhich = 0.1m;      // nhích số lượng thực phẩm theo cân từng 0,1 kg

    /// <summary>Nhóm nào còn DƯ tiền so với định mức (số tiền ăn/cháu × số suất) thì:
    ///  1) Nhà trẻ / Mẫu giáo: cộng thêm 0,5 kg vào dòng gạo của gia vị (nếu đủ tiền);
    ///  2) phân bổ phần còn lại: nhích từng 0,1 kg ở các dòng thực phẩm tính theo cân (dòng bị chỉnh ít nhất, theo % so
    ///     với số lượng gốc, được nhích trước) cho tới khi phần dư nhỏ hơn 1 bước;
    ///  3) phần lẻ cuối cùng dồn vào dòng muối (hoặc gạo nếu không có muối) => Chênh lệch = 0.
    /// Nhóm đang chi VƯỢT định mức thì giữ nguyên (không tự cắt bớt).</summary>
    private static void SanVeKhong(
        ChiTietNgayTinhToanResponse kq, GiaViThatDto giaVi,
        DinhMucAnNgay? dmAnSang, DinhMucAnNgay? dmNhaTre, DinhMucAnNgay? dmMauGiao,
        bool canSang, bool canChinh, string? monManNhaTre = null, string? monManMauGiao = null, ISet<int>? hangChinhDb = null)
    {
        static decimal DinhMuc(DinhMucAnNgay? d) => d is null ? 0 : d.SoSuatAn * d.TienAn1Chau;
        static string ChuanMon(string? t) => (t ?? "").Trim().ToLowerInvariant();
        var gv = giaVi.DanhSach.ToList();

        // NGUYÊN LIỆU CHÍNH của từng món: dòng được đánh dấu trong công thức món (cột la_nguyen_lieu_chinh); món chưa đánh dấu
        // thì tự lấy dòng tính theo cân có thành tiền lớn nhất. Tiền dư / thiếu được dồn vào dòng này trước.
        bool[] XacDinhChinh(string?[] mon, decimal[] sl, decimal[] gia, string?[] dvt, int[] hangId, bool[] coDinh)
        {
            var kq2 = new bool[sl.Length];
            bool DungDuoc(int i) => sl[i] > 0 && gia[i] > 0 && ChiTietNgayTinhToanService.DonViCan(dvt[i]);
            foreach (var nhom in Enumerable.Range(0, sl.Length).Where(i => !coDinh[i]).GroupBy(i => ChuanMon(mon[i])))
            {
                var ds = nhom.ToList();
                var danhDau = ds.Where(i => hangChinhDb is not null && hangId[i] > 0 && hangChinhDb.Contains(hangId[i]) && DungDuoc(i)).ToList();
                if (danhDau.Count > 0) { foreach (var i in danhDau) kq2[i] = true; continue; }
                var lonNhat = ds.Where(DungDuoc).OrderByDescending(i => sl[i] * gia[i]).ToList();
                if (lonNhat.Count > 0) kq2[lonNhat[0]] = true;
            }
            return kq2;
        }

        // Đặt thêm "them" vào cột cot (0 sáng, 1 nhà trẻ, 2 mẫu giáo) của dòng gia vị thứ i.
        void CongGiaVi(int i, int cot, decimal them) => gv[i] = DatCotGiaVi(gv[i], cot, LayCotGiaVi(gv[i], cot) + them);

        // Trả về (số lượng mới của từng dòng thực phẩm, true nếu có thay đổi). coDinh[i] = dòng không được nhích (sữa...).
        decimal[] PhanBo(int cot, decimal dinhMuc, decimal[] sl, decimal[] gia, string?[] dvt, bool[] coDinh, bool duocCongGao, string?[] mon, string? monMan, bool[] laChinh)
        {
            var moi = (decimal[])sl.Clone();
            var du = dinhMuc - gv.Sum(x => LayCotGiaVi(x, cot) * x.DonGia) - moi.Select((q, i) => q * gia[i]).Sum();
            if (du <= 0) return moi;

            if (false && duocCongGao)   // gạo theo đúng quy tắc, không cộng thêm 0,5 kg
            {
                var g = gv.FindIndex(x => x.TenHang.TrimStart().StartsWith("Gạo", StringComparison.OrdinalIgnoreCase) && x.DonGia > 0);
                if (g >= 0 && du >= GaoThemToiDa * gv[g].DonGia)
                {
                    CongGiaVi(g, cot, GaoThemToiDa);
                    du -= GaoThemToiDa * gv[g].DonGia;
                }
            }

            // Tiền dư: dồn về món MẶN (xem bên dưới); không có món Mặn thì dàn đều ra TỪNG MÓN: mỗi vòng, mỗi món nhích 1 bước 0,1 kg (món MẶN được 2 bước, đi trước);
            // trong 1 món chọn dòng bị chỉnh ít nhất (theo % so với số lượng gốc). Lặp tới khi không còn bước nào vừa số dư.
            var nhich = new int[moi.Length];
            var khoaMan = ChuanMon(monMan);
            var nhomMon = Enumerable.Range(0, moi.Length)
                .Where(i => !coDinh[i])
                .GroupBy(i => ChuanMon(mon[i]))
                .OrderBy(g => khoaMan.Length > 0 && g.Key == khoaMan ? 0 : 1)
                .ThenBy(g => g.Min())
                .Select(g => (Man: khoaMan.Length > 0 && g.Key == khoaMan, Dong: g.ToList()))
                .ToList();
            // Số lượng từng món giữ ĐÚNG định lượng chuẩn: toàn bộ tiền còn thừa dồn về món MẶN (nhích 0,1 kg ở dòng của món Mặn).
            // Ngày không có món Mặn (hoặc cột Ăn sáng, không có món Mặn) thì giữ cách cũ: dàn đều ra các món.
            if (nhomMon.Any(g => g.Man)) nhomMon = nhomMon.Where(g => g.Man).ToList();

            // Chọn dòng nhích: nguyên liệu CHÍNH trước; trong cùng loại chọn dòng có độ lệch SAU KHI nhích (so với số lượng gốc) nhỏ nhất.
            // coTran = true: dòng nào nhích xong lệch quá TranLech so với số gốc thì bỏ qua (nhường dòng khác).
            const decimal TranLech = 0.05m;
            int ChonTrongMon(List<int> dong, bool coTran)
            {
                var chon = -1;
                var lechChon = decimal.MaxValue;
                foreach (var i in dong)
                {
                    if (sl[i] <= 0 || gia[i] <= 0 || !ChiTietNgayTinhToanService.DonViCan(dvt[i])) continue;
                    var tien = BuocNhich * gia[i];
                    if (tien > du) continue;
                    var lech = Math.Abs(moi[i] + BuocNhich - sl[i]) / Math.Max(sl[i], BuocNhich);
                    if (coTran && lech > TranLech) continue;
                    var tot = chon < 0
                        || (laChinh[i] && !laChinh[chon])
                        || (laChinh[i] == laChinh[chon] && (lech < lechChon || (lech == lechChon && tien > BuocNhich * gia[chon])));
                    if (tot) { chon = i; lechChon = lech; }
                }
                return chon;
            }

            void ChayNhich(bool coTran)
            {
                for (var vong = 0; vong < 5000; vong++)
                {
                    var coNhich = false;
                    foreach (var (man, dong) in nhomMon)
                    {
                        for (var buoc = 0; buoc < (man ? 2 : 1); buoc++)
                        {
                            var chon = ChonTrongMon(dong, coTran);
                            if (chon < 0) break;
                            moi[chon] += BuocNhich;
                            nhich[chon]++;
                            du -= BuocNhich * gia[chon];
                            coNhich = true;
                        }
                    }
                    if (!coNhich) break;
                }
            }

            ChayNhich(true);                       // 1) trong trần lệch ±5% so với số gốc
            if (du > 5000m) ChayNhich(false);      // 2) còn dư nhiều (> ~1 gói muối) mới bỏ trần, tránh dồn cả cục tiền vào muối

            if (du > 0)
            {
                var m = gv.FindIndex(x => x.TenHang.Contains("Muối", StringComparison.OrdinalIgnoreCase) && x.DonGia > 0);
                if (m < 0) m = gv.FindIndex(x => x.TenHang.TrimStart().StartsWith("Gạo", StringComparison.OrdinalIgnoreCase) && x.DonGia > 0);
                if (m >= 0) CongGiaVi(m, cot, du / gv[m].DonGia);
            }
            return moi;
        }

        // --- Ăn sáng (không cộng gạo)
        if (canSang && kq.Sang.Count > 0 && DinhMuc(dmAnSang) > 0)
        {
            var slS = kq.Sang.Select(x => x.SoLuong).ToArray();
            var giaS = kq.Sang.Select(x => x.DonGia).ToArray();
            var dvtS = kq.Sang.Select(x => x.Dvt).ToArray();
            var coDinhS = kq.Sang.Select(x => ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon)).ToArray();
            var tenMonS = kq.Sang.Select(x => x.TenMon).ToArray();
            var chinhS = XacDinhChinh(tenMonS, slS, giaS, dvtS, kq.Sang.Select(x => x.HangHoaId).ToArray(), coDinhS);
            var moi = PhanBo(0, DinhMuc(dmAnSang), slS, giaS, dvtS, coDinhS, duocCongGao: false, tenMonS, monMan: null, chinhS);
            kq.Sang = kq.Sang.Select((x, i) => x with { SoLuong = moi[i], ThanhTien = moi[i] * x.DonGia }).ToList();
            kq.TongTienSang = kq.Sang.Sum(x => x.ThanhTien);
        }

        // --- Nhà trẻ / Mẫu giáo (cộng gạo 0,5 kg)
        if (canChinh && kq.Chinh.Count > 0)
        {
            var coDinh = kq.Chinh.Select(x => ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon)).ToArray();
            var dvt = kq.Chinh.Select(x => x.Dvt).ToArray();
            var gia = kq.Chinh.Select(x => x.DonGia).ToArray();
            var tenMon = kq.Chinh.Select(x => x.TenMon).ToArray();
            var hangIdChinh = kq.Chinh.Select(x => x.HangHoaId).ToArray();
            var slNtG = kq.Chinh.Select(x => x.SlNhaTre).ToArray();
            var slMgG = kq.Chinh.Select(x => x.SlMauGiao).ToArray();
            var chinhNt = XacDinhChinh(tenMon, slNtG, gia, dvt, hangIdChinh, coDinh);
            var chinhMg = XacDinhChinh(tenMon, slMgG, gia, dvt, hangIdChinh, coDinh);
            var moiNt = DinhMuc(dmNhaTre) > 0
                ? PhanBo(1, DinhMuc(dmNhaTre), slNtG, gia, dvt, coDinh, duocCongGao: true, tenMon, monManNhaTre, chinhNt)
                : kq.Chinh.Select(x => x.SlNhaTre).ToArray();
            var moiMg = DinhMuc(dmMauGiao) > 0
                ? PhanBo(2, DinhMuc(dmMauGiao), slMgG, gia, dvt, coDinh, duocCongGao: true, tenMon, monManMauGiao, chinhMg)
                : kq.Chinh.Select(x => x.SlMauGiao).ToArray();
            kq.Chinh = kq.Chinh.Select((x, i) => x with
            {
                SlNhaTre = moiNt[i], SlMauGiao = moiMg[i],
                TtNhaTre = moiNt[i] * x.DonGia, TtMauGiao = moiMg[i] * x.DonGia,
            }).ToList();
            kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
            kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
        }

        giaVi.DanhSach = gv;
    }

    private static NguyenLieuBaCotDto DatCotGiaVi(NguyenLieuBaCotDto x, int cot, decimal sl) => cot switch
    {
        0 => x with { SlAnSang = sl, TtAnSang = sl * x.DonGia },
        1 => x with { SlNhaTre = sl, TtNhaTre = sl * x.DonGia },
        _ => x with { SlMauGiao = sl, TtMauGiao = sl * x.DonGia },
    };

    private static decimal LayCotGiaVi(NguyenLieuBaCotDto x, int cot) => cot switch
    {
        0 => x.SlAnSang, 1 => x.SlNhaTre, _ => x.SlMauGiao,
    };


    // ---------------------------------------------------------------------------------------
    // SHEET "Gia vị" + "Đồ khô" (như file mẫu tuần)
    // ---------------------------------------------------------------------------------------

    /// <summary>Phân loại mặt hàng vào cột của sheet "Gia vị" (null = không thuộc sheet này).</summary>
    private static string? KhoaGiaViTuan(string? tenHang)
    {
        var t = (tenHang ?? "").Trim().ToLowerInvariant();
        if (t.StartsWith("muối")) return "muoi";
        if (t.Contains("bột nêm") || t.Contains("knor")) return "knorr";
        if (t.StartsWith("dầu") && t.Contains("simply")) return "dau";
        if (t.StartsWith("nước mắm")) return "nuocmam";
        if (t.StartsWith("xì dầu")) return "xidau";
        if (t.StartsWith("bột canh")) return "botcanh";
        if (t.StartsWith("đường")) return "duong";
        if (t.StartsWith("gạo")) return "gao";
        if (t.StartsWith("hành tím")) return "hanh";
        if (t.StartsWith("tỏi")) return "toi";
        return null;
    }

    /// <summary>Cộng số lượng gia vị của 1 (cơ sở, ngày) vào tổng cả tuần của cơ sở đó. Hành tím / tỏi nằm trong mục 3/4 nên lấy từ kq.</summary>
    private static void CongGiaViTuan(Dictionary<string, Dictionary<string, decimal>> tuan, string maCoSo, GiaViThatDto giaVi, ChiTietNgayTinhToanResponse kq)
    {
        if (!tuan.TryGetValue(maCoSo, out var d)) { d = new Dictionary<string, decimal>(); tuan[maCoSo] = d; }
        void Cong(string? khoa, decimal sl)
        {
            if (khoa is null || sl == 0) return;
            d[khoa] = d.TryGetValue(khoa, out var cu) ? cu + sl : sl;
        }
        foreach (var g in giaVi.DanhSach)
        {
            var k = KhoaGiaViTuan(g.TenHang);
            if (k is not null && k != "hanh" && k != "toi") Cong(k, g.SlAnSang + g.SlNhaTre + g.SlMauGiao);
        }
        foreach (var x in kq.Sang) { var k = KhoaGiaViTuan(x.TenHang); if (k is "hanh" or "toi") Cong(k, x.SoLuong); }
        foreach (var x in kq.Chinh) { var k = KhoaGiaViTuan(x.TenHang); if (k is "hanh" or "toi") Cong(k, x.SlNhaTre + x.SlMauGiao); }
    }

    /// <summary>Sheet "Gia vị": ĐỊNH LƯỢNG GIA VỊ tuần n/tháng — 3 dòng Trường chính / Phân hiệu / Điểm trường, mỗi cột 1 loại gia vị.
    /// Số liệu = TỔNG CẢ TUẦN của các sheet Chợ (gói/chai làm tròn lên, kg làm tròn lên 0,5; gạo lên 1 kg). Khối thứ hai
    /// (có cột "Đề xuất tăng") giữ đúng bố cục file mẫu để nhân viên tự điền số đề xuất.</summary>
    private static void GhiSheetGiaVi(XLWorkbook workbook, DateOnly thuHai, Dictionary<string, Dictionary<string, decimal>> tuan)
    {
        var ws = workbook.Worksheets.Add("Gia vị");
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 12;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);

        var cot = new (string Khoa, string TieuDe, bool Nguyen, double Buoc)[]
        {
            ("muoi", "Muối (gói 500g)", true, 1), ("knorr", "Bột nêm Knorr (gói 900g)", true, 1),
            ("dau", "Dầu simply (chai 1 lít)", true, 1), ("hanh", "Hành khô (kg)", false, 0.5),
            ("toi", "Tỏi khô sơ chế (kg)", false, 0.5), ("nuocmam", "Nước mắm đệ nhị 800ml (chai)", true, 1),
            ("xidau", "Xì dầu Maggi 700ml (chai)", true, 1), ("botcanh", "Bột canh gà (gói)", true, 1),
            ("duong", "Đường RS (kg)", false, 0.5), ("gao", "Gạo (kg)", false, 1),
        };
        var hangCoSo = new[] { ("TC", "Trường chính"), ("PH", "Phân hiệu"), ("ĐT", "Điểm trường") };
        var tieuDe = $"ĐỊNH LƯỢNG GIA VỊ tuần {(thuHai.Day - 1) / 7 + 1}/{thuHai.Month}";

        ws.Column(1).Width = 14;
        for (var i = 0; i <= cot.Length; i++) ws.Column(2 + i).Width = 15;

        for (var khoi = 0; khoi < 2; khoi++)
        {
            var dau = khoi == 0 ? 1 : 8;   // bố cục như mẫu: khối 1 ở dòng 1–5, khối 2 ở dòng 8–12
            ws.Range(dau, 1, dau, 10).Merge();
            ws.Cell(dau, 1).Value = tieuDe;
            ws.Cell(dau, 1).Style.Font.Bold = true;
            ws.Cell(dau, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var hDau = dau + 1;
            ws.Cell(hDau, 1).Value = "1 tuần";
            for (var i = 0; i < cot.Length; i++) ws.Cell(hDau, 2 + i).Value = cot[i].TieuDe;
            var cuoiCot = 1 + cot.Length;
            if (khoi == 1) { ws.Cell(hDau, cuoiCot + 1).Value = "Đề xuất tăng"; cuoiCot++; }
            var tieuDeBang = ws.Range(hDau, 1, hDau, cuoiCot);
            tieuDeBang.Style.Font.Bold = true;
            tieuDeBang.Style.Alignment.WrapText = true;
            tieuDeBang.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            tieuDeBang.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            tieuDeBang.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2E2D2");
            ws.Row(hDau).Height = 48;

            for (var r = 0; r < hangCoSo.Length; r++)
            {
                var hang = hDau + 1 + r;
                ws.Cell(hang, 1).Value = hangCoSo[r].Item2;
                if (!tuan.TryGetValue(hangCoSo[r].Item1, out var d)) continue;
                for (var i = 0; i < cot.Length; i++)
                {
                    if (!d.TryGetValue(cot[i].Khoa, out var tong) || tong <= 0) continue;
                    var x = (double)tong;
                    ws.Cell(hang, 2 + i).Value = cot[i].Nguyen
                        ? Math.Ceiling(x - 1e-9)
                        : Math.Ceiling(x / cot[i].Buoc - 1e-9) * cot[i].Buoc;
                }
            }
            var bang = ws.Range(hDau, 1, hDau + hangCoSo.Length, cuoiCot);
            bang.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            bang.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(hDau + 1, 2, hDau + hangCoSo.Length, cuoiCot).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    /// <summary>Tỷ lệ đồ khô trên mỗi cháu (kg/cháu) — đúng bảng của file mẫu. Hàng: TC sáng, TC trưa, TC xế, PH sáng, PH trưa, PH xế,
    /// ĐT sáng, ĐT trưa, ĐT xế. Cột: Bột bán, Nui sò, Miến HQ, Mỳ/bún tươi, Bánh mì ổ, Gạo, Nếp/Đậu xanh, Bột mì sợi. null = không dùng.</summary>
    private static readonly double?[][] TyLeDoKho =
    {
        new double?[] { 0.025, 0.025, 0.028, 0.13, 0.4, 0.02, 0.0075, 0.035 },
        new double?[] { 0.03, 0.03, 0.028, 0.15, 0.5, 0.065, null, 0.032 },
        new double?[] { 0.02, 0.02, 0.028, 0.1, 0.4, 0.02, 0.009, 0.027 },
        new double?[] { 0.02, 0.025, 0.028, 0.13, 0.4, 0.02, 0.01, 0.032 },
        new double?[] { 0.03, 0.03, 0.028, 0.15, 0.5, 0.065, null, 0.032 },
        new double?[] { 0.015, 0.02, 0.028, 0.1, 0.4, 0.02, 0.009, 0.032 },
        new double?[] { 0.02, 0.025, 0.025, 0.13, 0.4, 0.02, 0.01, 0.032 },
        new double?[] { 0.03, 0.03, 0.025, 0.15, 0.5, 0.065, null, 0.032 },
        new double?[] { 0.015, 0.02, 0.025, 0.1, 0.4, 0.02, 0.009, 0.032 },
    };

    private static int PhoBienNhat(IEnumerable<int> cacSo)
    {
        var ds = cacSo.Where(x => x > 0).ToList();
        return ds.Count == 0 ? 0 : ds.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => ds.IndexOf(g.Key)).First().Key;
    }

    /// <summary>Sheet "Đồ khô": BẢNG NHẬP SĨ SỐ DỰ KIẾN (Thứ 2–6 lấy số xuất phổ biến nhất trong tuần; Thứ 7 lấy theo ngày thứ 7) và
    /// BẢNG TÍNH ĐỒ KHÔ DỰ KIẾN = tỷ lệ/cháu × sĩ số (công thức Excel, sửa sĩ số là tự tính lại).</summary>
    private static void GhiSheetDoKho(XLWorkbook workbook, DateOnly thuHai, Dictionary<(string CoSo, DateOnly Ngay), (int Sang, int Nt, int Mg)> suat)
    {
        var ws = workbook.Worksheets.Add("Đồ khô");
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 12;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);

        (int Sang, int Nt, int Mg) NgayThuong(string cs)
        {
            var ds = Enumerable.Range(0, 5)
                .Select(i => suat.TryGetValue((cs, thuHai.AddDays(i)), out var v) ? v : (Sang: 0, Nt: 0, Mg: 0)).ToList();
            return (PhoBienNhat(ds.Select(x => x.Sang)), PhoBienNhat(ds.Select(x => x.Nt)), PhoBienNhat(ds.Select(x => x.Mg)));
        }
        (int Sang, int Nt, int Mg) ThuBay(string cs) =>
            suat.TryGetValue((cs, thuHai.AddDays(5)), out var v) ? v : (Sang: 0, Nt: 0, Mg: 0);

        ws.Range(1, 1, 1, 7).Merge();
        ws.Cell(1, 1).Value = "BẢNG NHẬP SỈ SỐ DỰ KIẾN";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var nhan = new[] { "TC sáng", "TC trưa", "PH sáng", "PH trưa", "ĐT sáng", "ĐT trưa" };
        for (var i = 0; i < nhan.Length; i++)
        {
            ws.Cell(2, 3 + i).Value = nhan[i];
            ws.Cell(2, 3 + i).Style.Font.Bold = true;
            ws.Cell(2, 3 + i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        void So(int hang, int cot, int v) { if (v > 0) ws.Cell(hang, cot).Value = v; }
        void KhoiSiSo(int hang, string tieuDe, Func<string, (int Sang, int Nt, int Mg)> lay)
        {
            ws.Range(hang, 1, hang + 1, 1).Merge();
            ws.Cell(hang, 1).Value = tieuDe;
            ws.Cell(hang, 2).Value = "Nhà trẻ";
            ws.Cell(hang + 1, 2).Value = "Mẫu giáo";
            var tc = lay("TC"); var ph = lay("PH"); var dt = lay("ĐT");
            ws.Range(hang, 3, hang + 1, 3).Merge(); So(hang, 3, tc.Sang);
            So(hang, 4, tc.Nt); So(hang + 1, 4, tc.Mg);
            ws.Range(hang, 5, hang + 1, 5).Merge(); So(hang, 5, ph.Sang);
            So(hang, 6, ph.Nt); So(hang + 1, 6, ph.Mg);
            ws.Range(hang, 7, hang + 1, 7).Merge(); So(hang, 7, dt.Sang);
            ws.Range(hang, 8, hang + 1, 8).Merge(); So(hang, 8, dt.Nt + dt.Mg);   // ĐT trưa: 1 ô (Nhà trẻ + Mẫu giáo)
            var t = hang + 2;
            ws.Cell(t, 1).Value = "Tổng";
            ws.Cell(t, 3).FormulaA1 = $"=C{hang}";
            ws.Cell(t, 4).FormulaA1 = $"=D{hang}+D{hang + 1}";
            ws.Cell(t, 5).FormulaA1 = $"=E{hang}";
            ws.Cell(t, 6).FormulaA1 = $"=SUM(F{hang}:F{hang + 1})";
            ws.Cell(t, 7).FormulaA1 = $"=G{hang}";
            ws.Cell(t, 8).FormulaA1 = $"=H{hang}";
            ws.Range(t, 1, t, 8).Style.Font.Bold = true;
            var vung = ws.Range(hang, 1, t, 8);
            vung.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            vung.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            vung.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Range(hang, 3, t, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(hang, 1, hang + 1, 1).Style.Alignment.WrapText = true;
        }
        KhoiSiSo(3, "Số lượng trẻ dự kiến các ngày trong tuần", NgayThuong);
        KhoiSiSo(6, "Số lượng dự kiến thứ bảy", ThuBay);
        ws.Column(1).Width = 24;

        // ----- Bảng tính đồ khô -----
        ws.Cell(10, 12).Value = "BẢNG TÍNH ĐỒ KHÔ DỰ KIẾN THEO SỐ LƯỢNG";
        ws.Cell(10, 12).Style.Font.Bold = true;

        var matHang = new[] { ("Bột bán", 2), ("Nui sò", 5), ("Miến HQ", 8), ("Mỳ, bún tươi", 12), ("Bánh mì ổ", 15), ("Gạo", 18), ("Nếp/ Đậu xanh", 21), ("Bột mì sợi", 24) };
        foreach (var (ten, cot) in matHang) { ws.Cell(11, cot).Value = ten; ws.Cell(11, cot).Style.Font.Bold = true; }
        var nhanHang = new[] { "TC sáng", "TC trưa", "TC xế", "PH sáng", "PH trưa", "PH xế", "ĐT sáng", "ĐT trưa", "ĐT xế" };
        var oSiSo = new[] { "$C$3", "$D$5", "$D$5", "$E$3", "$F$5", "$F$5", "$G$5", "$H$5", "$H$5" };

        for (var m = 0; m < 8; m++)
        {
            var c0 = 2 + 3 * m;   // cột tỷ lệ; +1 = SL học sinh; +2 = Số lượng
            ws.Cell(12, c0).Value = "tỷ lệ/ cháu";
            ws.Cell(12, c0 + 1).Value = "SL học sinh";
            ws.Cell(12, c0 + 2).Value = "Số lượng";
        }
        ws.Range(11, 2, 12, 25).Style.Font.Bold = true;
        ws.Range(11, 2, 12, 25).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(11, 2, 12, 25).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2E2D2");

        for (var r = 0; r < nhanHang.Length; r++)
        {
            var hang = 13 + r;
            ws.Cell(hang, 1).Value = nhanHang[r];
            for (var m = 0; m < 8; m++)
            {
                var c0 = 2 + 3 * m;
                if (TyLeDoKho[r][m] is double tyLe) ws.Cell(hang, c0).Value = tyLe;
                ws.Cell(hang, c0 + 1).FormulaA1 = $"={oSiSo[r]}";
                ws.Cell(hang, c0 + 2).FormulaA1 = $"={ws.Cell(hang, c0).Address.ToStringRelative()}*{ws.Cell(hang, c0 + 1).Address.ToStringRelative()}";
                ws.Cell(hang, c0).Style.NumberFormat.Format = "#,##0.000";
                ws.Cell(hang, c0 + 2).Style.NumberFormat.Format = "#,##0.000";
            }
        }
        var bangTinh = ws.Range(11, 1, 21, 25);
        bangTinh.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        bangTinh.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        for (var c = 2; c <= 25; c++) ws.Column(c).Width = 11;
    }

    private static void CanDoiTheoDinhMuc(
        ChiTietNgayTinhToanResponse kq, GiaViThatDto giaVi,
        DinhMucAnNgay? dmAnSang, DinhMucAnNgay? dmNhaTre, DinhMucAnNgay? dmMauGiao,
        bool canSang, bool canChinh, string oTrongNhaTre = "", string oTrongMauGiao = "", string? monManNhaTre = null, string? monManMauGiao = null,
        HashSet<string>? tenMonXeNhaTre = null, HashSet<string>? tenMonXeMauGiao = null, int soSuatNhaTre = 0, int soSuatMauGiao = 0,
        HashSet<string>? tenMonTrangMiengNhaTre = null, HashSet<string>? tenMonTrangMiengMauGiao = null)
    {
        static decimal DinhMuc(DinhMucAnNgay? d) => d is null ? 0 : d.SoSuatAn * d.TienAn1Chau;
        var khongCan = new List<string>();

        // Mô tả nhóm không cân được: chi bao nhiêu so với định mức (+/- %), để biết thiếu hay thừa bao nhiêu.
        string MoTaLech(string ten, int cot, decimal dinhMuc, IReadOnlyList<(decimal Sl, decimal Gia, string? Dvt)> thucPham)
        {
            var chi = thucPham.Sum(x => x.Sl * x.Gia) + giaVi.DanhSach.Sum(x => LayCotGiaVi(x, cot) * x.DonGia);
            var pct = dinhMuc == 0 ? 0 : (chi - dinhMuc) / dinhMuc * 100;
            return $"{ten}: chi {chi:N0} / định mức {dinhMuc:N0} ({(pct >= 0 ? "+" : "")}{pct:0.#}%)";
        }

        // Gia vị đã có số lưu cố định (Sửa/Xóa/phiếu thật) -> không đụng vào, chỉ co giãn thực phẩm (không làm tròn).
        var duocLamTron = !giaVi.CoPhieu;
        var muoi = giaVi.DanhSach.FindIndex(x => x.TenHang.Contains("Muối", StringComparison.OrdinalIgnoreCase));

        // Xử lý 1 nhóm. cot: 0 = Ăn sáng, 1 = Nhà trẻ, 2 = Mẫu giáo.
        // Trả về số lượng thực phẩm mới (hoặc null nếu giữ nguyên) và cập nhật cột gia vị tương ứng.
        decimal[]? XuLyNhom(string ten, int cot, decimal dinhMuc, IReadOnlyList<(decimal Sl, decimal Gia, string? Dvt)> thucPham, bool[]? dongMan = null)
        {
            if (dinhMuc <= 0 || thucPham.Count == 0 || thucPham.All(x => x.Sl <= 0)) return null;

            if (duocLamTron)
            {
                var gv = giaVi.DanhSach.Select(x => (LayCotGiaVi(x, cot), x.DonGia, (string?)x.Dvt)).ToList();
                var r = LamTronVaCanDoiNhom(thucPham, gv, muoi, dinhMuc, giaVi.DanhSach.Select(x => LaDongGao(x.TenHang)).ToArray(), dongMan);
                if (r is not null)
                {
                    var (tp, gvMoi) = r.Value;
                    giaVi.DanhSach = giaVi.DanhSach.Select((x, i) => DatCotGiaVi(x, cot, gvMoi[i])).ToList();
                    return tp;
                }
                // Không cân được (lệch quá lớn) -> chỉ làm tròn cho giống file mẫu, giữ nguyên chênh lệch để lộ lỗi thực đơn
                khongCan.Add(MoTaLech(ten, cot, dinhMuc, thucPham));
                giaVi.DanhSach = giaVi.DanhSach.Select(x => DatCotGiaVi(x, cot, Math.Round(LayCotGiaVi(x, cot), 1, MidpointRounding.AwayFromZero))).ToList();
                return thucPham.Select(x => ChiTietNgayTinhToanService.LamTronTheoDvt(x.Sl, x.Dvt)).ToArray();
            }

            var moi = SoLuongSauCanDoi(thucPham, dinhMuc - giaVi.DanhSach.Sum(x => LayCotGiaVi(x, cot) * x.DonGia), dinhMuc);
            if (moi is null) khongCan.Add(MoTaLech(ten, cot, dinhMuc, thucPham));
            return moi;
        }

        // --- Ăn sáng: gia vị ăn sáng + thực phẩm sáng = định mức ăn sáng
        if (canSang)
        {
            // Hành tím cố định KHÔNG tham gia cân đối: trừ tiền của nó khỏi định mức rồi chỉ cân đối các dòng còn lại.
            var coDinhSang = kq.Sang.Select(x => ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon)).ToList();
            var chiSoSang = Enumerable.Range(0, kq.Sang.Count).Where(i => !coDinhSang[i]).ToList();
            var tienCoDinhSang = kq.Sang.Where((x, i) => coDinhSang[i]).Sum(x => x.SoLuong * x.DonGia);
            var r = XuLyNhom("Ăn sáng", 0, DinhMuc(dmAnSang) - tienCoDinhSang,
                chiSoSang.Select(i => (kq.Sang[i].SoLuong, kq.Sang[i].DonGia, kq.Sang[i].Dvt)).ToList());
            if (r is not null)
            {
                var daySang = kq.Sang.Select(x => x.SoLuong).ToArray();
                for (var j = 0; j < chiSoSang.Count; j++) daySang[chiSoSang[j]] = r[j];
                kq.Sang = kq.Sang.Select((x, i) => x with { SoLuong = daySang[i], ThanhTien = daySang[i] * x.DonGia }).ToList();
                kq.TongTienSang = kq.Sang.Sum(x => x.ThanhTien);
            }
        }

        // --- Nhà trẻ / Mẫu giáo: gia vị + thực phẩm chính + xế = định mức của nhóm
        if (canChinh && kq.Chinh.Count > 0)
        {
            // Mặt hàng cố định theo số trẻ (sữa) KHÔNG tham gia làm tròn/cân đối: trừ tiền của chúng khỏi định mức rồi
            // chỉ cân đối các dòng còn lại, sau đó ghép lại đúng vị trí.
            var coDinh = kq.Chinh.Select(x => ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon)).ToList();
            var chiSoCanDoi = Enumerable.Range(0, kq.Chinh.Count).Where(i => !coDinh[i]).ToList();

            decimal[]? CanDoiChinh(string ten, int cot, decimal dinhMuc, Func<NguyenLieuHaiCotDto, decimal> laySl, string? monMan)
            {
                var tienCoDinh = kq.Chinh.Where((x, i) => coDinh[i]).Sum(x => laySl(x) * x.DonGia);
                static string Khoa(string? t) => ChiTietNgayTinhToanService.TenMonHienThi(t).Trim().ToLowerInvariant();
                var khoaMan = Khoa(monMan);
                var dongMan = khoaMan.Length == 0 ? null : chiSoCanDoi.Select(i => Khoa(kq.Chinh[i].TenMon) == khoaMan).ToArray();
                var r = XuLyNhom(ten, cot, dinhMuc - tienCoDinh,
                    chiSoCanDoi.Select(i => (laySl(kq.Chinh[i]), kq.Chinh[i].DonGia, kq.Chinh[i].Dvt)).ToList(), dongMan);
                if (r is null) return null;
                var day = kq.Chinh.Select(laySl).ToArray();
                for (var j = 0; j < chiSoCanDoi.Count; j++) day[chiSoCanDoi[j]] = r[j];
                return day;
            }

            var moiNt = CanDoiChinh("Nhà trẻ", 1, DinhMuc(dmNhaTre), x => x.SlNhaTre, monManNhaTre);
            var moiMg = CanDoiChinh("Mẫu giáo", 2, DinhMuc(dmMauGiao), x => x.SlMauGiao, monManMauGiao);
            if (moiNt is not null || moiMg is not null)
            {
                kq.Chinh = kq.Chinh.Select((x, i) =>
                {
                    var slNt = moiNt?[i] ?? x.SlNhaTre;
                    var slMg = moiMg?[i] ?? x.SlMauGiao;
                    return x with { SlNhaTre = slNt, SlMauGiao = slMg, TtNhaTre = slNt * x.DonGia, TtMauGiao = slMg * x.DonGia };
                }).ToList();
                kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
                kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
            }
        }

        // --- TRẦN XẾ: tiền xế mỗi suất tối đa 4.500đ (trừ bánh có giá sẵn, cháo xế và các dòng cố định) — vượt thì co các dòng xế còn lại.
        if (canChinh && kq.Chinh.Count > 0)
        {
            const decimal TranXeMotSuat = 4500m;
            static string KhoaMon(string? t) => ChiTietNgayTinhToanService.TenMonHienThi(t).Trim();
            static bool LaBanhCoGiaSan(string? tenHang) => (tenHang ?? "").TrimStart().StartsWith("Bánh", StringComparison.OrdinalIgnoreCase);
            void TranXe(HashSet<string>? tenMonXe, int soSuat, Func<NguyenLieuHaiCotDto, decimal> laySl, Func<NguyenLieuHaiCotDto, decimal, NguyenLieuHaiCotDto> datSl)
            {
                if (tenMonXe is null || tenMonXe.Count == 0 || soSuat <= 0) return;
                // Xế là món bánh có giá tiền sẵn (có dòng tên "Bánh...") -> giữ nguyên giá, không áp trần 4.500đ.
                if (kq.Chinh.Any(x => tenMonXe.Contains(KhoaMon(x.TenMon)) && laySl(x) > 0 && LaBanhCoGiaSan(x.TenHang))) return;
                bool ThuocXe(NguyenLieuHaiCotDto x) => tenMonXe.Contains(KhoaMon(x.TenMon)) && !ChiTietNgayTinhToanService.LaMatHangCoDinh(x.TenHang);
                bool LaDongXe(NguyenLieuHaiCotDto x) => ThuocXe(x) && !ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon);
                // Tiền xế = MỌI dòng của món xế (kể cả nếp/đậu xanh/cà rốt cháo cố định); chỉ co các dòng không cố định.
                var tienXe = kq.Chinh.Where(ThuocXe).Sum(x => laySl(x) * x.DonGia);
                var tienCoDinhXe = kq.Chinh.Where(x => ThuocXe(x) && !LaDongXe(x)).Sum(x => laySl(x) * x.DonGia);
                var tien = tienXe - tienCoDinhXe;
                var tran = TranXeMotSuat * soSuat - tienCoDinhXe;
                if (tienXe <= TranXeMotSuat * soSuat || tien <= 0) return;
                var heSo = Math.Max(0.3m, tran / tien);   // tối thiểu 30% để món không bị mất nguyên liệu chính
                kq.Chinh = kq.Chinh.Select(x => LaDongXe(x)
                    ? datSl(x, ChiTietNgayTinhToanService.DonViCan(x.Dvt) ? Math.Floor(laySl(x) * heSo * 10m) / 10m : ChiTietNgayTinhToanService.LamTronTheoDvt(laySl(x) * heSo, x.Dvt))
                    : x).ToList();
            }
            TranXe(tenMonXeNhaTre, soSuatNhaTre, x => x.SlNhaTre, (x, v) => x with { SlNhaTre = v, TtNhaTre = v * x.DonGia });
            TranXe(tenMonXeMauGiao, soSuatMauGiao, x => x.SlMauGiao, (x, v) => x with { SlMauGiao = v, TtMauGiao = v * x.DonGia });
            // Chả của món XẾ: tối đa 3 kg mỗi khối và làm tròn số nguyên kg.
            kq.Chinh = kq.Chinh.Select(x =>
            {
                if (!ChiTietNgayTinhToanService.LaChaCan(x.TenHang, x.Dvt)) return x;
                var thuocNt = tenMonXeNhaTre is not null && tenMonXeNhaTre.Contains(KhoaMon(x.TenMon));
                var thuocMg = tenMonXeMauGiao is not null && tenMonXeMauGiao.Contains(KhoaMon(x.TenMon));
                var nt = thuocNt ? ChiTietNgayTinhToanService.ApChaToiDa(x.SlNhaTre) : x.SlNhaTre;
                var mg = thuocMg ? ChiTietNgayTinhToanService.ApChaToiDa(x.SlMauGiao) : x.SlMauGiao;
                return x with { SlNhaTre = nt, SlMauGiao = mg, TtNhaTre = nt * x.DonGia, TtMauGiao = mg * x.DonGia };
            }).ToList();
            kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
            kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
        }

        // --- TRẦN TRÁNG MIỆNG: tiền tráng miệng mỗi suất tối đa 2.250đ (đúng bảng tỷ lệ). Món tráng miệng có GIÁ TIỀN SẴN (có dòng tên "Bánh...")
        // thì tính đúng theo giá tiền, không áp trần. Món theo phần (kem flan, sữa chua, hộp, quả…) giữ đủ số phần/trẻ nên chỉ co các dòng tính theo cân (dưa hấu, thanh long…).
        if (canChinh && kq.Chinh.Count > 0)
        {
            const decimal TranTrangMiengMacDinh = 2250m;
            const decimal TranTrangMiengNhoRuby = 2500m;   // Nho Ruby: định mức tráng miệng 2.500đ/suất
            static string KhoaMonTm(string? t) => ChiTietNgayTinhToanService.TenMonHienThi(t).Trim();
            void TranTrangMieng(HashSet<string>? tenMonTm, int soSuat, Func<NguyenLieuHaiCotDto, decimal> laySl, Func<NguyenLieuHaiCotDto, decimal, NguyenLieuHaiCotDto> datSl)
            {
                if (tenMonTm is null || tenMonTm.Count == 0 || soSuat <= 0) return;
                var TranTrangMiengMotSuat = tenMonTm.Any(t => t.StartsWith("Nho Ruby", StringComparison.OrdinalIgnoreCase)) ? TranTrangMiengNhoRuby : TranTrangMiengMacDinh;
                bool ThuocTm(NguyenLieuHaiCotDto x) => tenMonTm.Contains(KhoaMonTm(x.TenMon)) && !ChiTietNgayTinhToanService.LaMatHangCoDinh(x.TenHang);
                if (kq.Chinh.Any(x => ThuocTm(x) && laySl(x) > 0 && ChiTietNgayTinhToanService.LaBanhCoGiaSan(x.TenHang))) return;   // có giá tiền sẵn
                bool CoTheCo(NguyenLieuHaiCotDto x) => ThuocTm(x) && ChiTietNgayTinhToanService.DonViCan(x.Dvt)
                    && !ChiTietNgayTinhToanService.LaDongKhongCanDoi(x.TenHang, x.TenMon);
                var tienTm = kq.Chinh.Where(ThuocTm).Sum(x => laySl(x) * x.DonGia);
                var tienCoThe = kq.Chinh.Where(CoTheCo).Sum(x => laySl(x) * x.DonGia);
                var tran = TranTrangMiengMotSuat * soSuat - (tienTm - tienCoThe);
                if (tienTm <= TranTrangMiengMotSuat * soSuat || tienCoThe <= 0 || tran <= 0) return;
                var heSo = Math.Max(0.3m, tran / tienCoThe);
                kq.Chinh = kq.Chinh.Select(x => CoTheCo(x) ? datSl(x, Math.Floor(laySl(x) * heSo * 10m) / 10m) : x).ToList();
            }
            TranTrangMieng(tenMonTrangMiengNhaTre, soSuatNhaTre, x => x.SlNhaTre, (x, v) => x with { SlNhaTre = v, TtNhaTre = v * x.DonGia });
            TranTrangMieng(tenMonTrangMiengMauGiao, soSuatMauGiao, x => x.SlMauGiao, (x, v) => x with { SlMauGiao = v, TtMauGiao = v * x.DonGia });
            kq.TongTienChinhNhaTre = kq.Chinh.Sum(x => x.TtNhaTre);
            kq.TongTienChinhMauGiao = kq.Chinh.Sum(x => x.TtMauGiao);
        }

        if (khongCan.Count > 0)
        {
            var oTrong = new List<string>();
            if (khongCan.Any(x => x.StartsWith("Mẫu giáo")) && oTrongMauGiao != "") oTrong.Add("Mẫu giáo trống ô " + oTrongMauGiao);
            if (khongCan.Any(x => x.StartsWith("Nhà trẻ")) && oTrongNhaTre != "") oTrong.Add("Nhà trẻ trống ô " + oTrongNhaTre);
            kq.GhiChuCanDoi = "Chưa cân đối — lệch quá 15% định mức: " + string.Join("; ", khongCan)
                + (oTrong.Count > 0 ? ". " + string.Join("; ", oTrong) : "") + " → kiểm tra thực đơn (thiếu món?)";
        }
    }

    private static byte[] DoiBangMauThemeGiongFileMau(byte[] xlsx)
    {
        using var vao = new MemoryStream(xlsx);
        using var ra = new MemoryStream();
        using (var zipVao = new ZipArchive(vao, ZipArchiveMode.Read))
        using (var zipRa = new ZipArchive(ra, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in zipVao.Entries)
            {
                var entryMoi = zipRa.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var luongDoc = entry.Open();
                using var luongGhi = entryMoi.Open();
                if (entry.FullName.Equals("xl/theme/theme1.xml", StringComparison.OrdinalIgnoreCase))
                {
                    string xml;
                    using (var doc = new StreamReader(luongDoc, Encoding.UTF8)) xml = doc.ReadToEnd();
                    xml = Regex.Replace(xml, "(<a:accent2>\\s*<a:srgbClr val=\")[0-9A-Fa-f]{6}(\")", "${1}C0504D${2}");
                    xml = Regex.Replace(xml, "(<a:accent3>\\s*<a:srgbClr val=\")[0-9A-Fa-f]{6}(\")", "${1}9BBB59${2}");
                    var bytes = new UTF8Encoding(false).GetBytes(xml);
                    luongGhi.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    luongDoc.CopyTo(luongGhi);
                }
            }
        }
        return ra.ToArray();
    }

    /// <summary>Sheet "Thực đơn" — ĐÚNG mẫu "Thực đơn tuần ok" gốc: 2 bảng tuần (Thứ 2 →
    /// Thứ 7) xếp dọc, bảng trên "THỰC ĐƠN MẪU GIÁO", bảng dưới "THỰC ĐƠN NHÀ TRẺ", cùng
    /// font Times New Roman, cỡ chữ, viền, merge tiêu đề/chữ ký như file mẫu. Chỉ điền
    /// món cho những ngày ĐÃ CÓ dữ liệu trong tuần (ngày nghỉ hiện "Nghỉ" + ghi chú nếu
    /// có, ngày chưa soạn thực đơn để trống — không suy diễn).</summary>
    private static void GhiSheetThucDonTuan(
        XLWorkbook workbook, DateOnly thuHai,
        List<(DateOnly Ngay, TrangThaiNgay TrangThai, string? GhiChu, List<ThucDonEntity> Mg, List<ThucDonEntity> Nt)> duLieuTuan)
    {
        // Tên sheet đúng như file mẫu để file xuất tải lên lại làm file mẫu cho tuần sau được (MauTuanExcelService tìm đúng tên này).
        var ws = workbook.Worksheets.Add("Thực đơn tuần ok");
        ws.Style.Font.FontName = "Times New Roman";
        ws.Style.Font.FontSize = 14;

        var doRong = new (string Cot, double Rong)[] { ("A", 14), ("B", 19), ("C", 19), ("D", 19), ("E", 19), ("F", 19), ("G", 19) };
        foreach (var (cot, rong) in doRong) ws.Column(cot).Width = rong;

        // A4 ngang, vừa 1 trang theo chiều ngang (chiều dọc tự do: mỗi bảng Mẫu giáo / Nhà trẻ một trang).
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.Left = 0.39;
        ws.PageSetup.Margins.Right = 0.39;
        ws.PageSetup.Margins.Top = 0.5;
        ws.PageSetup.Margins.Bottom = 0.5;

        string[] tenThu = { "THỨ 2", "THỨ 3", "THỨ 4", "THỨ 5", "THỨ 6", "THỨ 7" };
        var thuBay = thuHai.AddDays(5);
        // Tiêu đề ghi rõ "TUẦN n THÁNG m" (n = thứ tự tuần trong tháng, cùng cách đặt tên file "TUẦN n THÁNG m A/B/...") + khoảng ngày của tuần.
        var (namTuan, thangTuan) = LichSuThucDonViewModel.ThangCuaTuan(thuHai);
        var soTuan = Math.Max(1, LichSuThucDonViewModel.CacTuanCuaThang(namTuan, thangTuan).IndexOf(thuHai) + 1);
        // Sữa uống mỗi ngày — như file mẫu: bảng Mẫu giáo có 1 dòng phụ (không nhãn) dưới "XẾ"; bảng Nhà trẻ thì hàng "XẾ" chính là dòng sữa
        // (món xế thật của Nhà trẻ nằm ở "CHÍNH CHIỀU"). Chỉ ghi cho ngày học bình thường ĐÃ có thực đơn (ngày nghỉ / chưa soạn để trống).
        const string TenSuaMoiNgay = "Sữa Ellac Multi";

        static void OTieuDe(IXLCell c, string text, double size, bool bold, bool italic = false)
        {
            c.Value = text;
            c.Style.Font.FontSize = size;
            c.Style.Font.Bold = bold;
            c.Style.Font.Italic = italic;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Alignment.WrapText = true;
        }

        // Trả về dòng kế tiếp sau khối vừa ghi (để ghi khối Nhà trẻ nối tiếp bên dưới).
        int GhiMotKhoi(int hangBatDau, string tenKhoiHoa, string tenBang, List<string> cacBua, bool themDongSuaRieng, Func<(DateOnly, TrangThaiNgay, string?, List<ThucDonEntity>, List<ThucDonEntity>), List<ThucDonEntity>> layMonTheoKhoi)
        {
            var h = hangBatDau;

            ws.Range(h, 1, h, 3).Merge();
            OTieuDe(ws.Cell(h, 1), "UBND PHƯỜNG NGŨ HÀNH SƠN", 13, bold: true);
            h++;
            ws.Range(h, 1, h, 3).Merge();
            OTieuDe(ws.Cell(h, 1), "TRƯỜNG MẦM NON BẠCH DƯƠNG", 13, bold: true);
            h++;

            ws.Range(h, 1, h, 7).Merge();
            OTieuDe(ws.Cell(h, 1), $"THỰC ĐƠN {tenKhoiHoa} TUẦN {soTuan} THÁNG {thangTuan} ({thuHai.Day:00}/{thuHai.Month:00}/{thuHai.Year} - {thuBay.Day:00}/{thuBay.Month:00}/{thuBay.Year})", 14, bold: true);
            h++;
            h++; // dòng đệm, giống file mẫu

            var hangHeader = h;
            // Ô tiêu đề cột đầu để " " (bảng Excel không cho tiêu đề cột rỗng) — nằm trong "bảng" nên
            // chữ tiêu đề màu trắng trên nền màu của kiểu bảng, giống file mẫu.
            OTieuDe(ws.Cell(h, 1), " ", 13, bold: true);
            for (var i = 0; i < 6; i++)
            {
                var c = ws.Cell(h, 2 + i);
                OTieuDe(c, tenThu[i], 13, bold: true);
                c.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            }
            ws.Range(h, 1, h, 7).Style.Font.FontColor = XLColor.White;
            // Nền tiêu đề đặt thẳng vào ô (cùng màu với kiểu bảng) để chữ trắng vẫn đọc được ở LibreOffice / xem trước / điện thoại.
            ws.Range(h, 1, h, 7).Style.Fill.BackgroundColor = XLColor.FromHtml(tenKhoiHoa == "MẪU GIÁO" ? "#9BBB59" : "#C0504D");
            ws.Range(hangHeader, 1, hangHeader, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            h++;

            var daGhiNghi = new bool[6];   // ngày nghỉ chỉ ghi chữ "Nghỉ…" ở dòng bữa đầu tiên
            void GhiDongBua(string? nhan, Func<(DateOnly, TrangThaiNgay, string?, List<ThucDonEntity>, List<ThucDonEntity>), string> layTen)
            {
                if (nhan is not null)
                {
                    OTieuDe(ws.Cell(h, 1), nhan, 13, bold: true);
                }
                for (var i = 0; i < 6; i++)
                {
                    var d = duLieuTuan[i];
                    var c = ws.Cell(h, 2 + i);
                    c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    c.Style.Alignment.WrapText = true;
                    c.Style.Font.FontSize = 14;

                    if (d.Item2 != TrangThaiNgay.HocBinhThuong)
                    {
                        if (!daGhiNghi[i])
                        {
                            daGhiNghi[i] = true;
                            c.Value = d.Item2 == TrangThaiNgay.NghiLe ? ("Nghỉ lễ" + (string.IsNullOrWhiteSpace(d.Item3) ? "" : $" — {d.Item3}")) : ("Nghỉ" + (string.IsNullOrWhiteSpace(d.Item3) ? "" : $" — {d.Item3}"));
                        }
                    }
                    else
                    {
                        c.Value = layTen(d);
                    }
                }
                // Dòng cao 42,45 như mẫu (đủ 2 dòng chữ); món dài hơn thì thêm chiều cao để chữ xuống dòng không bị cắt.
                var dai = 0;
                for (var i = 0; i < 6; i++) dai = Math.Max(dai, ws.Cell(h, 2 + i).GetString().Length);
                var soDongChu = Math.Max(2, (int)Math.Ceiling(dai / 15.0));
                ws.Row(h).Height = soDongChu <= 2 ? 42.45 : 19 * soDongChu;
                h++;
            }

            foreach (var tenBua in cacBua)
            {
                var buaHienTai = tenBua;
                var laXeNhaTre = tenKhoiHoa == "NHÀ TRẺ" && buaHienTai == "XẾ";   // hàng XẾ của Nhà trẻ = sữa (nếu không có món thật)
                GhiDongBua(buaHienTai, d =>
                {
                    var ten = ChiTietNgayTinhToanService.TenMonHienThi(layMonTheoKhoi(d).FirstOrDefault(t => t.LoaiBuaAn.TenBua == buaHienTai)?.MonAn?.TenMon);
                    return laXeNhaTre && string.IsNullOrWhiteSpace(ten) && layMonTheoKhoi(d).Count > 0 ? TenSuaMoiNgay : ten;
                });
            }
            if (themDongSuaRieng)
            {
                // Dòng "Sữa" phụ dưới XẾ (không gắn với bữa nào trong hệ thống): ghi "Sữa Ellac Multi" cho mỗi ngày đã có thực đơn.
                GhiDongBua(null, d => d.Item4.Count > 0 ? TenSuaMoiNgay : "");
            }

            // Biến vùng "tiêu đề thứ + các bữa" thành BẢNG EXCEL có kiểu màu y hệt file mẫu:
            // Mẫu giáo = TableStyleMedium4 (xanh lá), Nhà trẻ = TableStyleMedium10 (đỏ), có sọc dòng.
            var bang = ws.Range(hangHeader, 1, h - 1, 7).CreateTable(tenBang);
            bang.Theme = tenKhoiHoa == "MẪU GIÁO" ? XLTableTheme.TableStyleMedium4 : XLTableTheme.TableStyleMedium10;
            bang.ShowRowStripes = true;
            bang.ShowAutoFilter = false;

            h++; // dòng đệm trước chữ ký
            ws.Range(h, 5, h, 7).Merge();
            OTieuDe(ws.Cell(h, 5), $"Ngũ Hành Sơn, ngày {DateTime.Now.Day:00} tháng {DateTime.Now.Month:00} năm {DateTime.Now.Year}", 14, bold: false, italic: true);
            h++;
            ws.Range(h, 1, h, 3).Merge();
            OTieuDe(ws.Cell(h, 1), "Người lập", 14, bold: true);
            ws.Range(h, 5, h, 7).Merge();
            OTieuDe(ws.Cell(h, 5), "Hiệu trưởng", 14, bold: true);
            h += 3; // chỗ trống để ký tên
            ws.Range(h, 1, h, 3).Merge();
            OTieuDe(ws.Cell(h, 1), "Nguyễn Thị Thanh Thảo", 14, bold: true);
            ws.Range(h, 5, h, 7).Merge();
            OTieuDe(ws.Cell(h, 5), "Huỳnh Thị Thiên", 14, bold: true);
            h++;

            h++; // dòng đệm giữa 2 khối
            return h;
        }

        var hangTiep = GhiMotKhoi(1, "MẪU GIÁO", "ThucDonMauGiao", new List<string> { "SÁNG", "CANH", "MẶN", "XÀO", "TRÁNG MIỆNG", "XẾ" }, themDongSuaRieng: true, d => d.Item4);
        ws.PageSetup.AddHorizontalPageBreak(hangTiep - 1);   // bảng Nhà trẻ bắt đầu trang mới
        GhiMotKhoi(hangTiep, "NHÀ TRẺ", "ThucDonNhaTre", new List<string> { "SÁNG", "CANH", "MẶN", "TRÁNG MIỆNG", "XẾ", "CHÍNH CHIỀU" }, themDongSuaRieng: false, d => d.Item5);
    }

    /// <summary>Ghi ĐÚNG 1 khối "BẢNG KÊ THỰC ĐƠN - CHI TIỀN CHỢ" của 1 ngày vào sheet `ws`,
    /// bắt đầu từ dòng `hangBatDau` — dùng lặp lại 6 lần/sheet (Thứ 2 → Thứ 7) để ra đúng
    /// cấu trúc "nhiều khối xếp dọc trong 1 sheet" của file mẫu. Trả về dòng bắt đầu cho
    /// khối kế tiếp (đã chừa khoảng trống).</summary>
    // Số nguyên hiện "8", số lẻ hiện "16.1" (định dạng "#,##0.###" của Excel hiện "8." có dấu chấm thừa).
    // Chữ ĐỎ ở mục 3 (thực phẩm sáng) và mục 4 (ăn chính + xế), tô cả dòng từ cột B → J, đúng như file mẫu tuần.
    // Đối chiếu 7 file mẫu tháng 9-10 (21 sheet Chợ), những hàng LUÔN đỏ (87–100% số dòng):
    //   - Gia vị/rau thơm: Hành tím, Hành ngò, Hành lá
    //   - Sữa: Sữa Ellac Multi, Sữa Vinamilk, Sữa Darifa A+
    //   - Bánh sản phẩm: Bánh Flan caramel, Bánh bèo tôm khô, Bánh bông lan mềm
    // KHÔNG đỏ: Hành Tây, dầu mè, đường, Cholimex, xì dầu, dầu hào... và mục 2 (xuất kho gia vị) luôn màu đen.
    // Chỉ đỏ lác đác (<= 50% số dòng, không tô theo quy tắc): Đường vàng, Ngò (mùi), Sữa chua lên men tự nhiên, Ếch,
    // Bột năng, Bột bắp... — để nguyên màu đen.
    private static readonly string[] TenHangChuDo =
    {
        "Hành tím", "Hành ngò", "Hành lá",
        "Sữa Ellac", "Sữa Darifa",
        "Bánh Flan caramel", "Bánh bèo tôm khô", "Bánh bông lan mềm",
    };

    // "Sữa Vinamilk" phải khớp đúng tên (các loại "Sữa vinamilk NC/ADM 110ml..." là mặt hàng khác, mẫu không tô đỏ).
    private static readonly string[] TenHangChuDoKhopDung = { "Sữa Vinamilk" };

    private static bool LaHangChuDo(string? tenHang)
    {
        if (string.IsNullOrWhiteSpace(tenHang)) return false;
        var t = tenHang.Trim();
        return TenHangChuDo.Any(x => t.StartsWith(x, StringComparison.OrdinalIgnoreCase))
            || TenHangChuDoKhopDung.Any(x => string.Equals(t, x, StringComparison.OrdinalIgnoreCase));
    }

    private static string DinhDangSoLuong(decimal v) => v == decimal.Truncate(v) ? "#,##0" : "#,##0.0##";

    // Tên món dùng nguyên liệu này (nhỏ, nghiêng, xám) — giúp bếp biết mỗi dòng thuộc món nào trong thực đơn.
    private static void GhiTenMon(IXLCell cell, string? tenMon)
    {
        if (string.IsNullOrWhiteSpace(tenMon)) return;
        cell.Value = ChiTietNgayTinhToanService.TenMonHienThi(tenMon);
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontSize = 9;
        cell.Style.Font.FontColor = XLColor.FromHtml("#7A7266");
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    private static int GhiKhoiChoMotNgay(
        IXLWorksheet ws, int hangBatDau, string? tenCoSo, DateOnly ngay,
        List<ThucDonEntity> thucDonMg, List<ThucDonEntity> thucDonNt,
        DinhMucAnNgay? dmAnSang, DinhMucAnNgay? dmMauGiao, DinhMucAnNgay? dmNhaTre,
        ChiTietNgayTinhToanResponse kq, GiaViThatDto giaViThat, BangGia bangGia, string? maCoSo = null)
    {
        var h0 = hangBatDau - 1; // cộng vào mọi số dòng "tuyệt đối" cũ (vốn viết cho khối bắt đầu ở dòng 1)

        static void Tieude(IXLCell c, string text, int size = 12, bool italic = false, XLAlignmentHorizontalValues? align = null)
        {
            c.Value = text;
            c.Style.Font.Bold = true;
            c.Style.Font.FontSize = size;
            c.Style.Font.Italic = italic;
            c.Style.Alignment.Horizontal = align ?? XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        static void OThuong(IXLCell c, XLAlignmentHorizontalValues align = XLAlignmentHorizontalValues.Center)
        {
            c.Style.Alignment.Horizontal = align;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        static void VienBang(IXLRange range)
        {
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        var nhomMon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in thucDonMg.Concat(thucDonNt).OrderBy(x => x.LoaiBuaAn.ThuTu))
        {
            if (td.MonAn is null) continue;
            var nhom = td.LoaiBuaAn.TenBua.Trim().ToUpperInvariant() switch
            {
                "CANH" => "Canh",
                "MẶN" => "Mặn",
                "XÀO" => "Xào",
                "TRÁNG MIỆNG" => "Tráng miệng",
                "XẾ" or "CHÍNH CHIỀU" => "Xế",
                _ => null,
            };
            if (nhom is null) continue;
            var tenMon = ChiTietNgayTinhToanService.TenMonHienThi(td.MonAn.TenMon);
            if (tenMon.Length > 0) nhomMon.TryAdd(tenMon, nhom);
        }

        // ================= Khối 1: tiêu đề + "1. Thực đơn" + bảng ngân sách trong ngày =================
        ws.Range(h0 + 1, 1, h0 + 1, 3).Merge();
        Tieude(ws.Cell(h0 + 1, 1), TenDonVi);
        ws.Range(h0 + 1, 4, h0 + 1, 10).Merge();
        Tieude(ws.Cell(h0 + 1, 4), "BẢNG KÊ THỰC ĐƠN - CHI TIỀN CHỢ", 16);

        ws.Range(h0 + 2, 1, h0 + 2, 3).Merge();
        Tieude(ws.Cell(h0 + 2, 1), (tenCoSo ?? string.Empty).ToUpperInvariant());
        ws.Range(h0 + 2, 4, h0 + 2, 10).Merge();
        Tieude(ws.Cell(h0 + 2, 4), "THEO DÕI XUẤT KHO", 16);

        ws.Cell(h0 + 3, 1).Value = "1. Thực đơn :";
        ws.Cell(h0 + 3, 1).Style.Font.Bold = true;
        ws.Range(h0 + 3, 7, h0 + 3, 10).Merge();
        Tieude(ws.Cell(h0 + 3, 7), $"{ThuTrongTuan(ngay)}, ngày {ngay.Day} tháng {ngay.Month} năm {ngay.Year}", italic: true, align: XLAlignmentHorizontalValues.Right);
        ws.Cell(h0 + 3, 7).Style.Font.Bold = false;
        ws.Cell(h0 + 3, 7).Style.Font.Italic = true;

        string MonCua(string tenBua) => ChiTietNgayTinhToanService.TenMonHienThi(thucDonMg.FirstOrDefault(t => t.LoaiBuaAn.TenBua == tenBua)?.MonAn?.TenMon);
        string MonSangCua(List<ThucDonEntity> ds) => ChiTietNgayTinhToanService.TenMonHienThi(ds.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "SÁNG")?.MonAn?.TenMon);

        void DongMon(int hang, string? nhan, string tenBua)
        {
            if (nhan is not null)
            {
                ws.Cell(hang, 1).Value = nhan;
                ws.Cell(hang, 1).Style.Font.Bold = true;
                ws.Cell(hang, 1).Style.Font.Italic = true;
            }
            ws.Cell(hang, 2).Value = MonCua(tenBua);
            ws.Cell(hang, 2).Style.Font.Bold = true;
            ws.Cell(hang, 2).Style.Font.Italic = true;
        }

        // Bữa SÁNG dùng chung cho cả 2 khối — hiện đúng 1 món.
        var monSangMg = MonSangCua(thucDonMg);
        ws.Cell(h0 + 4, 1).Value = "Sáng : ";
        ws.Cell(h0 + 4, 1).Style.Font.Bold = true;
        ws.Cell(h0 + 4, 1).Style.Font.Italic = true;
        ws.Cell(h0 + 4, 2).Value = monSangMg;
        ws.Cell(h0 + 4, 2).Style.Font.Bold = true;
        ws.Cell(h0 + 4, 2).Style.Font.Italic = true;
        DongMon(h0 + 5, " Trưa :", "CANH");
        DongMon(h0 + 6, null, "MẶN");
        DongMon(h0 + 7, null, "XÀO");
        DongMon(h0 + 8, "Tráng miệng: ", "TRÁNG MIỆNG");
        DongMon(h0 + 9, "Xế chiều:", "XẾ");
        var xeHaiMon = false;
        {
            // Xế chiều của Mẫu giáo (bữa XẾ) và Nhà trẻ (bữa CHÍNH CHIỀU) có thể là 2 món khác nhau: hiện đủ cả hai (trước đây chỉ hiện món Mẫu giáo).
            var xeMg = MonCua("XẾ");
            var xeNt = ChiTietNgayTinhToanService.TenMonHienThi(thucDonNt.FirstOrDefault(t => t.LoaiBuaAn.TenBua == "CHÍNH CHIỀU")?.MonAn?.TenMon);
            var xeKhacNhau = xeNt.Length > 0 && !string.Equals(xeNt, xeMg, StringComparison.OrdinalIgnoreCase);
            if (xeKhacNhau && xeMg.Length > 0)
            {
                // 2 món khác nhau: mỗi món 1 dòng riêng (Mẫu giáo ở dòng Xế chiều, Nhà trẻ ở dòng kế); dòng sữa xuống dòng ngay dưới món Nhà trẻ cùng ô.
                ws.Cell(h0 + 9, 2).Value = xeMg + " (MG)";
                ws.Cell(h0 + 10, 2).Value = xeNt + " (NT)\nSữa Ellac Multi";
                ws.Cell(h0 + 10, 2).Style.Alignment.WrapText = true;
                ws.Cell(h0 + 10, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(h0 + 10).Height = 36;
                xeHaiMon = true;
            }
            else if (xeKhacNhau)
                ws.Cell(h0 + 9, 2).Value = xeNt + " (NT)";
        }
        // Dòng "Sữa Ellac Multi" dưới Xế chiều như file mẫu (sữa uống mỗi ngày, tính tự động theo số trẻ).
        if (!xeHaiMon) ws.Cell(h0 + 10, 2).Value = "Sữa Ellac Multi";
        ws.Cell(h0 + 10, 2).Style.Font.Bold = true;
        ws.Cell(h0 + 10, 2).Style.Font.Italic = true;

        // Bảng mini ngân sách ngày, góc phải trên — cột G=Tổng cộng, H=Ăn sáng, I=Nhà trẻ, J=Mẫu giáo
        string[] nhanNganSach = { "Tổng cộng", "Ăn sáng", "Nhà trẻ", "Mẫu giáo" };
        for (var i = 0; i < 4; i++)
        {
            var c = ws.Cell(h0 + 5, 7 + i);
            c.Value = nhanNganSach[i];
            c.Style.Font.Bold = true;
            OThuong(c);
            c.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            c.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            c.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            c.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        }

        void DongTien(int hang, string nhan, decimal? tongCong, decimal? anSang, decimal nhaTre, decimal mauGiao, bool tongLaFormula = false)
        {
            ws.Cell(hang, 4).Value = nhan;
            ws.Cell(hang, 4).Style.Font.Bold = false;
            if (tongLaFormula)
                ws.Cell(hang, 7).FormulaA1 = $"=H{hang}+I{hang}+J{hang}";
            else if (tongCong is not null)
                ws.Cell(hang, 7).Value = tongCong.Value;
            if (anSang is not null) ws.Cell(hang, 8).Value = anSang.Value;
            ws.Cell(hang, 9).Value = nhaTre;
            ws.Cell(hang, 10).Value = mauGiao;
            foreach (var col in new[] { 7, 8, 9, 10 })
            {
                ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0";
                OThuong(ws.Cell(hang, col));
            }
        }

        DongTien(h0 + 6, "Số tiền ăn trên 1 cháu:", null, dmAnSang?.TienAn1Chau ?? 10000, dmNhaTre?.TienAn1Chau ?? 29000, dmMauGiao?.TienAn1Chau ?? 29000);
        DongTien(h0 + 7, "Xuất ăn trong ngày: ", null, dmAnSang?.SoSuatAn ?? 0, dmNhaTre?.SoSuatAn ?? 0, dmMauGiao?.SoSuatAn ?? 0);
        ws.Cell(h0 + 7, 11).FormulaA1 = $"=I{h0 + 7}+J{h0 + 7}";
        DongTien(h0 + 8, "Số tiền được phép chi chợ:", null, dmAnSang?.TienDuocPhepChi ?? 0, dmNhaTre?.TienDuocPhepChi ?? 0, dmMauGiao?.TienDuocPhepChi ?? 0, tongLaFormula: true);
        DongTien(h0 + 9, "Tổng chi tiền chợ trong ngày :", null,
            giaViThat.TongAnSang + kq.TongTienSang,
            giaViThat.TongNhaTre + kq.TongTienChinhNhaTre,
            giaViThat.TongMauGiao + kq.TongTienChinhMauGiao,
            tongLaFormula: true);
        ws.Cell(h0 + 10, 4).Value = "Chênh lệch cuối ngày:";
        if (!string.IsNullOrEmpty(kq.GhiChuCanDoi))
        {
            ws.Cell(h0 + 10, 15).Value = kq.GhiChuCanDoi;
            ws.Cell(h0 + 10, 15).Style.Font.FontColor = XLColor.Red;
            ws.Cell(h0 + 10, 15).Style.Font.Italic = true;
        }
        ws.Cell(h0 + 10, 7).FormulaA1 = $"=G{h0 + 8}-G{h0 + 9}";
        ws.Cell(h0 + 10, 8).FormulaA1 = $"=H{h0 + 8}-H{h0 + 9}";
        ws.Cell(h0 + 10, 9).FormulaA1 = $"=I{h0 + 8}-I{h0 + 9}";
        ws.Cell(h0 + 10, 10).FormulaA1 = $"=J{h0 + 8}-J{h0 + 9}";
        foreach (var col in new[] { 7, 8, 9, 10 })
        {
            ws.Cell(h0 + 10, col).Style.NumberFormat.Format = "#,##0";
            OThuong(ws.Cell(h0 + 10, col));
        }

        var hang = h0 + 11;

        // ================= Mục 2: Theo dõi xuất kho gia vị hằng ngày (3 cột SL/TT) =================
        ws.Cell(hang, 1).Value = "  2. Theo dõi xuất kho gia vị hằng ngày :";
        ws.Cell(hang, 1).Style.Font.Bold = true;
        hang++;

        var dauMuc2 = hang;
        ws.Range(hang, 1, hang + 1, 1).Merge(); Tieude(ws.Cell(hang, 1), "Số TT");
        ws.Range(hang, 2, hang + 1, 2).Merge(); Tieude(ws.Cell(hang, 2), "Thực phẩm", align: XLAlignmentHorizontalValues.Left);
        ws.Range(hang, 3, hang + 1, 3).Merge(); Tieude(ws.Cell(hang, 3), "ĐVT");
        ws.Range(hang, 4, hang, 6).Merge(); Tieude(ws.Cell(hang, 4), "Số lượng xuất gia vị");
        ws.Range(hang, 7, hang + 1, 7).Merge(); Tieude(ws.Cell(hang, 7), "Đơn giá");
        ws.Range(hang, 8, hang, 10).Merge(); Tieude(ws.Cell(hang, 8), "Thành tiền");
        hang++;
        Tieude(ws.Cell(hang, 4), "ăn sáng"); Tieude(ws.Cell(hang, 5), "Nhà trẻ"); Tieude(ws.Cell(hang, 6), "Mẫu giáo");
        Tieude(ws.Cell(hang, 8), "ăn sáng"); Tieude(ws.Cell(hang, 9), "Nhà trẻ"); Tieude(ws.Cell(hang, 10), "Mẫu giáo");
        hang++;

        var dauDuLieu2 = hang;
        var stt = 1;
        var dongGiaVi = new List<(int Hang, string TenHang, string? TenMon)>();
        foreach (var d in giaViThat.DanhSach
            .OrderBy(x => x.TenHang.Contains("Gạo", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.TenHang, StringComparer.OrdinalIgnoreCase))
        {
            dongGiaVi.Add((hang, d.TenHang, null));
            ws.Cell(hang, 1).Value = stt++;
            ws.Cell(hang, 2).Value = d.TenHang; ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;
            ws.Cell(hang, 3).Value = d.Dvt;
            ws.Cell(hang, 4).Value = d.SlAnSang;
            ws.Cell(hang, 5).Value = d.SlNhaTre;
            ws.Cell(hang, 6).Value = d.SlMauGiao;
            if (!giaViThat.CoPhieu && LaDongGao(d.TenHang))
            {
                // Số gạo trong dữ liệu đã được cân đối: trùng quy tắc (suất × 0,055 / 0,065, hoặc cộng thêm 0,5 kg) thì ghi công thức
                // như file mẫu, khác thì ghi đúng số đã cân đối — KHÔNG ghi đè nữa (tránh lệch tiền cuối ngày).
                var baseNt = (dmNhaTre?.SoSuatAn ?? 0) * GaoNhaTreMotSuat;
                var baseMg = (dmMauGiao?.SoSuatAn ?? 0) * GaoMauGiaoMotSuat;
                if (Math.Abs(d.SlNhaTre - baseNt) < 0.00001m) ws.Cell(hang, 5).FormulaA1 = $"=I{h0 + 7}*0.055";
                else if (Math.Abs(d.SlNhaTre - baseNt - 0.5m) < 0.00001m) ws.Cell(hang, 5).FormulaA1 = $"=I{h0 + 7}*0.055+0.5";
                if (Math.Abs(d.SlMauGiao - baseMg) < 0.00001m) ws.Cell(hang, 6).FormulaA1 = $"=J{h0 + 7}*0.065";
                else if (Math.Abs(d.SlMauGiao - baseMg - 0.5m) < 0.00001m) ws.Cell(hang, 6).FormulaA1 = $"=J{h0 + 7}*0.065+0.5";
            }
            ws.Cell(hang, 7).Value = d.DonGia;
            // Muối = phần đệm cuối cùng: số lượng = SỐ TIỀN ÷ ĐƠN GIÁ như file mẫu (=3060/G18)
            if (d.TenHang.Contains("Muối", StringComparison.OrdinalIgnoreCase) && d.DonGia > 0)
            {
                void MuoiTheoTien(int cot, decimal sl)
                {
                    var tien = Math.Round(sl * d.DonGia, 0);
                    if (tien > 0 && Math.Abs(tien - sl * d.DonGia) < 0.01m) ws.Cell(hang, cot).FormulaA1 = $"={tien.ToString(System.Globalization.CultureInfo.InvariantCulture)}/G{hang}";
                }
                MuoiTheoTien(4, d.SlAnSang); MuoiTheoTien(5, d.SlNhaTre); MuoiTheoTien(6, d.SlMauGiao);
            }
            // Thành tiền = số lượng × đơn giá (công thức như file mẫu)
            ws.Cell(hang, 8).FormulaA1 = $"=D{hang}*G{hang}";
            ws.Cell(hang, 9).FormulaA1 = $"=E{hang}*G{hang}";
            ws.Cell(hang, 10).FormulaA1 = $"=F{hang}*G{hang}";
            var maGv = bangGia.MaNeuTraDuoc(d.HangHoaId, d.TenHang, d.Dvt, d.DonGia);
            if (maGv is not null) GhiMaVaTra(ws, hang, maGv, 2, 3, 7);
            ws.Cell(hang, 4).Style.NumberFormat.Format = DinhDangSoLuong(d.SlAnSang);
            ws.Cell(hang, 5).Style.NumberFormat.Format = DinhDangSoLuong(d.SlNhaTre);
            ws.Cell(hang, 6).Style.NumberFormat.Format = DinhDangSoLuong(d.SlMauGiao);
            foreach (var col in new[] { 7, 8, 9, 10 }) ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0";
            foreach (var col in Enumerable.Range(1, 10)) OThuong(ws.Cell(hang, col));
            ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;
            hang++;
        }
        var cuoiDuLieu2 = hang - 1;
        var hangTongGv = hang;
        ws.Cell(hang, 2).Value = "Tổng cộng"; ws.Cell(hang, 2).Style.Font.Bold = true;
        if (giaViThat.DanhSach.Count > 0)
        {
            ws.Cell(hang, 8).FormulaA1 = $"=SUM(H{dauDuLieu2}:H{cuoiDuLieu2})";
            ws.Cell(hang, 9).FormulaA1 = $"=SUM(I{dauDuLieu2}:I{cuoiDuLieu2})";
            ws.Cell(hang, 10).FormulaA1 = $"=SUM(J{dauDuLieu2}:J{cuoiDuLieu2})";
        }
        foreach (var col in new[] { 8, 9, 10 }) { ws.Cell(hang, col).Style.Font.Bold = true; ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0"; OThuong(ws.Cell(hang, col)); }
        VienBang(ws.Range(dauMuc2, 1, hang, 10));
        hang++;

        // ================= Mục 3: Chi tiền thực phẩm sáng (1 cột SL/TT) =================
        ws.Cell(hang, 1).Value = "  3. Chi tiền thực phẩm sáng :";
        ws.Cell(hang, 1).Style.Font.Bold = true;
        hang++;

        var dauMuc3 = hang;
        Tieude(ws.Cell(hang, 1), "Số TT"); Tieude(ws.Cell(hang, 2), "Thực phẩm", align: XLAlignmentHorizontalValues.Left); Tieude(ws.Cell(hang, 3), "ĐVT");
        Tieude(ws.Cell(hang, 4), "Số lượng"); Tieude(ws.Cell(hang, 5), "Đơn giá"); Tieude(ws.Cell(hang, 6), "Thành tiền");
        hang++;

        var dauDuLieu3 = hang;
        stt = 1;
        foreach (var d in kq.Sang)
        {
            ws.Cell(hang, 1).Value = stt++;
            ws.Cell(hang, 2).Value = d.TenHang; ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;
            ws.Cell(hang, 3).Value = d.Dvt;
            ws.Cell(hang, 4).Value = d.SoLuong;
            ws.Cell(hang, 4).Style.NumberFormat.Format = DinhDangSoLuong(d.SoLuong);
            ws.Cell(hang, 5).Value = d.DonGia;
            ws.Cell(hang, 6).FormulaA1 = $"=D{hang}*E{hang}";   // Thành tiền = số lượng × đơn giá
            var maSang = bangGia.MaNeuTraDuoc(d.HangHoaId, d.TenHang, d.Dvt, d.DonGia);
            if (maSang is not null) GhiMaVaTra(ws, hang, maSang, 2, 3, 5);
            foreach (var col in new[] { 5, 6 }) ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0";
            foreach (var col in Enumerable.Range(1, 6)) OThuong(ws.Cell(hang, col));
            ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;   // tên thực phẩm căn trái
            if (LaHangChuDo(d.TenHang)) ws.Range(hang, 2, hang, 6).Style.Font.FontColor = XLColor.Red;   // gia vị chữ đỏ như file mẫu
            GhiTenMon(ws.Cell(hang, 7), d.TenMon);
            hang++;
        }
        var cuoiDuLieu3 = hang - 1;
        var hangTongSang = hang;
        ws.Cell(hang, 2).Value = "Tổng cộng"; ws.Cell(hang, 2).Style.Font.Bold = true;
        if (kq.Sang.Count > 0) ws.Cell(hang, 6).FormulaA1 = $"=SUM(F{dauDuLieu3}:F{cuoiDuLieu3})";
        ws.Cell(hang, 6).Style.Font.Bold = true; ws.Cell(hang, 6).Style.NumberFormat.Format = "#,##0"; OThuong(ws.Cell(hang, 6));
        VienBang(ws.Range(dauMuc3, 1, hang, 6));
        hang++;

        // ================= Mục 4: Chi tiền thực phẩm ăn chính + xế (2 cột SL/TT + tổng cộng) =================
        ws.Cell(hang, 1).Value = "  4. Chi tiền thực phẩm ăn chính + xế  trong ngày :";
        ws.Cell(hang, 1).Style.Font.Bold = true;
        hang++;

        var dauMuc4 = hang;
        ws.Range(hang, 1, hang + 1, 1).Merge(); Tieude(ws.Cell(hang, 1), "Số TT");
        ws.Range(hang, 2, hang + 1, 2).Merge(); Tieude(ws.Cell(hang, 2), "Thực phẩm", align: XLAlignmentHorizontalValues.Left);
        ws.Range(hang, 3, hang + 1, 3).Merge(); Tieude(ws.Cell(hang, 3), "ĐVT");
        ws.Range(hang, 4, hang, 5).Merge(); Tieude(ws.Cell(hang, 4), "Số lượng");
        ws.Range(hang, 6, hang + 1, 6).Merge(); Tieude(ws.Cell(hang, 6), "Đơn giá");
        ws.Range(hang, 7, hang, 8).Merge(); Tieude(ws.Cell(hang, 7), "Thành tiền");
        ws.Range(hang, 9, hang, 10).Merge(); Tieude(ws.Cell(hang, 9), "Tổng cộng");
        hang++;
        Tieude(ws.Cell(hang, 4), "Nhà trẻ"); Tieude(ws.Cell(hang, 5), "Mẫu giáo");
        Tieude(ws.Cell(hang, 7), "Nhà trẻ"); Tieude(ws.Cell(hang, 8), "Mẫu giáo");
        Tieude(ws.Cell(hang, 9), "Số lượng"); Tieude(ws.Cell(hang, 10), "Thành tiền");
        hang++;

        var dauDuLieu4 = hang;
        stt = 1;
        var dongThucPham = new List<(int Hang, string TenHang, string? TenMon)>();
        foreach (var d in kq.Chinh)
        {
            dongThucPham.Add((hang, d.TenHang, d.TenMon));
            ws.Cell(hang, 1).Value = stt++;
            ws.Cell(hang, 2).Value = d.TenHang; ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;
            ws.Cell(hang, 3).Value = d.Dvt;
            ws.Cell(hang, 4).Value = d.SlNhaTre;
            ws.Cell(hang, 5).Value = d.SlMauGiao;
            var laSuaEllac = ChiTietNgayTinhToanService.LaMatHangCoDinh(d.TenHang);
            ws.Cell(hang, 6).Value = laSuaEllac ? 240000m : d.DonGia;
            if (laSuaEllac)
            {
                // Sữa Ellac Multi như file mẫu: SL = số suất × 3.000đ ÷ đơn giá (=I7*3000/$F$55). Chỉ ghi công thức khi số trong dữ liệu
                // đúng bằng công thức (số lưu cố định / đã sửa tay thì giữ nguyên số).
                var slNtSua = (dmNhaTre?.SoSuatAn ?? 0) * 3000m / 240000m;
                var slMgSua = (dmMauGiao?.SoSuatAn ?? 0) * 3000m / 240000m;
                if (Math.Abs(d.SlNhaTre - slNtSua) < 0.00001m) ws.Cell(hang, 4).FormulaA1 = $"=I{h0 + 7}*3000/$F${hang}";
                if (Math.Abs(d.SlMauGiao - slMgSua) < 0.00001m) ws.Cell(hang, 5).FormulaA1 = $"=J{h0 + 7}*3000/$F${hang}";
            }
            ws.Cell(hang, 7).FormulaA1 = $"=D{hang}*F{hang}";   // Thành tiền Nhà trẻ = SL × đơn giá
            ws.Cell(hang, 8).FormulaA1 = $"=E{hang}*F{hang}";   // Thành tiền Mẫu giáo
            var maChinh = bangGia.MaNeuTraDuoc(d.HangHoaId, d.TenHang, d.Dvt, d.DonGia);
            if (!laSuaEllac && maChinh is not null) GhiMaVaTra(ws, hang, maChinh, 2, 3, 6);
            ws.Cell(hang, 9).FormulaA1 = $"=D{hang}+E{hang}";
            ws.Cell(hang, 10).FormulaA1 = $"=G{hang}+H{hang}";
            ws.Cell(hang, 4).Style.NumberFormat.Format = DinhDangSoLuong(d.SlNhaTre);
            ws.Cell(hang, 5).Style.NumberFormat.Format = DinhDangSoLuong(d.SlMauGiao);
            ws.Cell(hang, 9).Style.NumberFormat.Format = DinhDangSoLuong(d.SlNhaTre + d.SlMauGiao);
            foreach (var col in new[] { 6, 7, 8, 10 }) ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0";
            foreach (var col in Enumerable.Range(1, 10)) OThuong(ws.Cell(hang, col));
            ws.Cell(hang, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; ws.Cell(hang, 2).Style.Alignment.Indent = 1;   // tên thực phẩm căn trái
            if (LaHangChuDo(d.TenHang)) ws.Range(hang, 2, hang, 10).Style.Font.FontColor = XLColor.Red;   // gia vị chữ đỏ như file mẫu
            GhiTenMon(ws.Cell(hang, 15), d.TenMon);
            hang++;
        }
        var cuoiDuLieu4 = hang - 1;
        var hangTongChinh = hang;
        ws.Cell(hang, 2).Value = "Tổng cộng"; ws.Cell(hang, 2).Style.Font.Bold = true;
        if (kq.Chinh.Count > 0)
        {
            ws.Cell(hang, 7).FormulaA1 = $"=SUM(G{dauDuLieu4}:G{cuoiDuLieu4})";
            ws.Cell(hang, 8).FormulaA1 = $"=SUM(H{dauDuLieu4}:H{cuoiDuLieu4})";
            ws.Cell(hang, 10).FormulaA1 = $"=SUM(J{dauDuLieu4}:J{cuoiDuLieu4})";
        }
        foreach (var col in new[] { 7, 8, 10 }) { ws.Cell(hang, col).Style.Font.Bold = true; ws.Cell(hang, col).Style.NumberFormat.Format = "#,##0"; OThuong(ws.Cell(hang, col)); }
        VienBang(ws.Range(dauMuc4, 1, hang, 10));

        // Bảng ngân sách đầu khối bằng CÔNG THỨC như file mẫu:
        //   Được phép chi = Số tiền/cháu × Xuất ăn ;  Tổng chi = Gia vị + Thực phẩm (lấy từ dòng Tổng cộng bên dưới).
        ws.Cell(h0 + 8, 8).FormulaA1 = $"=H{h0 + 6}*H{h0 + 7}";
        ws.Cell(h0 + 8, 9).FormulaA1 = $"=I{h0 + 6}*I{h0 + 7}";
        ws.Cell(h0 + 8, 10).FormulaA1 = $"=J{h0 + 6}*J{h0 + 7}";
        ws.Cell(h0 + 9, 8).FormulaA1 = $"=H{hangTongGv}+F{hangTongSang}";
        ws.Cell(h0 + 9, 9).FormulaA1 = $"=I{hangTongGv}+G{hangTongChinh}";
        ws.Cell(h0 + 9, 10).FormulaA1 = $"=J{hangTongGv}+H{hangTongChinh}";

        GhiBangPhanTich(ws, h0 + 1, h0 + 7, hangTongGv, dongGiaVi, dongThucPham, nhomMon);

        hang += 2;

        // ================= Chữ ký =================
        ws.Range(hang, 1, hang, 10).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        Tieude(ws.Cell(hang, 1), "Nhà bếp");
        Tieude(ws.Cell(hang, 4), "Thủ kho");
        Tieude(ws.Cell(hang, 6), "Kế toán");
        Tieude(ws.Cell(hang, 8), "Hiệu trưởng");

        // Họ tên người ký (dưới chỗ trống để ký): thủ kho TC khác thủ kho phân hiệu (PH) và điểm trường (ĐT).
        var ma = (maCoSo ?? string.Empty).Trim().ToUpperInvariant();
        var tenThuKho = ma == "TC" ? "Nguyễn Thị Mạnh" : ma is "ĐT" or "DT" ? "" : "Nguyễn Thị Hoàng Yến";   // ĐT: để trống tên thủ kho
        var hangTen = hang + 3;
        if (tenThuKho.Length > 0) Tieude(ws.Cell(hangTen, 4), tenThuKho);
        Tieude(ws.Cell(hangTen, 6), "Nguyễn Thị Thanh Thảo");
        Tieude(ws.Cell(hangTen, 8), "Huỳnh Thị Thiện");

        return hangTen + 3; // chừa 2 dòng trống trước khối (ngày) kế tiếp
    }

    public async Task<byte[]> XuatThucDonNgayAsync(int coSoId, int khoiId, DateOnly ngay)
    {
        var db = _db;

        var coSo = await db.CoSos.FindAsync(coSoId);
        var khoi = await db.KhoiHocs.FindAsync(khoiId);
        var ngayHoc = await db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSoId && n.Ngay == ngay);

        var cacThucDon = ngayHoc is null
            ? new List<ThucDonEntity>()
            : await db.ThucDons
                .Where(t => t.CoSoId == coSoId && t.KhoiId == khoiId && t.NgayHocId == ngayHoc.Id)
                .Include(t => t.LoaiBuaAn)
                .Include(t => t.MonAn).ThenInclude(m => m!.DinhLuongNguyenLieus).ThenInclude(dl => dl.HangHoa).ThenInclude(h => h.Dvt)
                .OrderBy(t => t.LoaiBuaAn.ThuTu)
                .ToListAsync();

        var dinhMucCacNhom = await db.DinhMucAnNgays
            .Include(d => d.NhomChiPhi)
            .Where(d => d.CoSoId == coSoId && d.Ngay == ngay)
            .ToListAsync();

        // Khối chưa có FK chính thức tới nhóm chi phí — dò theo tên (VD "Nhà trẻ (6-18T)"
        // chứa "Nhà trẻ"). Nếu trường tổ chức nhóm chi phí khác quy ước này, sẽ không khớp
        // được và phần đối chiếu ngân sách sẽ bỏ trống thay vì suy đoán sai.
        var dinhMucPhuHop = khoi is null
            ? null
            : dinhMucCacNhom.FirstOrDefault(d =>
                khoi.TenKhoi.Contains(d.NhomChiPhi.TenNhom, StringComparison.OrdinalIgnoreCase) ||
                d.NhomChiPhi.TenNhom.Contains(khoi.TenKhoi, StringComparison.OrdinalIgnoreCase));

        var soSuatAn = dinhMucPhuHop?.SoSuatAn ?? 0;

        var ketQuaNguyenLieu = _nguyenLieuCalc.TinhNhuCauNguyenLieu(
            cacThucDon.Select(td => (td, soSuatAn)), maCoSo: coSo?.MaCoSo);

        using var workbook = new XLWorkbook();
        GhiSheetThucDon(workbook, coSo?.TenCoSo, khoi?.TenKhoi, ngay, cacThucDon);
        GhiSheetCho(workbook, coSo?.TenCoSo, ngay, ketQuaNguyenLieu, dinhMucPhuHop);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void GhiSheetThucDon(XLWorkbook workbook, string? tenCoSo, string? tenKhoi, DateOnly ngay, List<ThucDonEntity> cacThucDon)
    {
        var ws = workbook.Worksheets.Add("Thực đơn");

        ws.Cell(1, 1).Value = $"THỰC ĐƠN {tenKhoi?.ToUpperInvariant()} — {tenCoSo}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = $"Ngày {ngay:dd/MM/yyyy}";

        ws.Cell(4, 1).Value = "Bữa";
        ws.Cell(4, 2).Value = "Món ăn";
        ws.Range(4, 1, 4, 2).Style.Font.Bold = true;

        var hang = 5;
        foreach (var td in cacThucDon)
        {
            ws.Cell(hang, 1).Value = td.LoaiBuaAn.TenBua;
            ws.Cell(hang, 2).Value = td.MonAn is null ? "(chưa chọn)" : ChiTietNgayTinhToanService.TenMonHienThi(td.MonAn.TenMon);
            hang++;
        }

        ws.Columns().AdjustToContents();
    }

    private void GhiSheetCho(
        XLWorkbook workbook,
        string? tenCoSo,
        DateOnly ngay,
        IReadOnlyList<KetQuaNguyenLieu> ketQuaNguyenLieu,
        DinhMucAnNgay? dinhMuc)
    {
        var ws = workbook.Worksheets.Add("Chợ");

        ws.Cell(1, 1).Value = $"NHU CẦU NGUYÊN LIỆU — {tenCoSo} — Ngày {ngay:dd/MM/yyyy}";
        ws.Cell(1, 1).Style.Font.Bold = true;

        ws.Cell(3, 1).Value = "Nguyên liệu";
        ws.Cell(3, 2).Value = "ĐVT";
        ws.Cell(3, 3).Value = "Số lượng cần";
        ws.Cell(3, 4).Value = "Thành tiền";
        ws.Range(3, 1, 3, 4).Style.Font.Bold = true;

        var hang = 4;
        foreach (var kq in ketQuaNguyenLieu)
        {
            ws.Cell(hang, 1).Value = kq.HangHoa.TenHang;
            ws.Cell(hang, 2).Value = kq.HangHoa.Dvt?.TenDvt;
            ws.Cell(hang, 3).Value = kq.TongCanDung;
            ws.Cell(hang, 4).Value = kq.ThanhTien;
            ws.Cell(hang, 4).Style.NumberFormat.Format = "#,##0";
            hang++;
        }

        var hangTongThucTe = hang;
        ws.Cell(hangTongThucTe, 1).Value = "Tổng cộng";
        ws.Cell(hangTongThucTe, 1).Style.Font.Bold = true;
        // Dùng công thức SUM thay vì số cố định — nếu người dùng chỉnh sửa số liệu
        // trực tiếp trong Excel sau này, tổng vẫn tự tính đúng (khác lỗi ở file gốc)
        ws.Cell(hangTongThucTe, 4).FormulaA1 = ketQuaNguyenLieu.Any()
            ? $"=SUM(D4:D{hangTongThucTe - 1})"
            : "0";
        ws.Cell(hangTongThucTe, 4).Style.Font.Bold = true;
        ws.Cell(hangTongThucTe, 4).Style.NumberFormat.Format = "#,##0";

        if (dinhMuc is not null)
        {
            var tongThucTe = ketQuaNguyenLieu.Sum(k => k.ThanhTien);
            var ketQuaNganSach = _nganSachCalc.TinhNganSach(dinhMuc, tongThucTe);

            var hangNganSach = hangTongThucTe + 2;
            ws.Cell(hangNganSach, 1).Value = $"Số tiền được phép chi ({dinhMuc.NhomChiPhi.TenNhom}, {dinhMuc.SoSuatAn} suất)";
            ws.Cell(hangNganSach, 4).Value = ketQuaNganSach.SoTienDuocPhepChi;
            ws.Cell(hangNganSach + 1, 1).Value = "Chênh lệch";
            ws.Cell(hangNganSach + 1, 4).Value = ketQuaNganSach.ChenhLech;
            ws.Range(hangNganSach, 4, hangNganSach + 1, 4).Style.NumberFormat.Format = "#,##0";
        }

        ws.Columns().AdjustToContents();
    }
}
