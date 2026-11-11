using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// Trang "Chi tiết chợ" — bản xem trực quan 1 ngày x 1 cơ sở (TC/PH/ĐT), đúng bố cục
/// 4 mục của bảng kê "Chợ ..." trong file Excel gốc (Thực đơn / Đối chiếu ngân sách /
/// Gia vị hằng ngày / Thực phẩm sáng / Thực phẩm chính + xế), dựng từ dữ liệu THẬT đã
/// có sẵn trong CSDL — không tính lại từ công thức món ăn (xem ghi chú trong
/// <see cref="ViewModels.ChiTietChoViewModel"/>).
///
/// Được liên kết trực tiếp từ Dashboard: mỗi cột ngày trên lưới thực đơn có nút
/// "Xem chi tiết chợ" trỏ sang <c>/cho/chi-tiet?ngay=...</c> — chọn thực đơn/ngày nào
/// ở Dashboard thì trang này hiện đúng dữ liệu chợ của ngày đó.
/// </summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.ThuKho},{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("cho")]
public sealed class ChoController : Controller
{
    private readonly ThucDonDbContext _db;
    private readonly Services.ChiTietNgayTinhToanService _tinhToanSvc;

    public ChoController(ThucDonDbContext db, Services.ChiTietNgayTinhToanService tinhToanSvc)
    {
        _db = db;
        _tinhToanSvc = tinhToanSvc;
    }

    [HttpGet("chi-tiet")]
    public async Task<IActionResult> ChiTiet(DateOnly? ngay, int? coSoId, int? anSang, int? nhaTre, int? mauGiao)
    {
        var vm = await TaoViewModelAsync(ngay ?? DateOnly.FromDateTime(DateTime.Today), coSoId, anSang, nhaTre, mauGiao);
        return View(vm);
    }

    /// <summary>
    /// AJAX: trả về MẢNH HTML (không có layout, không có form/tab của trang đầy đủ) để
    /// nhúng trực tiếp vào Dashboard — nơi người dùng chọn/xem thực đơn theo ngày, bấm
    /// "Xem chợ" là bảng kê xổ ra ngay tại chỗ, không cần rời trang.
    /// </summary>
    [HttpGet("chi-tiet-panel")]
    public async Task<IActionResult> ChiTietPanel(DateOnly? ngay, int? coSoId, int? anSang, int? nhaTre, int? mauGiao)
    {
        var vm = await TaoViewModelAsync(ngay ?? DateOnly.FromDateTime(DateTime.Today), coSoId, anSang, nhaTre, mauGiao);
        return PartialView("_ChiTietChoPanel", vm);
    }

    /// <param name="anSangMoi">Số suất Ăn sáng người dùng muốn xem thử — null nghĩa là
    /// dùng đúng số đã lưu trong <c>dinh_muc_an_ngay</c> (không đổi gì).</param>
    /// <param name="nhaTreMoi">Tương tự, cho nhóm Nhà trẻ.</param>
    /// <param name="mauGiaoMoi">Tương tự, cho nhóm Mẫu giáo.</param>
    private async Task<ChiTietChoViewModel> TaoViewModelAsync(
        DateOnly ngay, int? coSoId, int? anSangMoi, int? nhaTreMoi, int? mauGiaoMoi)
    {
        var vm = new ChiTietChoViewModel
        {
            Ngay = ngay,
            // Sắp theo Id (thứ tự khởi tạo TC/PH/ĐT trong 01_kho_thuc_pham_mam_non.sql),
            // KHÔNG sắp theo tên — vì sắp theo tên sẽ ra PH trước TC (thứ tự chữ cái),
            // dễ gây cảm giác "chưa có dữ liệu" khi người dùng chọn từ Dashboard (không
            // truyền coSoId) mà lại rơi vào đúng cơ sở PH trong khi dữ liệu nằm ở TC.
            DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(),
        };

        var coSo = coSoId is not null
            ? vm.DsCoSo.FirstOrDefault(c => c.Id == coSoId)
            : vm.DsCoSo.FirstOrDefault();

        if (coSo is null)
        {
            // Chưa có cơ sở nào active trong hệ thống — không có gì để hiển thị.
            return vm;
        }

        vm.CoSoId = coSo.Id;
        vm.TenCoSo = coSo.TenCoSo;

        // ---- 1. Thực đơn trong ngày (2 khối) ----
        var ngayHoc = await _db.NgayHocs.FirstOrDefaultAsync(n => n.CoSoId == coSo.Id && n.Ngay == vm.Ngay);
        vm.CoNgayHoc = ngayHoc is not null;

        if (ngayHoc is not null)
        {
            var dsKhoi = await _db.KhoiHocs.ToListAsync();
            var khoiMauGiao = dsKhoi.FirstOrDefault(k => k.TenKhoi.Contains("Mẫu giáo"));
            var khoiNhaTre = dsKhoi.FirstOrDefault(k => k.TenKhoi.Contains("Nhà trẻ"));

            vm.ThucDonMauGiao = await LayThucDonAsync(coSo.Id, khoiMauGiao?.Id, ngayHoc.Id);
            vm.ThucDonNhaTre = await LayThucDonAsync(coSo.Id, khoiNhaTre?.Id, ngayHoc.Id);
        }

        // ---- 2. Định mức ăn ngày — lấy số suất GỐC đã lưu, rồi tính tỉ lệ với số suất
        // người dùng muốn xem thử (nếu có nhập) ----
        var dinhMucs = await _db.DinhMucAnNgays
            .Include(d => d.NhomChiPhi)
            .Where(d => d.CoSoId == coSo.Id && d.Ngay == vm.Ngay)
            .ToListAsync();

        int SuatGoc(string tenNhom) => dinhMucs.FirstOrDefault(d => d.NhomChiPhi.TenNhom == tenNhom)?.SoSuatAn ?? 0;
        decimal RateCuaNhom(string tenNhom) => dinhMucs.FirstOrDefault(d => d.NhomChiPhi.TenNhom == tenNhom)?.TienAn1Chau ?? 0m;

        vm.AnSangGoc = SuatGoc("Ăn sáng");
        vm.NhaTreGoc = SuatGoc("Nhà trẻ");
        vm.MauGiaoGoc = SuatGoc("Mẫu giáo");
        vm.AnSangHienTai = anSangMoi ?? vm.AnSangGoc;
        vm.NhaTreHienTai = nhaTreMoi ?? vm.NhaTreGoc;
        vm.MauGiaoHienTai = mauGiaoMoi ?? vm.MauGiaoGoc;

        // Tỉ lệ co giãn = Hiện tại / Gốc.
        // Khi CHƯA có sĩ số gốc đã lưu (goc = 0) thì không có cơ sở nào để quy đổi tỉ lệ —
        // trước đây forcing tỉ lệ = 0 khiến số liệu THẬT trong phiếu bị nhân về 0 hết
        // (nhìn như đổi ô "suất ăn xem thử" không có tác dụng gì, còn tệ hơn là che mất số
        // liệu thật đã ghi sổ). Đổi sang giữ nguyên (tỉ lệ = 1, không co giãn) cho tới khi
        // có sĩ số gốc thật để quy đổi — an toàn hơn vì không bao giờ tự ý xoá số liệu thật.
        var tyLeAnSang = vm.AnSangGoc > 0 ? (decimal)vm.AnSangHienTai / vm.AnSangGoc : 1m;
        var tyLeNhaTre = vm.NhaTreGoc > 0 ? (decimal)vm.NhaTreHienTai / vm.NhaTreGoc : 1m;
        var tyLeMauGiao = vm.MauGiaoGoc > 0 ? (decimal)vm.MauGiaoHienTai / vm.MauGiaoGoc : 1m;

        // ---- 3. Phiếu xuất kho trong ngày (số liệu THẬT đã lập) ----
        var phieuXuats = await _db.PhieuXuatKhos
            .Where(p => p.CoSoId == coSo.Id && p.NgayXuat == vm.Ngay)
            .Include(p => p.ChiTiets).ThenInclude(c => c.HangHoa).ThenInclude(h => h.Dvt)
            .Include(p => p.ChiTiets).ThenInclude(c => c.NhomChiPhi)
            .ToListAsync();

        var phieuGiaVi = phieuXuats.FirstOrDefault(p => p.LoaiXuat == LoaiXuatKho.GiaViHangNgay);
        var phieuSang = phieuXuats.FirstOrDefault(p => p.LoaiXuat == LoaiXuatKho.ThucPhamSang);
        var phieuChinhXe = phieuXuats.FirstOrDefault(p => p.LoaiXuat == LoaiXuatKho.ThucPhamChinhXe);

        // Mục 2: ưu tiên phiếu thật nếu ngày này đã lập; CHƯA lập thì tự tính từ định mức
        // cố định (dinh_muc_gia_vi_chuan) × sĩ số ngày đó — không cần lập phiếu mỗi ngày
        // nữa (đáp ứng yêu cầu "gia vị hằng ngày cho cố định tất cả các ngày"). Dùng
        // chung đúng 1 chỗ tính với Excel (ChiTietNgayTinhToanService), tránh lệch nhau.
        // Truyền thẳng sĩ số HIỆN TẠI (đang xem thử) vào nhánh "tự động theo định mức" —
        // để đổi ô "suất ăn xem thử" có tác dụng NGAY, không phải nhân tỉ lệ hiện
        // tại/gốc (cách đó chia cho 0 khi chưa lưu sĩ số gốc, đổi ô xem thử vô tác dụng).
        var giaViThat = await _tinhToanSvc.LayGiaViThatAsync(
            coSo.Id, vm.Ngay, vm.AnSangHienTai, vm.NhaTreHienTai, vm.MauGiaoHienTai);
        vm.GiaViTuDinhMuc = !giaViThat.CoPhieu;
        vm.GiaVi = giaViThat.DanhSach.Select(x => new ChiTietChoGiaViVm
        {
            TenHang = x.TenHang,
            Dvt = x.Dvt,
            DonGia = x.DonGia,
            SlAnSang = x.SlAnSang,
            SlNhaTre = x.SlNhaTre,
            SlMauGiao = x.SlMauGiao,
            TtAnSang = x.TtAnSang,
            TtNhaTre = x.TtNhaTre,
            TtMauGiao = x.TtMauGiao,
        }).ToList();

        vm.ThucPhamSang = XayThucPhamSang(phieuSang);
        vm.ThucPhamChinhXe = XayChinhXe(phieuChinhXe);

        // Co giãn số lượng/thành tiền theo đúng tỉ lệ suất mới — khi không đổi gì thì
        // 3 tỉ lệ đều = 1, phép nhân này không làm lệch số liệu thật đi chút nào. Mục 2 KHÔNG
        // co giãn nữa khi đang ở nhánh tự động (giaViThat.CoPhieu == false) vì ở trên đã tính
        // thẳng theo đúng sĩ số hiện tại rồi — co giãn thêm lần nữa sẽ nhân đúp sai số.
        ApDungTyLe(vm, tyLeAnSang, tyLeNhaTre, tyLeMauGiao, coGianGiaVi: giaViThat.CoPhieu);

        // ---- 4. Đối chiếu ngân sách — Thực chi lấy TỪ CHÍNH danh sách ở trên (đã co
        // giãn nếu có), không đọc lại phiếu gốc, để 2 nơi không bao giờ lệch nhau ----
        vm.DinhMuc = new List<ChiTietChoDinhMucVm>
        {
            new() { TenNhom = "Ăn sáng", TienAn1Chau = RateCuaNhom("Ăn sáng"), SoSuatAn = vm.AnSangHienTai,
                     DuocPhepChi = RateCuaNhom("Ăn sáng") * vm.AnSangHienTai, ThucChi = TongChiTuVm("Ăn sáng", vm) },
            new() { TenNhom = "Nhà trẻ", TienAn1Chau = RateCuaNhom("Nhà trẻ"), SoSuatAn = vm.NhaTreHienTai,
                     DuocPhepChi = RateCuaNhom("Nhà trẻ") * vm.NhaTreHienTai, ThucChi = TongChiTuVm("Nhà trẻ", vm) },
            new() { TenNhom = "Mẫu giáo", TienAn1Chau = RateCuaNhom("Mẫu giáo"), SoSuatAn = vm.MauGiaoHienTai,
                     DuocPhepChi = RateCuaNhom("Mẫu giáo") * vm.MauGiaoHienTai, ThucChi = TongChiTuVm("Mẫu giáo", vm) },
        };
        // Nhóm nào chưa từng có định mức trong CSDL (TienAn1Chau = 0 và SoSuatAn gốc = 0)
        // thì bỏ khỏi bảng, tránh hiện dòng "0 đồng / 0 suất" vô nghĩa.
        vm.DinhMuc = vm.DinhMuc.Where(d => dinhMucs.Any(x => x.NhomChiPhi.TenNhom == d.TenNhom)).ToList();

        // ---- 5. Chữ ký — lấy từ phiếu nào có giá trị trước (thường cả 3 phiếu do cùng
        // 1 người lập trong ngày, không cần ghép nối phức tạp) ----
        vm.NguoiLap = phieuGiaVi?.NguoiLap ?? phieuSang?.NguoiLap ?? phieuChinhXe?.NguoiLap;
        vm.ThuKho = phieuGiaVi?.ThuKho ?? phieuSang?.ThuKho ?? phieuChinhXe?.ThuKho;
        vm.KeToan = phieuGiaVi?.KeToan ?? phieuSang?.KeToan ?? phieuChinhXe?.KeToan;
        vm.HieuTruong = phieuGiaVi?.HieuTruong ?? phieuSang?.HieuTruong ?? phieuChinhXe?.HieuTruong;

        return vm;
    }

    private async Task<List<ChiTietChoBuaMonVm>> LayThucDonAsync(int coSoId, int? khoiId, int ngayHocId)
    {
        if (khoiId is null) return new List<ChiTietChoBuaMonVm>();

        return await _db.ThucDons
            .Where(t => t.CoSoId == coSoId && t.KhoiId == khoiId && t.NgayHocId == ngayHocId)
            .Include(t => t.LoaiBuaAn)
            .Include(t => t.MonAn)
            .OrderBy(t => t.LoaiBuaAn.ThuTu)
            .Select(t => new ChiTietChoBuaMonVm { TenBua = t.LoaiBuaAn.TenBua, TenMon = t.MonAn != null ? t.MonAn.TenMon : null })
            .ToListAsync();
    }

    /// <summary>Mục 3 — 1 dòng/hàng hóa, không tách nhóm chi phí.</summary>
    private static List<ChiTietChoThucPhamSangVm> XayThucPhamSang(PhieuXuatKho? phieu)
    {
        if (phieu is null) return new List<ChiTietChoThucPhamSangVm>();

        return phieu.ChiTiets
            .Select(c => new ChiTietChoThucPhamSangVm
            {
                TenHang = c.HangHoa.TenHang,
                Dvt = c.HangHoa.Dvt?.TenDvt,
                SoLuong = c.SoLuong,
                DonGia = c.DonGia,
                ThanhTien = c.ThanhTien,
            })
            .OrderBy(x => x.TenHang)
            .ToList();
    }

    /// <summary>Mục 4 — pivot 1 dòng/hàng hóa theo 2 nhóm chi phí (Nhà trẻ/Mẫu giáo).</summary>
    private static List<ChiTietChoChinhXeVm> XayChinhXe(PhieuXuatKho? phieu)
    {
        if (phieu is null) return new List<ChiTietChoChinhXeVm>();

        return phieu.ChiTiets
            .GroupBy(c => c.HangHoaId)
            .Select(g =>
            {
                var dauTien = g.First();
                return new ChiTietChoChinhXeVm
                {
                    TenHang = dauTien.HangHoa.TenHang,
                    Dvt = dauTien.HangHoa.Dvt?.TenDvt,
                    MaHang = dauTien.HangHoa.MaHang,
                    DonGia = dauTien.DonGia,
                    SlNhaTre = TongTheoNhom(g, "Nhà trẻ", c => c.SoLuong),
                    SlMauGiao = TongTheoNhom(g, "Mẫu giáo", c => c.SoLuong),
                    TtNhaTre = TongTheoNhom(g, "Nhà trẻ", c => c.ThanhTien),
                    TtMauGiao = TongTheoNhom(g, "Mẫu giáo", c => c.ThanhTien),
                };
            })
            .OrderBy(x => x.TenHang)
            .ToList();
    }

    private static decimal TongTheoNhom(IEnumerable<ChiTietXuatKho> dong, string tenNhom, Func<ChiTietXuatKho, decimal> chonSo)
        => dong.Where(c => c.NhomChiPhi?.TenNhom == tenNhom).Sum(chonSo);

    /// <summary>
    /// Nhân số lượng/thành tiền của cả 3 mục (GiaVi/ThucPhamSang/ThucPhamChinhXe) theo
    /// đúng tỉ lệ suất mới/suất gốc của từng nhóm — đây là bước "tính lại" khi người
    /// dùng đổi số suất ăn ở ô nhập trên trang. Khi tỉ lệ = 1 (không đổi gì), phép nhân
    /// này giữ nguyên số liệu thật 100%.
    /// </summary>
    private static void ApDungTyLe(ChiTietChoViewModel vm, decimal tyLeAnSang, decimal tyLeNhaTre, decimal tyLeMauGiao, bool coGianGiaVi)
    {
        if (coGianGiaVi)
        {
            foreach (var r in vm.GiaVi)
            {
                r.SlAnSang *= tyLeAnSang;
                r.SlNhaTre *= tyLeNhaTre;
                r.SlMauGiao *= tyLeMauGiao;
                r.TtAnSang = r.SlAnSang * r.DonGia;
                r.TtNhaTre = r.SlNhaTre * r.DonGia;
                r.TtMauGiao = r.SlMauGiao * r.DonGia;
            }
        }

        foreach (var r in vm.ThucPhamSang)
        {
            r.SoLuong *= tyLeAnSang;
            r.ThanhTien = r.SoLuong * r.DonGia;
        }

        foreach (var r in vm.ThucPhamChinhXe)
        {
            r.SlNhaTre *= tyLeNhaTre;
            r.SlMauGiao *= tyLeMauGiao;
            r.TtNhaTre = r.SlNhaTre * r.DonGia;
            r.TtMauGiao = r.SlMauGiao * r.DonGia;
        }
    }

    /// <summary>
    /// Tổng chi thực tế của 1 nhóm chi phí, cộng lại TỪ CHÍNH các danh sách đã hiển thị
    /// trên trang (vm.GiaVi/ThucPhamSang/ThucPhamChinhXe — đã co giãn nếu có đổi suất),
    /// thay vì đọc lại phiếu xuất kho gốc, để con số "Thực chi" không bao giờ lệch với
    /// tổng cộng hiện đang hiển thị ở mục 2/3/4 bên dưới.
    /// </summary>
    private static decimal TongChiTuVm(string tenNhom, ChiTietChoViewModel vm)
    {
        var tuGiaVi = tenNhom switch
        {
            "Ăn sáng" => vm.GiaVi.Sum(x => x.TtAnSang),
            "Nhà trẻ" => vm.GiaVi.Sum(x => x.TtNhaTre),
            "Mẫu giáo" => vm.GiaVi.Sum(x => x.TtMauGiao),
            _ => 0m,
        };
        var tuChinhXe = tenNhom switch
        {
            "Nhà trẻ" => vm.ThucPhamChinhXe.Sum(x => x.TtNhaTre),
            "Mẫu giáo" => vm.ThucPhamChinhXe.Sum(x => x.TtMauGiao),
            _ => 0m,
        };
        var tuSang = tenNhom == "Ăn sáng" ? vm.ThucPhamSang.Sum(x => x.ThanhTien) : 0m;
        return tuGiaVi + tuChinhXe + tuSang;
    }
}
