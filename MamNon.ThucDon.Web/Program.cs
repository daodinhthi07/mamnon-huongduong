using MamNon.ThucDon.Web.Infrastructure.Data;
using MamNon.ThucDon.Web.Infrastructure.Initializers;
using MamNon.ThucDon.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------
var configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Thiếu chuỗi kết nối 'Default' trong appsettings.json");

// Luôn neo đường dẫn SQLite vào thư mục ứng dụng, tránh đọc nhầm DB theo thư mục chạy hiện tại.
var dbFileName = "kho_thuc_pham_mam_non.db";
var dbPath = Path.Combine(builder.Environment.ContentRootPath, dbFileName);
var connectionString = $"Data Source={dbPath}";

// Identity — DÙNG EF Migrations (bảng chưa có sẵn trong script SQL nào)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Nghiệp vụ (danh mục, thực đơn, kho) — KHÔNG dùng EF Migrations, map vào
// schema đã tạo sẵn bởi 3 script 01/02/03_*.sql. MVC controller là request-scoped
// nên đăng ký DbContext bình thường là đủ (không cần IDbContextFactory như Blazor Server).
builder.Services.AddDbContext<ThucDonDbContext>(options =>
    options.UseSqlite(connectionString));

// Công khai thực đơn cho phụ huynh — bảng riêng (cong_khai_tuan/cong_khai_mon), không đụng dữ liệu cũ
builder.Services.AddDbContext<MamNon.ThucDon.Web.Infrastructure.Data.CongKhaiDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.TongHopNgayService>();   // chi tiết nguyên liệu & giá cả 3 cơ sở theo ngày (chụp lúc Công khai)
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.CongKhaiThucDonService>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.NhapThucDonExcelService>();

// ---------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/dang-nhap";
    options.AccessDeniedPath = "/khong-co-quyen";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ChinhSach.KhongPhaiQuanLy, policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx => !ctx.User.IsInRole(VaiTro.QuanLy)));
});

// ---------------------------------------------------------------------
// Services tính toán + xuất Excel (thuần, không phụ thuộc hạ tầng UI)
// ---------------------------------------------------------------------
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.NguyenLieuCalculator>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.HangMucNganSachCalculator>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.NganSachNgayCalculator>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.ExcelExportService>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.ChiTietNgayTinhToanService>();
// Số lượng theo ngày mẫu: thực đơn trùng ngày trong file Excel mẫu -> dùng đúng số lượng của ngày mẫu
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.MauSoLuongService>();
// Mẫu tuần: lưu các file Excel mẫu (TUẦN n THÁNG m A..E) và xuất lại y như mẫu, chỉ đổi ngày + số suất
builder.Services.AddSingleton<MamNon.ThucDon.Web.Services.MauTuanExcelService>();
builder.Services.AddScoped<MamNon.ThucDon.Web.Services.ThucDonTuMauService>();

// ---------------------------------------------------------------------
// MVC
// ---------------------------------------------------------------------
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Loi");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Khởi tạo dữ liệu nền tảng lúc chạy lần đầu (idempotent — chạy lại vô hại)
await app.SeedRolesAsync();
await app.SeedTaiKhoanHieuTruongMacDinhAsync();
await app.SeedTaiKhoanQuanLyAsync();   // tài khoản "quanly" (vai trò QuanLy, bị chặn vài trang) — idempotent
await app.DamBaoCotGhiChuHangHoaAsync();   // thêm cột hang_hoa.ghi_chu nếu CSDL chưa có (idempotent)
await app.DamBaoCotNguyenLieuChinhAsync();   // thêm cột dinh_luong_nguyen_lieu.la_nguyen_lieu_chinh nếu CSDL chưa có (idempotent) — PHẢI chạy trước mọi truy vấn công thức món
await app.DamBaoBangQuyTacNguyenLieuAsync();   // bảng quy tắc nguyên liệu cố định (sửa ở /quy-tac-nguyen-lieu) + nạp quy tắc mặc định lần đầu
await app.DamBaoBangCongKhaiAsync();   // bảng công khai thực đơn phụ huynh (idempotent)
await app.DamBaoCotDinhLuongNhaTreAsync();   // cột dinh_luong_nha_tre (định lượng riêng Nhà trẻ) ở bảng chung + bảng theo chợ — gọi SAU dòng trên vì bảng theo chợ do nó tạo

await app.DamBaoMonPhuAsync();   // món phụ (Phụ sáng / Phụ chiều) trong Danh mục món ăn — gọi SAU DamBaoBangCongKhaiAsync (idempotent)

app.Run();
