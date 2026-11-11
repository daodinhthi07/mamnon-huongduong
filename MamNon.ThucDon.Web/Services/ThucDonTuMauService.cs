using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Services;

public sealed class KetQuaNhapThucDon
{
    public int NgayTao { get; set; }
    public int DongThem { get; set; }
    public int DongCapNhat { get; set; }
    public int DongGiu { get; set; }
    public int DinhMucThem { get; set; }
    public int DaXoa { get; set; }
    public int PhieuXoa { get; set; }
    public List<string> MonMoi { get; } = new();
    public string? Loi { get; set; }

    public string TomTat(DateOnly thuHai)
    {
        if (Loi != null) return Loi;
        var s = "Thực đơn tuần " + thuHai.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ": thêm " + DongThem + " ô, cập nhật " + DongCapNhat
            + " ô, giữ nguyên " + DongGiu + " ô";
        if (DaXoa > 0) s += "; đã xóa " + DaXoa + " ô thực đơn cũ";
        if (PhieuXoa > 0) s += "; đã bỏ " + PhieuXoa + " phiếu xuất kho cố định của tuần cũ (số liệu sẽ tính lại theo thực đơn mới)";
        if (NgayTao > 0) s += "; tạo " + NgayTao + " ngày học mới";
        if (DinhMucThem > 0) s += "; thêm " + DinhMucThem + " dòng số suất ăn";
        if (MonMoi.Count > 0) s += "; món mới chưa có định lượng: " + string.Join(", ", MonMoi);
        return s + ".";
    }
}

/// <summary>
/// Nạp thực đơn của 1 tuần từ file Excel mẫu vào hệ thống (cho cả các cơ sở): tạo ngày học còn thiếu, đặt món từng bữa,
/// ghi nhận ngày nghỉ, số suất ăn và tiền ăn 1 cháu. Món chưa có trong danh mục được tạo mới (chưa có định lượng).
/// </summary>
public sealed class ThucDonTuMauService
{
    private static readonly Dictionary<string, string> BietDanh = new()
    {
        ["lơ xanh"] = "lơ xanh hấp chấm xì dầu",
        ["khoai lang"] = "khoai lang hấp",
    };

    private readonly ThucDonDbContext _db;
    private readonly MauTuanExcelService _mau;

    public ThucDonTuMauService(ThucDonDbContext db, MauTuanExcelService mau)
    {
        _db = db;
        _mau = mau;
    }

    /// <summary>Khoá "bộ từ": bỏ hậu tố (2)/(3), bỏ chữ "nấu", không phân biệt thứ tự từ. Để "Canh mướp, rau mồng tơi nấu tôm" và
    /// "Canh rau mồng tơi, mướp nấu tôm" (file mẫu viết lúc này lúc kia) cùng khớp 1 món, không bị tạo thêm món rỗng không có định lượng.</summary>
    private static string BoTu(string? s)
    {
        var k = Regex.Replace(Khoa(s), @"\(\s*\d+\s*\)", " ");
        return string.Join(" ", Regex.Matches(k, @"[\p{L}\p{N}]+").Select(m => m.Value).Where(t => t != "nấu").Distinct().OrderBy(t => t, StringComparer.Ordinal));
    }

    private static string Khoa(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return Regex.Replace(s.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim().ToLowerInvariant();
    }

    /// <param name="thuHaiDich">Thứ 2 của tuần cần ghi vào hệ thống (có thể khác tuần của file mẫu: các ngày tự dịch theo).</param>
    /// <param name="ghiDe">true = ghi đè món của các ngày đó theo file mẫu (ô trống trong file => bỏ món); false = chỉ điền chỗ còn trống.</param>
    /// <param name="suatMoi">Số suất do người dùng nhập (khóa "TC|1"); null = dùng số suất trong file mẫu.</param>
    /// <param name="tienMoi">Tiền ăn 1 cháu do người dùng nhập (khóa "TC|1", {Ăn sáng, Nhà trẻ, Mẫu giáo}); null/phần tử null = dùng tiền trong file mẫu.</param>
    /// <param name="thayThe">true = XÓA HẾT dữ liệu cũ của tuần đó (thực đơn, số suất ăn, phiếu xuất kho cố định, ngày học không còn trong file)
    /// rồi nạp lại hoàn toàn theo file mẫu.</param>
    public async Task<KetQuaNhapThucDon> NhapAsync(MauTuanInfo mau, DateOnly thuHaiDich, bool ghiDe, TrangThaiThucDon trangThai,
        string? nguoi, IDictionary<string, double?[]>? suatMoi = null, bool thayThe = false, IDictionary<string, double?[]>? tienMoi = null)
    {
        var kq = new KetQuaNhapThucDon();
        var td = _mau.DocThucDon(mau);
        if (td.Cells.Count == 0 && td.NgayNghi.Count == 0)
        {
            kq.Loi = "Không đọc được thực đơn trong file mẫu (sheet \"Thực đơn tuần ok\").";
            return kq;
        }
        var chiTiet = _mau.DocChiTiet(mau);

        var loai = await _db.LoaiBuaAns.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var coSos = await _db.CoSos.Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync();
        var mons = await _db.MonAns.ToListAsync();
        var theoNhom = new Dictionary<(int, string), MonAn>();
        var tatCa = new HashSet<string>();
        foreach (var m in mons)
        {
            var k = Khoa(m.TenMon);
            tatCa.Add(k);
            if (m.NhomBuaAnId != null && m.IsActive) theoNhom[(m.NhomBuaAnId.Value, k)] = m; // món đã ẩn không dùng khi nhập
        }
        var theoBoTu = new Dictionary<(int, string), MonAn>();   // (nhóm bữa, bộ từ) -> món đang dùng (ưu tiên món có id nhỏ)
        foreach (var m in mons.OrderBy(x => x.Id))
            if (m.NhomBuaAnId != null && m.IsActive) theoBoTu.TryAdd((m.NhomBuaAnId.Value, BoTu(m.TenMon)), m);

        LoaiBuaAn? TimBua(int khoi, string nhan)
        {
            var u = nhan.Trim().ToUpperInvariant();
            if (khoi == 2 && u == "XẾ") return null;               // ô XẾ của Nhà trẻ chỉ là sữa (tính tự động)
            if (khoi == 2 && u == "PHỤ SÁNG") u = "CANH";
            if (khoi == 2 && u == "TRƯA") u = "MẶN";
            return loai.FirstOrDefault(x => x.KhoiId == khoi && x.TenBua.ToUpperInvariant() == u);
        }

        MonAn? TimMon(int? nhom, string ten)
        {
            var k = Khoa(ten);
            if (BietDanh.TryGetValue(k, out var bd)) k = bd;
            if (nhom == null) return null;
            if (nhom == 8 && theoNhom.TryGetValue((8, k + " (xế)"), out var a)) return a;
            if (nhom == 1 && theoNhom.TryGetValue((1, k + " (sáng)"), out var s)) return s;
            if (theoNhom.TryGetValue((nhom.Value, k), out var b)) return b;
            // Món cùng tên đã bị ẩn / đã từng tạo bản " (2)", " (3)"… => dùng lại bản đang dùng thay vì tạo thêm.
            for (var i = 2; i <= 9; i++)
                if (theoNhom.TryGetValue((nhom.Value, k + " (" + i + ")"), out var b2)) return b2;
            // Cùng bộ từ nhưng khác thứ tự / khác "nấu" => cùng 1 món
            if (theoBoTu.TryGetValue((nhom.Value, BoTu(ten)), out var b3)) return b3;
            return null;
        }

        // ---- 1) xác định món cho từng ô (tạo món mới nếu chưa có) ----
        var gan = new List<(int Khoi, int LoaiId, int Ngay, MonAn Mon)>();
        foreach (var c in td.Cells)
        {
            var lb = TimBua(c.Khoi, c.Nhan);
            if (lb == null) continue;
            var mon = TimMon(lb.NhomBuaAnId, c.Text);
            if (mon == null)
            {
                var ten = c.Text.Trim();
                if (tatCa.Contains(Khoa(ten)))
                {
                    var goc = ten;
                    ten = goc + (lb.NhomBuaAnId == 8 ? " (xế)" : (lb.NhomBuaAnId == 1 ? " (sáng)" : " (2)"));
                    for (var i = 2; tatCa.Contains(Khoa(ten)); i++) ten = goc + " (" + i + ")";   // tên phải duy nhất (uq_monan_ten)
                }
                mon = new MonAn
                {
                    TenMon = ten,
                    NhomBuaAnId = lb.NhomBuaAnId,
                    IsActive = true,
                    GhiChu = "Thêm từ file mẫu tuần " + mau.ThuHai.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " (chưa có định lượng)",
                };
                _db.MonAns.Add(mon);
                tatCa.Add(Khoa(ten));
                theoNhom[(lb.NhomBuaAnId ?? 0, Khoa(ten))] = mon;
                kq.MonMoi.Add(ten);
            }
            gan.Add((c.Khoi, lb.Id, c.Ngay, mon));
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();

        // ---- 2) các cơ sở: ngày học, thực đơn, số suất ----
        var ngayCoMon = td.Cells.Select(c => c.Ngay).Concat(td.NgayNghi.Keys).Distinct().OrderBy(x => x).ToList();
        var tu = thuHaiDich;
        var den = thuHaiDich.AddDays(5);
        var homNay = DateOnly.FromDateTime(DateTime.Today);

        foreach (var cs in coSos)
        {
            if (thayThe)
            {
                var cu = await _db.NgayHocs.Where(n => n.CoSoId == cs.Id && n.Ngay >= tu && n.Ngay <= den).ToListAsync();
                var cuIds = cu.Select(n => n.Id).ToList();
                var tdCu = await _db.ThucDons.Where(t => t.CoSoId == cs.Id && cuIds.Contains(t.NgayHocId)).ToListAsync();
                _db.ThucDons.RemoveRange(tdCu);
                var dmCu = await _db.DinhMucAnNgays.Where(x => x.CoSoId == cs.Id && x.Ngay >= tu && x.Ngay <= den).ToListAsync();
                _db.DinhMucAnNgays.RemoveRange(dmCu);
                var phieuCu = await _db.PhieuXuatKhos.Include(p => p.ChiTiets).Where(p => p.CoSoId == cs.Id && p.NgayXuat >= tu && p.NgayXuat <= den).ToListAsync();
                foreach (var p in phieuCu) _db.ChiTietXuatKhos.RemoveRange(p.ChiTiets);
                _db.PhieuXuatKhos.RemoveRange(phieuCu);
                var giu = ngayCoMon.Select(x => tu.AddDays(x)).ToHashSet();
                _db.NgayHocs.RemoveRange(cu.Where(n => !giu.Contains(n.Ngay)));
                kq.DaXoa += tdCu.Count;
                kq.PhieuXoa += phieuCu.Count;
                await _db.SaveChangesAsync();
            }

            var ngayHocs = await _db.NgayHocs.Where(n => n.CoSoId == cs.Id && n.Ngay >= tu && n.Ngay <= den).ToListAsync();
            foreach (var k in ngayCoMon)
            {
                var ngay = tu.AddDays(k);
                var nh = ngayHocs.FirstOrDefault(n => n.Ngay == ngay);
                var nghi = td.NgayNghi.TryGetValue(k, out var inf);
                var moiTao = false;
                if (nh == null)
                {
                    nh = new NgayHoc { CoSoId = cs.Id, Ngay = ngay };
                    _db.NgayHocs.Add(nh);
                    ngayHocs.Add(nh);
                    kq.NgayTao++;
                    moiTao = true;
                }
                if (moiTao || ghiDe)
                {
                    nh.TrangThai = nghi ? (inf.NghiLe ? TrangThaiNgay.NghiLe : TrangThaiNgay.NghiKhac) : TrangThaiNgay.HocBinhThuong;
                    nh.GhiChu = nghi ? inf.GhiChu : null;
                }
            }
            await _db.SaveChangesAsync();

            var ids = ngayHocs.Select(n => n.Id).ToList();
            var dong = await _db.ThucDons.Where(t => t.CoSoId == cs.Id && ids.Contains(t.NgayHocId)).ToListAsync();
            foreach (var k in ngayCoMon)
            {
                var nh = ngayHocs.First(n => n.Ngay == tu.AddDays(k));
                var nghi = td.NgayNghi.ContainsKey(k);
                foreach (var lb in loai)
                {
                    MonAn? mon = null;
                    if (!nghi)
                    {
                        var g = gan.FirstOrDefault(x => x.Khoi == lb.KhoiId && x.LoaiId == lb.Id && x.Ngay == k);
                        mon = g.Mon;
                    }
                    var co = dong.FirstOrDefault(t => t.NgayHocId == nh.Id && t.KhoiId == lb.KhoiId && t.LoaiBuaAnId == lb.Id);
                    if (co == null)
                    {
                        var moi = new ThucDonEntity
                        {
                            CoSoId = cs.Id,
                            KhoiId = lb.KhoiId,
                            NgayHocId = nh.Id,
                            LoaiBuaAnId = lb.Id,
                            MonAnId = mon?.Id,
                            TrangThai = trangThai,
                            NguoiLap = td.NguoiLap ?? nguoi,
                            NguoiDuyet = trangThai == TrangThaiThucDon.DaDuyet ? td.NguoiDuyet : null,
                            NgayLap = homNay,
                            NgayDuyet = trangThai == TrangThaiThucDon.DaDuyet ? homNay : null,
                        };
                        _db.ThucDons.Add(moi);
                        dong.Add(moi);
                        kq.DongThem++;
                    }
                    else if (ghiDe)
                    {
                        if (co.MonAnId != mon?.Id)
                        {
                            co.MonAnId = mon?.Id;
                            co.TrangThai = trangThai;
                            co.UpdatedAt = DateTime.UtcNow;
                            kq.DongCapNhat++;
                        }
                        else kq.DongGiu++;
                    }
                    else if (co.MonAnId == null && mon != null)
                    {
                        co.MonAnId = mon.Id;
                        co.UpdatedAt = DateTime.UtcNow;
                        kq.DongCapNhat++;
                    }
                    else kq.DongGiu++;
                }
            }
            await _db.SaveChangesAsync();

            // số suất ăn + tiền ăn 1 cháu từng ngày (cơ sở nhận diện theo mã TC / PH / ĐT trong file mẫu)
            var khoa = cs.MaCoSo.Trim();
            if (chiTiet.Tien.TryGetValue(khoa, out var tienTheoNgay) && chiTiet.Suat.TryGetValue(khoa, out var suatTheoNgay))
            {
                var dm = await _db.DinhMucAnNgays.Where(x => x.CoSoId == cs.Id && x.Ngay >= tu && x.Ngay <= den).ToListAsync();
                foreach (var k in ngayCoMon)
                {
                    if (td.NgayNghi.ContainsKey(k)) continue;
                    if (!tienTheoNgay.TryGetValue(k, out var tien) || !suatTheoNgay.TryGetValue(k, out var suatMau)) continue;
                    var suat = suatMau;
                    if (tienMoi != null && tienMoi.TryGetValue(khoa + "|" + k, out var tm))
                        tien = new[] { tm[0] ?? tien[0], tm[1] ?? tien[1], tm[2] ?? tien[2] };
                    if (suatMoi != null && suatMoi.TryGetValue(khoa + "|" + k, out var sm))
                        suat = new[] { sm[0] ?? suatMau[0], sm[1] ?? suatMau[1], sm[2] ?? suatMau[2] };
                    var ngay = tu.AddDays(k);
                    for (var i = 0; i < 3; i++)
                    {
                        var nhomId = i + 1;
                        var cu = dm.FirstOrDefault(x => x.Ngay == ngay && x.NhomChiPhiId == nhomId);
                        if (tien[i] == null && suat[i] == null) continue;
                        var so = (int)Math.Round(suat[i] ?? 0);
                        var giaMacDinh = i == 0 ? 10000m : 29000m;
                        var gia = tien[i] ?? (double)(cu?.TienAn1Chau ?? giaMacDinh);
                        if (cu == null)
                        {
                            _db.DinhMucAnNgays.Add(new DinhMucAnNgay { CoSoId = cs.Id, NhomChiPhiId = nhomId, Ngay = ngay, TienAn1Chau = (decimal)gia, SoSuatAn = so });
                            kq.DinhMucThem++;
                        }
                        else if (ghiDe && (suatMoi != null || tienMoi != null))
                        {
                            if (suatMoi != null && suat[i] != null) cu.SoSuatAn = so;
                            if (tien[i] != null) cu.TienAn1Chau = (decimal)gia;
                        }
                    }
                }
                await _db.SaveChangesAsync();
            }
        }
        return kq;
    }
}
