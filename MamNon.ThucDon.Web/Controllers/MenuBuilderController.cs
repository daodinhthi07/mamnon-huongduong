using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MamNon.ThucDon.Web.Identity;
using MamNon.ThucDon.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MamNon.ThucDon.Web.Controllers;

/// <summary>
/// Nhân bản Chợ TC -> Chợ PH ok/Chợ ĐT ok bằng cách gọi lại script Python
/// (build_multi_facility_menu.py, xem PythonScript/). Đây là tính năng gộp
/// từ project MenuBuilderApi (.NET 6 + Swagger) độc lập trước đó vào thẳng
/// project MVC chính — 1 project duy nhất, dùng chung đăng nhập/phân quyền,
/// không cần mở 2 ứng dụng khác nhau nữa.
/// </summary>
[Authorize(Roles = $"{VaiTro.NhaBep},{VaiTro.ThuKho},{VaiTro.HieuTruong},{VaiTro.QuanTri},{VaiTro.QuanLy}")]
[Route("menu-builder")]
public sealed class MenuBuilderController : Controller
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MenuBuilderController> _logger;

    public MenuBuilderController(IConfiguration config, IWebHostEnvironment env, ILogger<MenuBuilderController> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => View(new MenuBuilderFormVm());

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Index(MenuBuilderFormVm model)
    {
        if (model.File is null || model.File.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "File tải lên đang trống.");
            return View(model);
        }

        if (!string.Equals(Path.GetExtension(model.File.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Chỉ nhận file .xlsx.");
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(model.StartDate) &&
            !DateTime.TryParseExact(model.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            ModelState.AddModelError(string.Empty, "Ngày bắt đầu tuần phải theo định dạng YYYY-MM-DD.");
            return View(model);
        }

        var loiPh = ValidateCountsPerDayJson(model.PhCountsPerDayJson, "Số cháu PH theo từng ngày");
        if (loiPh is not null) { ModelState.AddModelError(string.Empty, loiPh); return View(model); }

        var loiDt = ValidateCountsPerDayJson(model.DtCountsPerDayJson, "Số cháu ĐT theo từng ngày");
        if (loiDt is not null) { ModelState.AddModelError(string.Empty, loiDt); return View(model); }

        var loiTc = ValidateCountsPerDayJson(model.TcCountsPerDayJson, "Số cháu TC theo từng ngày");
        if (loiTc is not null) { ModelState.AddModelError(string.Empty, loiTc); return View(model); }

        var soOTcDaDien = new[] { model.TcSang, model.TcNhaTre, model.TcMauGiao }.Count(x => x.HasValue);
        if (soOTcDaDien is > 0 and < 3)
        {
            ModelState.AddModelError(string.Empty, "Đã điền một phần số cháu TC — cần điền đủ cả 3 ô (Sáng/Nhà trẻ/Mẫu giáo), hoặc để trống cả 3.");
            return View(model);
        }

        var tempFolder = _config["MenuBuilder:TempFolder"];
        tempFolder = string.IsNullOrWhiteSpace(tempFolder) ? Path.Combine(Path.GetTempPath(), "MenuBuilder") : tempFolder;
        Directory.CreateDirectory(tempFolder);

        var jobId = Guid.NewGuid().ToString("N");
        var inputPath = Path.Combine(tempFolder, $"{jobId}_input.xlsx");
        var outputPath = Path.Combine(tempFolder, $"{jobId}_output.xlsx");

        try
        {
            await using (var stream = new FileStream(inputPath, FileMode.Create))
            {
                await model.File.CopyToAsync(stream);
            }

            var configuredScriptPath = _config["MenuBuilder:ScriptPath"];
            var scriptPath = string.IsNullOrWhiteSpace(configuredScriptPath)
                ? Path.Combine(_env.ContentRootPath, "PythonScript", "build_multi_facility_menu.py")
                : configuredScriptPath;
            if (!System.IO.File.Exists(scriptPath))
            {
                ModelState.AddModelError(string.Empty, $"Không tìm thấy script tại '{scriptPath}'. Kiểm tra MenuBuilder:ScriptPath trong appsettings.json.");
                return View(model);
            }

            var arguments = new List<string>
            {
                scriptPath, inputPath, outputPath,
                "--ph-counts", model.PhSang.ToString(), model.PhNhaTre.ToString(), model.PhMauGiao.ToString(),
                "--dt-counts", model.DtSang.ToString(), model.DtNhaTre.ToString(), model.DtMauGiao.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(model.StartDate))
            {
                arguments.Add("--start-date");
                arguments.Add(model.StartDate);
            }
            if (!string.IsNullOrWhiteSpace(model.PhCountsPerDayJson))
            {
                arguments.Add("--ph-counts-per-day");
                arguments.Add(model.PhCountsPerDayJson);
            }
            if (!string.IsNullOrWhiteSpace(model.DtCountsPerDayJson))
            {
                arguments.Add("--dt-counts-per-day");
                arguments.Add(model.DtCountsPerDayJson);
            }
            if (soOTcDaDien == 3)
            {
                arguments.Add("--tc-counts");
                arguments.Add(model.TcSang!.Value.ToString());
                arguments.Add(model.TcNhaTre!.Value.ToString());
                arguments.Add(model.TcMauGiao!.Value.ToString());
            }
            if (!string.IsNullOrWhiteSpace(model.TcCountsPerDayJson))
            {
                arguments.Add("--tc-counts-per-day");
                arguments.Add(model.TcCountsPerDayJson);
            }

            var (exitCode, stdout, stderr) = await RunPythonScriptAsync(arguments);

            if (exitCode != 0)
            {
                _logger.LogWarning("Script chạy lỗi (exit={ExitCode}): {Stderr}", exitCode, stderr);
                ModelState.AddModelError(string.Empty, $"Script chạy thất bại (mã {exitCode}). Chi tiết: {stderr}");
                return View(model);
            }

            if (!System.IO.File.Exists(outputPath))
            {
                ModelState.AddModelError(string.Empty, "Script báo chạy xong nhưng không thấy file kết quả.");
                return View(model);
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(outputPath);
            var downloadName = $"{Path.GetFileNameWithoutExtension(model.File.FileName)}_da_dung.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadName);
        }
        catch (TimeoutException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ModelState.AddModelError(string.Empty,
                $"Không chạy được python ('{_config["MenuBuilder:PythonExecutable"]}'). Kiểm tra MenuBuilder:PythonExecutable trong appsettings.json và đảm bảo python đã cài + có trong PATH. Chi tiết: {ex.Message}");
            return View(model);
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private static string? ValidateCountsPerDayJson(string? json, string tenTruong)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return $"{tenTruong} không phải JSON hợp lệ: {ex.Message}"; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 6)
            {
                return $"{tenTruong} phải là mảng đúng 6 phần tử (Thứ 2 -> Thứ 7), " +
                       "ví dụ: [[220,70,290],[220,70,290],[200,65,280],[210,68,285],[215,70,288],[150,50,200]]";
            }

            var thu = new[] { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };
            var i = 0;
            foreach (var bo in doc.RootElement.EnumerateArray())
            {
                if (bo.ValueKind != JsonValueKind.Array || bo.GetArrayLength() != 3 ||
                    !bo.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
                {
                    return $"{tenTruong}: phần tử {thu[i]} phải là mảng đúng 3 số nguyên [ăn_sáng, nhà_trẻ, mẫu_giáo].";
                }
                i++;
            }
        }
        return null;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunPythonScriptAsync(List<string> arguments)
    {
        var pythonExe = _config["MenuBuilder:PythonExecutable"] ?? "python3";
        var timeoutSeconds = int.TryParse(_config["MenuBuilder:TimeoutSeconds"], out var t) ? t : 300;

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"Script chạy quá {timeoutSeconds}s, đã bị hủy.");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private void TryDelete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Không xoá được file tạm {Path}", path); }
    }
}
