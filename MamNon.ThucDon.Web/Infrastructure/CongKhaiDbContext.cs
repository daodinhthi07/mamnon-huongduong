using System.Text;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Infrastructure.Data;

/// <summary>DbContext RIÊNG cho module "Công khai thực đơn" (2 bảng cong_khai_tuan / cong_khai_mon, cùng file SQLite) —
/// tách riêng để không đụng ThucDonDbContext và dữ liệu nghiệp vụ hiện có.</summary>
public sealed class CongKhaiDbContext : DbContext
{
    public CongKhaiDbContext(DbContextOptions<CongKhaiDbContext> options) : base(options) { }

    public DbSet<CongKhaiTuan> CongKhaiTuans => Set<CongKhaiTuan>();
    public DbSet<CongKhaiMon> CongKhaiMons => Set<CongKhaiMon>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CongKhaiTuan>().ToTable("cong_khai_tuan");
        b.Entity<CongKhaiMon>().ToTable("cong_khai_mon");
        b.Entity<CongKhaiTuan>().HasIndex(x => x.TuanBatDau).IsUnique();
        b.Entity<CongKhaiMon>().HasOne(x => x.CongKhaiTuan).WithMany(x => x.Mons)
            .HasForeignKey(x => x.CongKhaiTuanId).OnDelete(DeleteBehavior.Cascade);

        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch)) { if (i > 0) sb.Append('_'); sb.Append(char.ToLowerInvariant(ch)); }
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
