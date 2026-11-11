using System.ComponentModel.DataAnnotations;
using MamNon.ThucDon.Web.Infrastructure.Entities;

namespace MamNon.ThucDon.Web.ViewModels;

public sealed class NguoiDungRowVm
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string HoTen { get; set; } = string.Empty;
    public string? TenCoSo { get; set; }
    public IList<string> VaiTros { get; set; } = new List<string>();
    public bool IsActive { get; set; }
}

public sealed class TaoNguoiDungVm
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
    public string TenDangNhap { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập họ tên")]
    public string HoTen { get; set; } = string.Empty;

    public int? CoSoId { get; set; }

    public string VaiTro { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu tạm thời")]
    public string MatKhau { get; set; } = string.Empty;
}

public sealed class QuanLyNguoiDungViewModel
{
    public List<CoSo> DsCoSo { get; set; } = new();
    public List<NguoiDungRowVm> DsNguoiDung { get; set; } = new();
    public TaoNguoiDungVm NguoiDungMoi { get; set; } = new();
}
