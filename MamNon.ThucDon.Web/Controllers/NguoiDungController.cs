using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Entities;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MamNon.ThucDon.Web.Controllers;

[Authorize(Roles = $"{VaiTro.HieuTruong},{VaiTro.QuanTri}")]
[Route("quan-ly-nguoi-dung")]
public sealed class NguoiDungController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ThucDonDbContext _db;

    public NguoiDungController(UserManager<ApplicationUser> userManager, ThucDonDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var vm = new QuanLyNguoiDungViewModel
        {
            DsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync()
        };
        vm.DsNguoiDung = await TaiDanhSachAsync(vm.DsCoSo);
        return View(vm);
    }

    [HttpPost("tao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tao(TaoNguoiDungVm nguoiDungMoi)
    {
        var dsCoSo = await _db.CoSos.Where(x => x.IsActive).OrderBy(x => x.TenCoSo).ToListAsync();

        if (!ModelState.IsValid)
        {
            return View("Index", new QuanLyNguoiDungViewModel
            {
                DsCoSo = dsCoSo,
                DsNguoiDung = await TaiDanhSachAsync(dsCoSo),
                NguoiDungMoi = nguoiDungMoi
            });
        }

        var user = new ApplicationUser
        {
            UserName = nguoiDungMoi.TenDangNhap.Trim(),
            HoTen = nguoiDungMoi.HoTen.Trim(),
            CoSoId = nguoiDungMoi.CoSoId,
            EmailConfirmed = true,
            IsActive = true
        };

        var ketQua = await _userManager.CreateAsync(user, nguoiDungMoi.MatKhau);
        if (!ketQua.Succeeded)
        {
            foreach (var loi in ketQua.Errors)
            {
                ModelState.AddModelError(string.Empty, loi.Description);
            }
            return View("Index", new QuanLyNguoiDungViewModel
            {
                DsCoSo = dsCoSo,
                DsNguoiDung = await TaiDanhSachAsync(dsCoSo),
                NguoiDungMoi = nguoiDungMoi
            });
        }

        await _userManager.AddToRoleAsync(user, nguoiDungMoi.VaiTro);

        TempData["ThongBao"] = $"Đã tạo tài khoản {user.UserName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("khoa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is not null)
        {
            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<NguoiDungRowVm>> TaiDanhSachAsync(List<CoSo> dsCoSo)
    {
        var coSoTheoId = dsCoSo.ToDictionary(cs => cs.Id, cs => cs.TenCoSo);
        var ketQua = new List<NguoiDungRowVm>();

        foreach (var user in _userManager.Users.OrderBy(u => u.UserName).ToList())
        {
            var vaiTros = await _userManager.GetRolesAsync(user);
            ketQua.Add(new NguoiDungRowVm
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                HoTen = user.HoTen,
                TenCoSo = user.CoSoId is not null && coSoTheoId.TryGetValue(user.CoSoId.Value, out var ten) ? ten : null,
                VaiTros = vaiTros,
                IsActive = user.IsActive
            });
        }

        return ketQua;
    }
}
