using System.Text;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
// Bí danh bắt buộc: tên namespace gốc "MamNon.ThucDon.Web" trùng 1 đoạn với tên
// entity "ThucDon", khiến trình biên dịch hiểu nhầm "ThucDon" là namespace thay vì
// class (lỗi CS0118) ở mọi file KHÔNG cùng namespace với Entities. Alias này ép rõ.
using ThucDonEntity = MamNon.ThucDon.Web.Infrastructure.Entities.ThucDon;

namespace MamNon.ThucDon.Web.Infrastructure.Data;

/// <summary>
/// DbContext cho toàn bộ nghiệp vụ (danh mục, thực đơn, kho). QUAN TRỌNG:
/// context này KHÔNG dùng EF Migrations — schema chuẩn là 3 file
/// 01_kho_thuc_pham_mam_non.sql / 02_thuc_don_mon_an.sql /
/// 03_dinh_muc_ty_le_ngan_sach.sql đã chạy tay trên SQLite. DbContext ở đây
/// chỉ để map/query/CRUD lên schema có sẵn đó — không gọi
/// "dotnet ef migrations add" cho context này, chỉ dùng cho
/// ApplicationDbContext (Identity).
/// </summary>
public sealed class ThucDonDbContext : DbContext
{
    public ThucDonDbContext(DbContextOptions<ThucDonDbContext> options) : base(options)
    {
    }

    public DbSet<CoSo> CoSos => Set<CoSo>();
    public DbSet<DonViTinh> DonViTinhs => Set<DonViTinh>();
    public DbSet<KhoiHoc> KhoiHocs => Set<KhoiHoc>();
    public DbSet<NhomChiPhi> NhomChiPhis => Set<NhomChiPhi>();
    public DbSet<HangMucNganSach> HangMucNganSachs => Set<HangMucNganSach>();
    public DbSet<NhomBuaAn> NhomBuaAns => Set<NhomBuaAn>();
    public DbSet<HangHoa> HangHoas => Set<HangHoa>();
    public DbSet<LoaiBuaAn> LoaiBuaAns => Set<LoaiBuaAn>();
    public DbSet<MonAn> MonAns => Set<MonAn>();
    public DbSet<DinhLuongNguyenLieu> DinhLuongNguyenLieus => Set<DinhLuongNguyenLieu>();
    public DbSet<NgayHoc> NgayHocs => Set<NgayHoc>();
    public DbSet<DinhMucAnNgay> DinhMucAnNgays => Set<DinhMucAnNgay>();
    public DbSet<DinhMucTyLeNganSach> DinhMucTyLeNganSachs => Set<DinhMucTyLeNganSach>();
    public DbSet<ThucDonEntity> ThucDons => Set<ThucDonEntity>();
    public DbSet<PhieuNhapKho> PhieuNhapKhos => Set<PhieuNhapKho>();
    public DbSet<ChiTietNhapKho> ChiTietNhapKhos => Set<ChiTietNhapKho>();
    public DbSet<PhieuXuatKho> PhieuXuatKhos => Set<PhieuXuatKho>();
    public DbSet<ChiTietXuatKho> ChiTietXuatKhos => Set<ChiTietXuatKho>();
    public DbSet<DinhMucGiaViChuan> DinhMucGiaViChuans => Set<DinhMucGiaViChuan>();
    public DbSet<QuyTacNguyenLieu> QuyTacNguyenLieus => Set<QuyTacNguyenLieu>();
    public DbSet<DinhLuongNguyenLieuTheoCoSo> DinhLuongNguyenLieuTheoCoSos => Set<DinhLuongNguyenLieuTheoCoSo>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---- Khóa chính / khóa ngoại / cột định danh theo tên bảng snake_case ----
        b.Entity<CoSo>().ToTable("co_so");
        b.Entity<DonViTinh>().ToTable("don_vi_tinh");
        b.Entity<KhoiHoc>().ToTable("khoi_hoc");
        b.Entity<NhomChiPhi>().ToTable("nhom_chi_phi");
        b.Entity<HangMucNganSach>().ToTable("hang_muc_ngan_sach");
        b.Entity<NhomBuaAn>().ToTable("nhom_bua_an");
        b.Entity<HangHoa>().ToTable("hang_hoa");
        b.Entity<LoaiBuaAn>().ToTable("loai_bua_an");
        b.Entity<MonAn>().ToTable("mon_an");
        b.Entity<DinhLuongNguyenLieu>().ToTable("dinh_luong_nguyen_lieu");
        b.Entity<NgayHoc>().ToTable("ngay_hoc");
        b.Entity<DinhMucAnNgay>().ToTable("dinh_muc_an_ngay");
        // Map tường minh: bộ chuyển snake_case tự động ra "tien_an1_chau" (không tách
        // số ra khỏi chữ), trong khi cột thật trong SQL là "tien_an_1_chau".
        b.Entity<DinhMucAnNgay>().Property(x => x.TienAn1Chau).HasColumnName("tien_an_1_chau");

        // Cùng loại lỗi với TienAn1Chau ở trên: "DinhLuong1Suat" tự chuyển thành
        // "dinh_luong1_suat" (thiếu "_" trước số 1), trong khi cột thật trong SQL là
        // "dinh_luong_1_suat". Map tường minh + loại trừ khỏi vòng lặp tự động bên dưới.
        b.Entity<DinhMucGiaViChuan>().Property(x => x.DinhLuong1Suat).HasColumnName("dinh_luong_1_suat");
        b.Entity<DinhMucTyLeNganSach>().ToTable("dinh_muc_ty_le_ngan_sach");
        b.Entity<ThucDonEntity>().ToTable("thuc_don");
        b.Entity<PhieuNhapKho>().ToTable("phieu_nhap_kho");
        b.Entity<ChiTietNhapKho>().ToTable("chi_tiet_nhap_kho");
        b.Entity<PhieuXuatKho>().ToTable("phieu_xuat_kho");
        b.Entity<ChiTietXuatKho>().ToTable("chi_tiet_xuat_kho");
        // Entity mới thêm sau này bị thiếu khai báo .ToTable() tường minh như các bảng
        // trên — EF mặc định lấy tên property DbSet ("DinhMucGiaViChuans", PascalCase)
        // làm tên bảng, trong khi bảng thật tạo bằng SQL là "dinh_muc_gia_vi_chuan" ->
        // lỗi runtime "no such table: DinhMucGiaViChuans". Bổ sung lại cho khớp.
        b.Entity<DinhMucGiaViChuan>().ToTable("dinh_muc_gia_vi_chuan");
        b.Entity<QuyTacNguyenLieu>().ToTable("quy_tac_nguyen_lieu");
        b.Entity<DinhLuongNguyenLieuTheoCoSo>().ToTable("dinh_luong_nguyen_lieu_theo_co_so");
        // Định lượng riêng cho khối Nhà trẻ (NULL = dùng chung cột dinh_luong của Mẫu giáo) — ở cả bảng chung lẫn bảng theo chợ.
        b.Entity<DinhLuongNguyenLieu>().Property(x => x.DinhLuongNhaTre).HasColumnName("dinh_luong_nha_tre");
        b.Entity<DinhLuongNguyenLieuTheoCoSo>().Property(x => x.DinhLuongNhaTre).HasColumnName("dinh_luong_nha_tre");
        b.Entity<DinhLuongNguyenLieuTheoCoSo>().HasOne(x => x.MonAn).WithMany()
            .HasForeignKey(x => x.MonAnId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DinhLuongNguyenLieuTheoCoSo>().HasOne(x => x.HangHoa).WithMany()
            .HasForeignKey(x => x.HangHoaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<QuyTacNguyenLieu>().HasOne(x => x.HangHoa).WithMany()
            .HasForeignKey(x => x.HangHoaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<QuyTacNguyenLieu>().Property(x => x.MonAnId).HasColumnName("mon_an_id");
        b.Entity<QuyTacNguyenLieu>().HasOne(x => x.MonAn).WithMany()
            .HasForeignKey(x => x.MonAnId).OnDelete(DeleteBehavior.Cascade);

        // ---- Enum <-> đúng chuỗi tiếng Việt đã dùng trong CHECK constraint của SQLite ----
        b.Entity<HangHoa>().Property(x => x.LoaiHang).HasConversion(
            v => v == LoaiHangHoa.ThucPhamTuoi ? "Thực phẩm tươi"
                : v == LoaiHangHoa.GiaVi ? "Gia vị"
                : "Khác",
            v => v == "Thực phẩm tươi" ? LoaiHangHoa.ThucPhamTuoi
                : v == "Gia vị" ? LoaiHangHoa.GiaVi
                : LoaiHangHoa.Khac);

        b.Entity<NgayHoc>().Property(x => x.TrangThai).HasConversion(
            v => v == TrangThaiNgay.NghiLe ? "Nghỉ lễ"
                : v == TrangThaiNgay.NghiKhac ? "Nghỉ khác"
                : "Học bình thường",
            v => v == "Nghỉ lễ" ? TrangThaiNgay.NghiLe
                : v == "Nghỉ khác" ? TrangThaiNgay.NghiKhac
                : TrangThaiNgay.HocBinhThuong);

        b.Entity<ThucDonEntity>().Property(x => x.TrangThai).HasConversion(
            v => v == TrangThaiThucDon.ChoDuyet ? "Chờ duyệt"
                : v == TrangThaiThucDon.DaDuyet ? "Đã duyệt"
                : "Nháp",
            v => v == "Chờ duyệt" ? TrangThaiThucDon.ChoDuyet
                : v == "Đã duyệt" ? TrangThaiThucDon.DaDuyet
                : TrangThaiThucDon.Nhap);

        b.Entity<PhieuXuatKho>().Property(x => x.LoaiXuat).HasConversion(
            v => v == LoaiXuatKho.ThucPhamSang ? "Thực phẩm sáng"
                : v == LoaiXuatKho.ThucPhamChinhXe ? "Thực phẩm chính + xế"
                : "Gia vị hằng ngày",
            v => v == "Thực phẩm sáng" ? LoaiXuatKho.ThucPhamSang
                : v == "Thực phẩm chính + xế" ? LoaiXuatKho.ThucPhamChinhXe
                : LoaiXuatKho.GiaViHangNgay);

        // ---- Cột tính tự động (GENERATED ALWAYS AS ... STORED phía SQLite) — EF chỉ đọc, không ghi ----
        b.Entity<ChiTietNhapKho>().Property(x => x.ThanhTien).ValueGeneratedOnAddOrUpdate();
        b.Entity<ChiTietXuatKho>().Property(x => x.ThanhTien).ValueGeneratedOnAddOrUpdate();

        // ---- Độ chính xác số thập phân (khớp DECIMAL trong SQL) ----
        foreach (var p in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            p.SetPrecision(14);
            p.SetScale(4);
        }

        // ---- Ràng buộc duy nhất khớp UNIQUE KEY trong SQL ----
        b.Entity<CoSo>().HasIndex(x => x.MaCoSo).IsUnique();
        b.Entity<HangHoa>().HasIndex(x => x.TenHang).IsUnique();
        b.Entity<MonAn>().HasIndex(x => x.TenMon).IsUnique();
        b.Entity<NhomBuaAn>().HasIndex(x => x.TenNhom).IsUnique();
        b.Entity<NgayHoc>().HasIndex(x => new { x.CoSoId, x.Ngay }).IsUnique();
        b.Entity<LoaiBuaAn>().HasIndex(x => new { x.KhoiId, x.TenBua }).IsUnique();
        b.Entity<DinhLuongNguyenLieu>().HasIndex(x => new { x.MonAnId, x.HangHoaId }).IsUnique();
        b.Entity<DinhMucAnNgay>().HasIndex(x => new { x.CoSoId, x.NhomChiPhiId, x.Ngay }).IsUnique();
        b.Entity<DinhMucTyLeNganSach>().HasIndex(x => new { x.KhoiId, x.HangMucId, x.NgayApDungTu }).IsUnique();
        b.Entity<ThucDonEntity>().HasIndex(x => new { x.CoSoId, x.KhoiId, x.NgayHocId, x.LoaiBuaAnId }).IsUnique();

        // ---- Quan hệ khóa ngoại tường minh (tránh EF tự đoán sai với nhiều FK cùng trỏ 1 bảng) ----
        b.Entity<LoaiBuaAn>().HasOne(x => x.HangMuc).WithMany(x => x.LoaiBuaAns)
            .HasForeignKey(x => x.HangMucId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<LoaiBuaAn>().HasOne(x => x.NhomBuaAn).WithMany(x => x.LoaiBuaAns)
            .HasForeignKey(x => x.NhomBuaAnId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<MonAn>().HasOne(x => x.NhomBuaAn).WithMany(x => x.MonAns)
            .HasForeignKey(x => x.NhomBuaAnId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ThucDonEntity>().HasOne(x => x.MonAn).WithMany(x => x.ThucDons)
            .HasForeignKey(x => x.MonAnId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ChiTietXuatKho>().HasOne(x => x.NhomChiPhi).WithMany(x => x.ChiTietXuatKhos)
            .HasForeignKey(x => x.NhomChiPhiId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<PhieuNhapKho>().HasOne(x => x.CoSo).WithMany(x => x.PhieuNhapKhos)
            .HasForeignKey(x => x.CoSoId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<PhieuXuatKho>().HasOne(x => x.CoSo).WithMany(x => x.PhieuXuatKhos)
            .HasForeignKey(x => x.CoSoId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<DinhMucGiaViChuan>().HasIndex(x => new { x.HangHoaId, x.NhomChiPhiId }).IsUnique();
        b.Entity<DinhMucGiaViChuan>().HasOne(x => x.HangHoa).WithMany()
            .HasForeignKey(x => x.HangHoaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DinhMucGiaViChuan>().HasOne(x => x.NhomChiPhi).WithMany()
            .HasForeignKey(x => x.NhomChiPhiId).OnDelete(DeleteBehavior.Restrict);

        // ChiTietNhapKho.Phieu / ChiTietXuatKho.Phieu KHÔNG được khai báo tường minh trước
        // đây — EF Core tự đoán FK theo quy ước tên "<TênNavigation>Id" (tức "PhieuId"),
        // không khớp với property thật "PhieuNhapId"/"PhieuXuatId", nên tự tạo thêm 1
        // shadow property "PhieuId" (map ra cột "phieu_id" — KHÔNG tồn tại trong DB thật)
        // thay vì dùng cột "phieu_nhap_id"/"phieu_xuat_id" đã có sẵn, gây lỗi runtime
        // "no such column: c.phieu_id" ngay khi query PhieuXuatKho kèm Include(ChiTiets).
        // Khai báo tường minh để EF dùng đúng property thật, không tự sinh thêm cột ảo.
        b.Entity<ChiTietNhapKho>().HasOne(x => x.Phieu).WithMany(x => x.ChiTiets)
            .HasForeignKey(x => x.PhieuNhapId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<ChiTietXuatKho>().HasOne(x => x.Phieu).WithMany(x => x.ChiTiets)
            .HasForeignKey(x => x.PhieuXuatId).OnDelete(DeleteBehavior.Restrict);

        // ---- Tự chuyển tên cột PascalCase (C#) -> snake_case (đúng cột đã tạo trong SQL) ----
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // DinhMucAnNgay.TienAn1Chau đã map tường minh ở trên thành "tien_an_1_chau"
                // (cột thật trong SQL) — bộ tự chuyển bên dưới cho ra "tien_an1_chau" (sai,
                // không tách số ra khỏi chữ), nên phải loại trừ để không bị ghi đè lại.
                if (entity.ClrType == typeof(DinhMucAnNgay) && property.Name == nameof(DinhMucAnNgay.TienAn1Chau))
                    continue;
                if (entity.ClrType == typeof(DinhMucGiaViChuan) && property.Name == nameof(DinhMucGiaViChuan.DinhLuong1Suat))
                    continue;

                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }
}
