using MamNon.ThucDon.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;

namespace MamNon.ThucDon.Tests;

[TestFixture]
public sealed class TemplateWorkbookSmokeTests
{
    [Test]
    public void TaoFile_UpdatesSeatsAndPricesAndKeepsWorkbookReadable()
    {
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        var mauThuMuc = Path.Combine(root, "MamNon.ThucDon.Web", "App_Data", "MauTuan");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MauTuan:ThuMuc"] = mauThuMuc })
            .Build();
        var service = new MauTuanExcelService(configuration, new TestWebHostEnvironment());
        var mau = service.LietKe().First();
        var chiTiet = service.DocChiTiet(mau);
        const int ngay = 0;
        Assert.That(chiTiet.Suat, Does.ContainKey("TC"));
        Assert.That(chiTiet.Suat["TC"], Does.ContainKey(ngay));

        var fileMoi = service.TaoFile(
            mau,
            mau.ThuHai.AddDays(7),
            new Dictionary<string, double?[]> { ["TC|0"] = new double?[] { 123, 234, 345 } },
            new List<string>(),
            new Dictionary<string, double?[]> { ["TC|0"] = new double?[] { 10001, 29002, 29003 } });

        using var stream = new MemoryStream(fileMoi);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Chợ TC");
        var hangSuat = sheet.RowsUsed()
            .FirstOrDefault(row => row.Cell(8).GetFormattedString() == "123"
                && row.Cell(9).GetFormattedString() == "234"
                && row.Cell(10).GetFormattedString() == "345");

        Assert.That(hangSuat, Is.Not.Null, "The new attendance values should be present in the TC sheet.");
        var hangTien = sheet.Row(hangSuat!.RowNumber() - 1);
        Assert.That(hangTien.Cell(8).GetDouble(), Is.EqualTo(10001));
        Assert.That(hangTien.Cell(9).GetDouble(), Is.EqualTo(29002));
        Assert.That(hangTien.Cell(10).GetDouble(), Is.EqualTo(29003));
        Assert.That(workbook.Worksheets.Any(ws => ws.Name == "Thực đơn tuần ok"), Is.True);
        Assert.That(workbook.Worksheets.Count, Is.GreaterThan(1));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
