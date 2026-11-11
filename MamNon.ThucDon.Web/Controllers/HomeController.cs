using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MamNon.ThucDon.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    // Trang chủ chính là Dashboard (DashboardController.Index, địa chỉ "/").

    [AllowAnonymous]
    [HttpGet("/Loi")]
    public IActionResult Loi() => View("Error");
}
