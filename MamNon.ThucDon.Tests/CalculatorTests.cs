using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Tests;

[TestFixture]
public class NganSachNgayCalculatorTests
{
    private readonly NganSachNgayCalculator _sut = new();

    // Số liệu lấy đúng từ file Excel "TUẦN 2 THÁNG 9 B", Thứ hai 07/9/2026,
    // nhóm "Ăn sáng": 10.000đ/suất x 280 suất = 2.800.000đ được phép chi.
    [Test]
    public void TinhNganSach_WhenDinhMucAnSang10000VaSoSuat280_ExpectedSoTienDuocPhepChi2800000()
    {
        var nhomAnSang = new NhomChiPhi { Id = 1, TenNhom = "Ăn sáng" };
        var dinhMuc = new DinhMucAnNgay
        {
            NhomChiPhiId = nhomAnSang.Id,
            NhomChiPhi = nhomAnSang,
            TienAn1Chau = 10000m,
            SoSuatAn = 280
        };

        var ketQua = _sut.TinhNganSach(dinhMuc, tongChiThucTe: 1_623_200m);

        Assert.That(ketQua.SoTienDuocPhepChi, Is.EqualTo(2_800_000m));
        Assert.That(ketQua.ChenhLech, Is.EqualTo(1_176_800m));
    }

    [Test]
    public void TinhNganSachNhieuNhom_WhenMotNhomChuaCoSoLieuChiThucTe_ExpectedTongChiThucTeBang0()
    {
        var nhaTre = new NhomChiPhi { Id = 2, TenNhom = "Nhà trẻ" };
        var mauGiao = new NhomChiPhi { Id = 3, TenNhom = "Mẫu giáo" };

        var dinhMucCacNhom = new[]
        {
            new DinhMucAnNgay { NhomChiPhiId = nhaTre.Id, NhomChiPhi = nhaTre, TienAn1Chau = 28000m, SoSuatAn = 70 },
            new DinhMucAnNgay { NhomChiPhiId = mauGiao.Id, NhomChiPhi = mauGiao, TienAn1Chau = 28000m, SoSuatAn = 330 }
        };
        var tongChiThucTe = new Dictionary<int, decimal> { [nhaTre.Id] = 1_483_955m };

        var ketQua = _sut.TinhNganSachNhieuNhom(dinhMucCacNhom, tongChiThucTe);

        Assert.That(ketQua.Single(k => k.Nhom.Id == mauGiao.Id).TongChiThucTe, Is.EqualTo(0m));
    }
}

[TestFixture]
public class NguyenLieuCalculatorTests
{
    private readonly ThucDonDbContext _db = new(
        new DbContextOptionsBuilder<ThucDonDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
    private readonly NguyenLieuCalculator _sut;

    public NguyenLieuCalculatorTests()
    {
        _sut = new NguyenLieuCalculator(_db);
    }

    [OneTimeTearDown]
    public void DisposeDbContext() => _db.Dispose();

    [Test]
    public void LaThucPhamTrongMuc34_TCOnlyIncludesHanhTim_ButPHAndDTIncludeAllOnions()
    {
        var hanhLa = new HangHoa { TenHang = "Hành lá", LoaiHang = LoaiHangHoa.GiaVi };
        var hanhNgo = new HangHoa { TenHang = "Hành ngò", LoaiHang = LoaiHangHoa.GiaVi };
        var hanhTim = new HangHoa { TenHang = "Hành tím", LoaiHang = LoaiHangHoa.GiaVi };

        Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhLa, "TC"), Is.False);
        Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhNgo, "TC"), Is.False);
        Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhTim, "TC"), Is.True);
        Assert.That(ChiTietNgayTinhToanService.LaHanhBiAnTaiTc("Hành lá", "TC"), Is.True);
        Assert.That(ChiTietNgayTinhToanService.LaHanhBiAnTaiTc("Hành ngò", "TC"), Is.True);
        Assert.That(ChiTietNgayTinhToanService.LaHanhBiAnTaiTc("Hành tím", "TC"), Is.False);

        foreach (var maCoSo in new[] { "PH", "ĐT" })
        {
            Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhLa, maCoSo), Is.True);
            Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhNgo, maCoSo), Is.True);
            Assert.That(ChiTietNgayTinhToanService.LaThucPhamTrongMuc34(hanhTim, maCoSo), Is.True);
            Assert.That(ChiTietNgayTinhToanService.LaHanhBiAnTaiTc("Hành lá", maCoSo), Is.False);
            Assert.That(ChiTietNgayTinhToanService.LaHanhBiAnTaiTc("Hành ngò", maCoSo), Is.False);
        }
    }

    [Test]
    public void TinhNhuCauNguyenLieu_WhenCungNguyenLieuDungOHaiMonHaiKhoi_ExpectedCongDonDung()
    {
        var hanhNgo = new HangHoa { Id = 1, TenHang = "Hành ngò", DonGiaGanNhat = 50000m };

        var monCanh = new MonAn { Id = 1, TenMon = "Canh rau tập tàng" };
        monCanh.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
        {
            MonAnId = monCanh.Id, MonAn = monCanh, HangHoaId = hanhNgo.Id, HangHoa = hanhNgo, DinhLuong = 0.003m
        });

        var monMan = new MonAn { Id = 2, TenMon = "Nấm rơm kho đậu khuôn" };
        monMan.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
        {
            MonAnId = monMan.Id, MonAn = monMan, HangHoaId = hanhNgo.Id, HangHoa = hanhNgo, DinhLuong = 0.001m
        });

        var buaCanh = new LoaiBuaAn { Id = 1, TenBua = "CANH" };
        var buaMan = new LoaiBuaAn { Id = 2, TenBua = "MẶN" };

        var thucDonNhaTre = new ThucDonEntity
        {
            Id = 1, LoaiBuaAnId = buaCanh.Id, LoaiBuaAn = buaCanh, MonAnId = monCanh.Id, MonAn = monCanh
        };
        var thucDonMauGiao = new ThucDonEntity
        {
            Id = 2, LoaiBuaAnId = buaMan.Id, LoaiBuaAn = buaMan, MonAnId = monMan.Id, MonAn = monMan
        };

        var ketQua = _sut.TinhNhuCauNguyenLieu(new[]
        {
            (thucDonNhaTre, SoSuatAn: 70),
            (thucDonMauGiao, SoSuatAn: 330)
        });

        var hanhNgoKetQua = ketQua.Single(k => k.HangHoa.Id == hanhNgo.Id);
        // 70 x 0.003 + 330 x 0.001 = 0.21 + 0.33 = 0.54 kg
        Assert.That(hanhNgoKetQua.TongCanDung, Is.EqualTo(0.54m));
        Assert.That(hanhNgoKetQua.ThanhTien, Is.EqualTo(0.54m * 50000m));
    }

    [Test]
    public void TinhNhuCauNguyenLieu_WhenThucDonKhongCoMonAn_ExpectedBoQuaNgayNghiLe()
    {
        var buaMan = new LoaiBuaAn { Id = 1, TenBua = "MẶN" };
        var thucDonNghiLe = new ThucDonEntity { Id = 1, LoaiBuaAnId = buaMan.Id, LoaiBuaAn = buaMan, MonAnId = null, MonAn = null };

        var ketQua = _sut.TinhNhuCauNguyenLieu(new[] { (thucDonNghiLe, SoSuatAn: 0) });

        Assert.That(ketQua, Is.Empty);
    }
}

[TestFixture]
public class HangMucNganSachCalculatorTests
{
    private readonly HangMucNganSachCalculator _sut = new();

    private static readonly HangMucNganSach Man = new() { Id = 1, TenHangMuc = "Mặn", ThuTu = 2 };
    private static readonly HangMucNganSach TrangMieng = new() { Id = 2, TenHangMuc = "Tráng miệng", ThuTu = 6 };

    // Định mức Mẫu giáo lấy đúng từ file Excel: Mặn 9.490đ/suất.
    private static Dictionary<int, decimal> DinhMucMauGiao => new()
    {
        [Man.Id] = 9490m,
    };

    [Test]
    public void TinhChiPhiTheoHangMuc_WhenMonManDatHonDinhMuc_ExpectedVuotDinhMucTrue()
    {
        var caThu = new HangHoa { Id = 1, TenHang = "Cá thu đen", DonGiaGanNhat = 220000m };
        var buaMan = new LoaiBuaAn { Id = 1, TenBua = "MẶN", HangMucId = Man.Id, HangMuc = Man };

        var monCaThu = new MonAn { Id = 1, TenMon = "Cá thu kho" };
        monCaThu.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
        {
            MonAnId = monCaThu.Id, MonAn = monCaThu, HangHoaId = caThu.Id, HangHoa = caThu, DinhLuong = 0.06m
        });

        var thucDon = new ThucDonEntity
        {
            Id = 1, LoaiBuaAnId = buaMan.Id, LoaiBuaAn = buaMan, MonAnId = monCaThu.Id, MonAn = monCaThu
        };

        var ketQua = _sut.TinhChiPhiTheoHangMuc(new[] { thucDon }, DinhMucMauGiao);

        var manKetQua = ketQua.Single(k => k.HangMuc.Id == Man.Id);
        // 0.06 x 220.000 = 13.200đ/suất
        Assert.That(manKetQua.ChiPhiThucTeTrenSuat, Is.EqualTo(13200m));
        Assert.That(manKetQua.ChenhLechTrenSuat, Is.EqualTo(13200m - 9490m));
        Assert.That(manKetQua.VuotDinhMuc, Is.True);
    }

    [Test]
    public void TinhChiPhiTheoHangMuc_WhenBuaKhongGanHangMuc_ExpectedBoQuaKhongTinh()
    {
        var gao = new HangHoa { Id = 2, TenHang = "Gạo bồ câu", DonGiaGanNhat = 21000m };
        var buaSang = new LoaiBuaAn { Id = 2, TenBua = "SÁNG", HangMucId = null, HangMuc = null };

        var monChao = new MonAn { Id = 2, TenMon = "Cháo tôm rim" };
        monChao.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
        {
            MonAnId = monChao.Id, MonAn = monChao, HangHoaId = gao.Id, HangHoa = gao, DinhLuong = 0.02m
        });

        var thucDon = new ThucDonEntity
        {
            Id = 2, LoaiBuaAnId = buaSang.Id, LoaiBuaAn = buaSang, MonAnId = monChao.Id, MonAn = monChao
        };

        var ketQua = _sut.TinhChiPhiTheoHangMuc(new[] { thucDon }, DinhMucMauGiao);

        Assert.That(ketQua, Is.Empty);
    }

    [Test]
    public void TinhChiPhiTheoHangMuc_WhenHangMucChuaCoDinhMucCauHinh_ExpectedDinhMucVaChenhLechNull()
    {
        var duaHau = new HangHoa { Id = 3, TenHang = "Dưa hấu", DonGiaGanNhat = 15000m };
        var buaTrangMieng = new LoaiBuaAn { Id = 3, TenBua = "TRÁNG MIỆNG", HangMucId = TrangMieng.Id, HangMuc = TrangMieng };

        var monDuaHau = new MonAn { Id = 3, TenMon = "Dưa hấu SG" };
        monDuaHau.DinhLuongNguyenLieus.Add(new DinhLuongNguyenLieu
        {
            MonAnId = monDuaHau.Id, MonAn = monDuaHau, HangHoaId = duaHau.Id, HangHoa = duaHau, DinhLuong = 0.1m
        });

        var thucDon = new ThucDonEntity
        {
            Id = 3, LoaiBuaAnId = buaTrangMieng.Id, LoaiBuaAn = buaTrangMieng, MonAnId = monDuaHau.Id, MonAn = monDuaHau
        };

        // DinhMucMauGiao chỉ có "Mặn", không có "Tráng miệng" -> chưa cấu hình định mức
        var ketQua = _sut.TinhChiPhiTheoHangMuc(new[] { thucDon }, DinhMucMauGiao);

        var trangMiengKetQua = ketQua.Single(k => k.HangMuc.Id == TrangMieng.Id);
        Assert.That(trangMiengKetQua.DinhMucTrenSuat, Is.Null);
        Assert.That(trangMiengKetQua.ChenhLechTrenSuat, Is.Null);
        Assert.That(trangMiengKetQua.VuotDinhMuc, Is.Null);
    }
}
