using MamNon.ThucDon.Web.Identity;
using Microsoft.AspNetCore.Identity;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>
/// Tạo tài khoản "quanly" (vai trò QuanLy) khi có cấu hình QuanLy:InitialPassword.
/// Idempotent: tài khoản đã có thì chỉ đảm bảo đúng vai trò, KHÔNG đổi mật khẩu
/// (trừ khi bật QuanLy:ResetExistingPassword = true).
/// </summary>
public static class QuanLyUserInitializer
{
    private const string TenDangNhap = "quanly";

    public static async Task SeedTaiKhoanQuanLyAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var matKhau = app.Configuration["QuanLy:InitialPassword"];
        var datLai = app.Configuration.GetValue<bool>("QuanLy:ResetExistingPassword");

        var user = await userManager.FindByNameAsync(TenDangNhap);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(matKhau))
            {
                app.Logger.LogWarning("Chưa tạo tài khoản quanly vì chưa cấu hình QuanLy:InitialPassword.");
                return;
            }

            if (matKhau.Length < 8)
                throw new InvalidOperationException("QuanLy:InitialPassword phải có ít nhất 8 ký tự.");

            user = new ApplicationUser
            {
                UserName = TenDangNhap,
                HoTen = "Quản lý",
                CoSoId = null,
                EmailConfirmed = true
            };
            var kq = await userManager.CreateAsync(user, matKhau);
            if (!kq.Succeeded)
                throw new InvalidOperationException("Không thể tạo tài khoản quanly: " + string.Join("; ", kq.Errors.Select(e => e.Description)));
            app.Logger.LogInformation("Đã tạo tài khoản quanly.");
        }
        else if (datLai)
        {
            if (string.IsNullOrWhiteSpace(matKhau))
                throw new InvalidOperationException("Cần cấu hình QuanLy:InitialPassword trước khi đặt lại mật khẩu tài khoản quanly.");
            if (matKhau.Length < 8)
                throw new InvalidOperationException("QuanLy:InitialPassword phải có ít nhất 8 ký tự.");

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var kq = await userManager.ResetPasswordAsync(user, token, matKhau);
            if (!kq.Succeeded)
                throw new InvalidOperationException("Không thể đặt lại mật khẩu quanly: " + string.Join("; ", kq.Errors.Select(e => e.Description)));
            app.Logger.LogInformation("Đã đặt lại mật khẩu tài khoản quanly.");
        }
        else
        {
            app.Logger.LogInformation("Tài khoản quanly đã tồn tại — giữ nguyên mật khẩu hiện có.");
        }

        if (!await userManager.IsInRoleAsync(user, VaiTro.QuanLy))
            await userManager.AddToRoleAsync(user, VaiTro.QuanLy);
    }
}
