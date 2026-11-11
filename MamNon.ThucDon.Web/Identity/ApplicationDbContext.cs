using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Identity;

/// <summary>
/// DbContext riêng cho Identity (tài khoản/vai trò đăng nhập). Các bảng
/// nghiệp vụ (thực đơn, kho, hàng hóa...) nên nằm trong 1 DbContext khác
/// (VD ThucDonDbContext) để tách bạch rõ 2 mối quan tâm — nhưng vẫn trỏ
/// chung 1 database SQLite đã tạo ở các script trước.
/// </summary>
public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.HoTen).HasMaxLength(100).IsRequired();
            b.HasIndex(u => u.CoSoId);
        });
    }
}
