using MamNon.ThucDon.Web.Identity;
using Microsoft.AspNetCore.Identity;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>Tạo sẵn 5 Role cố định (NhaBep/ThuKho/KeToan/HieuTruong/QuanTri) khi ứng dụng khởi động lần đầu.</summary>
public static class RoleInitializer
{
    public static async Task SeedRolesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var vaiTro in VaiTro.TatCa)
        {
            if (!await roleManager.RoleExistsAsync(vaiTro))
            {
                await roleManager.CreateAsync(new IdentityRole(vaiTro));
            }
        }
    }
}
