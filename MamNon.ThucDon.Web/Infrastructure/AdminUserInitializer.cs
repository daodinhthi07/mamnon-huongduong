using MamNon.ThucDon.Web.Identity;
using Microsoft.AspNetCore.Identity;

namespace MamNon.ThucDon.Web.Infrastructure.Initializers;

/// <summary>
/// Tạo tài khoản Hiệu trưởng ban đầu bằng mật khẩu cấu hình, không dùng mật khẩu
/// mặc định được lưu trong mã nguồn.
/// </summary>
public static class AdminUserInitializer
{
    private const string TenDangNhapMacDinh = "hieutruong";

    public static async Task SeedTaiKhoanHieuTruongMacDinhAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var matKhauBanDau = app.Configuration["Admin:InitialPassword"];
        var datLaiMatKhauTaiKhoanHienCo = app.Configuration.GetValue<bool>("Admin:ResetExistingPassword");
        var taiKhoanHienCo = await userManager.FindByNameAsync(TenDangNhapMacDinh);

        if (string.IsNullOrWhiteSpace(matKhauBanDau))
        {
            if (datLaiMatKhauTaiKhoanHienCo)
            {
                throw new InvalidOperationException(
                    "Cần cấu hình Admin:InitialPassword trước khi đặt lại mật khẩu tài khoản Hiệu trưởng.");
            }

            if (taiKhoanHienCo is null)
            {
                app.Logger.LogWarning(
                    "Chưa tạo tài khoản Hiệu trưởng vì chưa cấu hình Admin:InitialPassword.");
            }

            return;
        }

        if (matKhauBanDau.Length < 8)
        {
            throw new InvalidOperationException("Admin:InitialPassword phải có ít nhất 8 ký tự.");
        }

        if (taiKhoanHienCo is not null)
        {
            if (!datLaiMatKhauTaiKhoanHienCo)
            {
                return;
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(taiKhoanHienCo);
            var ketQuaDatLai = await userManager.ResetPasswordAsync(taiKhoanHienCo, token, matKhauBanDau);
            if (!ketQuaDatLai.Succeeded)
            {
                var loi = string.Join("; ", ketQuaDatLai.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Không thể đặt mật khẩu ban đầu cho tài khoản Hiệu trưởng: {loi}");
            }

            app.Logger.LogInformation("Đã đặt lại mật khẩu ban đầu cho tài khoản Hiệu trưởng theo cấu hình.");
            return;
        }

        var hieuTruong = new ApplicationUser
        {
            UserName = TenDangNhapMacDinh,
            HoTen = "Hiệu trưởng",
            CoSoId = null, // quản lý chung cả 3 cơ sở
            EmailConfirmed = true
        };

        var ketQua = await userManager.CreateAsync(hieuTruong, matKhauBanDau);
        if (!ketQua.Succeeded)
        {
            var loi = string.Join("; ", ketQua.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Không thể tạo tài khoản Hiệu trưởng ban đầu: {loi}");
        }

        var ketQuaGanVaiTro = await userManager.AddToRoleAsync(hieuTruong, VaiTro.HieuTruong);
        if (!ketQuaGanVaiTro.Succeeded)
        {
            var loi = string.Join("; ", ketQuaGanVaiTro.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Không thể gán vai trò Hiệu trưởng cho tài khoản ban đầu: {loi}");
        }
    }
}
