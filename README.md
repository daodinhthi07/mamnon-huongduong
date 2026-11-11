# Hệ thống Thực đơn & Kho — Trường Mầm non

**Kiến trúc: ASP.NET Core MVC** (Controller + View .cshtml + jQuery-free JS thuần
cho phần AJAX) — đã chuyển hẳn từ Blazor Server sang MVC theo đúng phong cách
project ElectricCo2 hiện có, không còn Razor Component nào.

## Cấu trúc solution

```
MamNon.ThucDon.sln
├── MamNon.ThucDon.Web/
│   ├── Controllers/
│   │   ├── AccountController.cs      (đăng nhập/đăng xuất, /khong-co-quyen)
│   │   ├── HomeController.cs         (trang chủ, /Loi)
│   │   ├── ThucDonController.cs      (soạn thực đơn — có 2 action AJAX JSON)
│   │   ├── DuyetThucDonController.cs (duyệt / trả về nháp)
│   │   ├── KhoController.cs          (nhập kho, xuất kho)
│   │   ├── NguoiDungController.cs    (quản lý tài khoản)
│   │   └── XuatExcelController.cs    (chọn tham số + tải file)
│   ├── ViewModels/
│   │   ├── AccountViewModels.cs
│   │   ├── ThucDon/  (Soan*, TinhToanRequest/Response, NhomThucDonChoDuyet, XuatExcelViewModel)
│   │   ├── Kho/      (NhapKhoViewModel, XuatKhoViewModel)
│   │   └── NguoiDung/
│   ├── Views/
│   │   ├── _ViewImports.cshtml, _ViewStart.cshtml
│   │   ├── Shared/_Layout.cshtml, Error.cshtml
│   │   ├── Account/, Home/, ThucDon/, DuyetThucDon/, Kho/, NguoiDung/, XuatExcel/
│   ├── Infrastructure/
│   │   ├── Data/ThucDonDbContext.cs       (KHÔNG dùng EF Migrations)
│   │   ├── Entities/                       (16 entity, map đúng 3 script SQL)
│   │   └── Initializers/                   (seed Role + tài khoản Hiệu trưởng mặc định)
│   ├── Identity/ (ApplicationUser + ApplicationDbContext — CÓ dùng EF Migrations)
│   ├── Services/ (4 calculator thuần + ExcelExportService, dùng chung mọi Controller)
│   ├── wwwroot/js/
│   │   ├── soan-thuc-don.js   (AJAX tính trực quan cho trang Soạn thực đơn)
│   │   ├── kho-dong-hang.js   (thêm/xóa dòng, tính tổng — dùng chung Nhập/Xuất kho)
│   │   └── xuat-kho.js        (ẩn/hiện cột Nhóm chi phí, đối chiếu ngân sách)
│   └── Program.cs
├── MamNon.ThucDon.Tests/ (NUnit — không đổi gì, vẫn test Services/Entities thuần)
└── Database/ (3 script SQL 01/02/03_*.sql)
```

## Vì sao MVC thay vì Blazor Server (đổi theo yêu cầu)

Bản trước dùng Blazor Server vì tự động re-render khi đổi lựa chọn (không cần
viết JS). Bản này đổi hẳn sang MVC thật để khớp đúng project ElectricCo2 đang
dùng — cái giá phải trả: **phần "tính trực quan" ở trang Soạn thực đơn giờ
cần JS + AJAX viết tay** (`soan-thuc-don.js`) thay vì Blazor tự lo, vì MVC
render theo từng request HTTP, không có kênh cập nhật 2 chiều như SignalR của
Blazor Server.

## Cách trang Soạn thực đơn giữ được "tính trực quan" mà không có Blazor

1. `GET /soan-thuc-don` trả về View với danh mục (cơ sở/khối/nhóm chi
   phí/món ăn) nhúng sẵn, và số liệu đã lưu trước đó (nếu có) cho ngày đang xem.
2. Mỗi khi đổi Khối → JS gọi `GET /soan-thuc-don/bua-theo-khoi?khoiId=..`
   (trả JSON) để dựng lại bảng chọn món theo đúng bữa của khối đó.
3. Mỗi khi đổi bất kỳ ô nào (số cháu, món, tiền/suất...) → JS gọi
   `POST /soan-thuc-don/tinh-toan` (JSON in/out) — Controller dùng lại
   **nguyên vẹn** `NguyenLieuCalculator`/`HangMucNganSachCalculator`/
   `NganSachNgayCalculator` đã có sẵn (không viết lại logic), trả kết quả
   JSON, JS render lại 3 bảng kết quả.
4. Bấm "Lưu nháp"/"Gửi duyệt" → JS gom lựa chọn hiện tại thành 1 form POST
   thường (`LuuThucDonFormModel`), Controller lưu và redirect chuẩn MVC
   (PRG pattern), không dùng AJAX cho bước này để tận dụng `TempData` báo
   thành công đúng chuẩn.

## Việc cần làm để chạy lần đầu (SQLite)

1. Tạo file database + chạy 3 script SQL trong `Database/` (nối tiếp, ĐÚNG thứ tự):
   ```
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/01_kho_thuc_pham_mam_non.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/02_thuc_don_mon_an.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/03_dinh_muc_ty_le_ngan_sach.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/04_nhom_bua_an.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/05_seed_hang_hoa.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/06_seed_mon_an_tu_thuc_don.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/07_gop_khoi_nha_tre.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/08_sua_phan_loai_gia_vi.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/09_cap_nhat_dinh_luong_mon_an.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/10_sua_lai_phan_loai_gia_vi_doi_chieu_thuc_te.sql
   sqlite3 MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db < Database/11_tach_an_sang_nha_tre.sql
   ```
   (Không có `sqlite3` CLI thì dùng DB Browser for SQLite, hoặc extension SQLite Viewer trong VS Code, mở từng file `.sql` rồi chạy.)
2. `appsettings.json` → `ConnectionStrings:Default` mặc định đã là
   `Data Source=kho_thuc_pham_mam_non.db` — file `.db` đặt CÙNG THƯ MỤC với
   `MamNon.ThucDon.Web.csproj` khi chạy `dotnet run` (đường dẫn tương đối
   tính theo thư mục làm việc lúc chạy).
3. Cài gói (đã khai trong `.csproj`, chỉ cần `dotnet restore`):
   `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`,
   `Microsoft.EntityFrameworkCore.Design`, `ClosedXML`.
4. Tạo & áp dụng migration **chỉ cho Identity** (bảng Identity KHÔNG có
   trong 3 script SQL ở trên — phải tạo bằng migration, sẽ tự thêm vào
   CÙNG file `.db`):
   ```
   dotnet ef migrations add InitIdentity -c ApplicationDbContext -p MamNon.ThucDon.Web -s MamNon.ThucDon.Web
   dotnet ef database update -c ApplicationDbContext -p MamNon.ThucDon.Web -s MamNon.ThucDon.Web
   ```
   (KHÔNG chạy migration cho `ThucDonDbContext`.)
5. `dotnet build` toàn solution, `dotnet test` (6 test NUnit không đổi từ bản Blazor).
6. Tạo mật khẩu quản trị ban đầu ngoài mã nguồn rồi chạy ứng dụng:
   ```
   dotnet user-secrets set "Admin:InitialPassword" "<mat-khau-ngau-nhien-it-nhat-8-ky-tu>" --project MamNon.ThucDon.Web
   dotnet run --project MamNon.ThucDon.Web
   ```
   Ứng dụng tự tạo 5 Role và tài khoản `hieutruong` nếu chưa có. Không
   lưu mật khẩu thật trong `appsettings.json` hoặc commit lên Git.

## Chạy demo miễn phí trên Render

Repo có `Dockerfile` và `render.yaml` để Render build ứng dụng .NET 8 cùng
Python/openpyxl. Sau khi đưa repo lên GitHub:

1. Tạo Web Service trên Render, kết nối repo và chọn Blueprint (`render.yaml`).
2. Khi Render hỏi `Admin__InitialPassword`, nhập mật khẩu mạnh riêng cho demo.
   `Admin__ResetExistingPassword` đang bật để thay mật khẩu cũ trong DB demo
   ngay lần khởi động đầu tiên.
3. Sau khi deploy thành công, đổi `Admin__ResetExistingPassword` thành `false`
   trong Environment của Render để các lần khởi động sau không ghi đè mật khẩu
   đã đổi. Không chia sẻ secret này hoặc commit vào repo.
4. Render cấp địa chỉ miễn phí dạng `*.onrender.com`; domain riêng chỉ dùng
   được nếu bạn đã sở hữu domain đó.

**Chỉ dùng làm demo:** dịch vụ Free có thể ngủ sau 15 phút không có truy cập
(lần mở lại có thể phải chờ khoảng một phút). Hệ thống tệp là tạm thời; mọi
thay đổi SQLite sẽ mất khi dịch vụ ngủ, khởi động lại hoặc deploy lại. Không
dùng bản miễn phí này để lưu dữ liệu hoạt động thật hoặc dữ liệu cần giữ lâu
dài.

**Nền tảng .NET:** dự án hiện dùng .NET 8, đã hết thời hạn hỗ trợ từ
10/11/2026. Chỉ nên dùng cấu hình này cho demo ngắn hạn; cần nâng lên phiên
bản .NET còn được hỗ trợ trước khi sử dụng thực tế
([chính sách hỗ trợ .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)).

**Lưu ý public dữ liệu:** repo chỉ giữ file DB chính
`MamNon.ThucDon.Web/kho_thuc_pham_mam_non.db` làm dữ liệu demo theo xác nhận
của chủ repo; các DB sao lưu, file ZIP, `files/`, `artifacts/`, workbook tổng
hợp và các file Excel mẫu tuần thật bị loại khỏi Git/Docker. Vì vậy demo mới
sẽ chưa có mẫu tuần để xuất cho đến khi bạn nạp một file mẫu đã làm sạch.
Mọi nội dung trong DB chính vẫn công khai trong repo. Hãy kiểm tra và ẩn danh
dữ liệu trước khi public; xóa file ở commit mới không xóa được khỏi lịch sử
Git đã public.

## Bổ sung mới nhất — Chuyển từ MySQL sang SQLite

Theo yêu cầu ("app nhỏ, chỉ cần SQLite") — đã đổi toàn bộ:
- `.csproj`: `Pomelo.EntityFrameworkCore.MySql` → `Microsoft.EntityFrameworkCore.Sqlite`.
- `Program.cs`: `UseMySql(...)` → `UseSqlite(...)` cho cả `ApplicationDbContext` lẫn `ThucDonDbContext`.
- `appsettings.json`: connection string đổi thành `Data Source=kho_thuc_pham_mam_non.db` (1 file, không cần cài server riêng).
- **3 script SQL viết lại hoàn toàn** — cú pháp SQLite khác MySQL khá
  nhiều: không có `AUTO_INCREMENT`/`ENUM`/`ENGINE=InnoDB`, không hỗ trợ
  `UPDATE ... JOIN`, không thêm được `FOREIGN KEY` qua `ALTER TABLE` theo
  kiểu MySQL, không có `CREATE OR REPLACE VIEW`. Đã đối chiếu và thay bằng
  cú pháp tương đương của SQLite (`CHECK` constraint thay `ENUM`, subquery
  tương quan thay `UPDATE...JOIN`, `REFERENCES` ngay trong `ADD COLUMN`
  thay vì `ADD CONSTRAINT` riêng, `DROP VIEW IF EXISTS` + `CREATE VIEW`).

**Đã chạy thử THẬT cả 3 script nối tiếp bằng SQLite thật** (không chỉ đọc
cú pháp bằng mắt) và **bắt được 1 lỗi thật**: script 03 gán `hang_muc_id`
cho `loai_bua_an` bằng cách so `ten_bua = ten_hang_muc` (VD `'CANH' =
'Canh'`) — bản MySQL chạy đúng vì mặc định dùng collation
`utf8mb4_unicode_ci` (không phân biệt hoa/thường), nhưng SQLite mặc định
**phân biệt hoa/thường** và không có collation Unicode-aware cho tiếng Việt
có dấu (`COLLATE NOCASE` chỉ xử lý được A-Z). Kết quả: toàn bộ
`hang_muc_id` bị để `NULL` mà **không báo lỗi gì** — kiểu lỗi âm thầm nguy
hiểm nhất vì trông như chạy thành công. Đã sửa bằng ánh xạ `CASE` tường
minh, không phụ thuộc so khớp chuỗi hoa/thường nữa. Xem chi tiết bằng
comment ngay trong `03_dinh_muc_ty_le_ngan_sach.sql`.

**Chưa đổi / không cần đổi**: `ThucDonDbContext.cs` không cần sửa gì —
`HasConversion` (enum ↔ chuỗi tiếng Việt) và cột tính tự động
(`ValueGeneratedOnAddOrUpdate`) đều hoạt động độc lập với DB engine.

## Lỗi đã bắt được khi rà soát chuyển đổi (chưa build thật được lần nào)

Sandbox này không có `dotnet` SDK — toàn bộ rà soát bằng mắt + grep. Đã tìm
và sửa 3 lỗi biên dịch thật trong lúc viết:
1. `Login.razor` (bản Blazor cũ, nay đã xóa) từng gán nhầm biến `_thongBao`
   không tồn tại — không còn liên quan sau khi chuyển sang MVC.
2. `NguoiDungController.cs`: dùng `Infrastructure.Entities.CoSo` (thiếu
   `using`) — đã sửa thành `using MamNon.ThucDon.Web.Infrastructure.Entities;`
   + tên ngắn `CoSo`.
3. `ThucDonController.cs`: dùng `VaiTro` trong `[Authorize(Roles = ...)]`
   nhưng thiếu `using MamNon.ThucDon.Web.Identity;` — đã thêm.
4. **Lỗi logic** (không phải cú pháp) trong `kho-dong-hang.js`: xóa 1 dòng
   giữa bảng Nhập/Xuất kho mà không đánh số lại `name="CacDong[i]..."` sẽ
   làm index bị đứt quãng — MVC model binder cho `List<T>` chỉ đọc index
   liên tục từ 0, gặp lỗ hổng là dừng luôn, khiến toàn bộ các dòng sau đó
   **biến mất khỏi phiếu khi lưu mà không báo lỗi gì**. Đã sửa bằng hàm
   `danhSoLaiCacDong()` chạy lại sau mỗi lần xóa.

⚠️ **Vẫn chưa build/test thật được lần nào** — việc vừa bắt được 4 lỗi
(3 biên dịch + 1 logic âm thầm) chỉ bằng rà soát tay cho thấy khả năng còn
sót lỗi khác là có thật, đặc biệt ở phần model binding của MVC (vốn dễ lỗi
âm thầm hơn Blazor — sai tên field sẽ không báo lỗi, chỉ lặng lẽ bind ra
giá trị mặc định). **Việc đầu tiên bạn nên làm là `dotnet build` rồi thử
tay từng luồng chính** (soạn thực đơn → lưu → gửi duyệt → duyệt → xuất
Excel; nhập kho/xuất kho với ít nhất 3 dòng rồi xóa dòng giữa để kiểm tra
đúng chỗ lỗi số 4 vừa sửa).

## Bổ sung mới nhất — Quản lý món ăn (tạo công thức cho món mới)

Trả lời đúng câu hỏi "muốn thêm 1 món mới thì làm sao có công thức nguyên
liệu như ảnh chụp thực tế (Bột bán, Bột bắp, Thịt gà...)" — trước đó hệ
thống chỉ có cách DÒ NGƯỢC công thức từ dữ liệu cũ (`Tools/RecipeDatabase/`),
không có cách nhập tay cho món hoàn toàn mới.

- **`/mon-an`** (`MonAnController` + `Views/MonAn/`): danh sách món ăn, số
  nguyên liệu đã khai báo, ẩn/bỏ ẩn.
- **`/mon-an/them-moi`**: tạo món mới. Điểm quan trọng — **nhập theo đúng
  cách nhà bếp nghĩ**: "hôm nay nấu cho bao nhiêu suất" + số lượng từng
  nguyên liệu cho ĐÚNG hôm đó (giống hệt cấu trúc ảnh bạn gửi: Bột bán 7kg,
  Bột bắp 1 gói...), hệ thống tự quy đổi ra **định lượng/1 suất** (chia cho
  số suất tham chiếu) rồi mới lưu — vì đó là đơn vị mà
  `NguyenLieuCalculator`/`HangMucNganSachCalculator` đã dùng từ đầu.
- **`/mon-an/{id}/sua`**: sửa công thức đã có, hiển thị/nhập thẳng định
  lượng/suất (không quy đổi qua suất tham chiếu nữa, vì lúc này số đã ở
  đúng đơn vị cần).
- Sau khi có công thức, món này dùng được ngay ở **Soạn thực đơn**,
  **Dashboard** (kể cả nút "🎲 Chọn ngẫu nhiên") — không cần làm gì thêm,
  vì tất cả đều đọc chung bảng `dinh_luong_nguyen_lieu`.

## Bổ sung mới nhất — Gộp MenuBuilderApi vào chung 1 project

Trước đó có **3 dự án riêng biệt** (xem `TongHopCode.zip` đã gửi ở lượt
trước: hệ thống MVC này, project Swagger `MenuBuilderApi`, và công cụ dựng
cơ sở dữ liệu công thức). Theo yêu cầu, đã **gộp `MenuBuilderApi` thẳng vào
project MVC chính này**:

- **`Controllers/MenuBuilderController.cs`** + **`Views/MenuBuilder/Index.cshtml`**
  — chuyển từ Swagger UI sang 1 trang web bình thường (`/menu-builder`),
  dùng chung đăng nhập/phân quyền với cả hệ thống (không cần mở app riêng).
  Logic bên trong (validate JSON số cháu theo ngày, gọi Python, trả file)
  giữ nguyên y hệt bản Swagger đã sửa/test trước đó.
- **`PythonScript/build_multi_facility_menu.py`** — copy nguyên bản đã sửa
  (3 lỗi đã fix: encoding UTF-8, công thức Excel hỏng khi thiếu dòng gia vị,
  số cháu theo từng ngày) vào project này, cấu hình tự copy ra thư mục build
  qua `.csproj`.
- **`appsettings.json`** — thêm mục `MenuBuilder` (PythonExecutable,
  ScriptPath, TempFolder, TimeoutSeconds). Để trống `ScriptPath` thì code tự
  tìm trong thư mục cài đặt app, không cần khai báo đường dẫn tuyệt đối.
- **`Tools/RecipeDatabase/`** (ở gốc solution, ngoài `MamNon.ThucDon.Web/`)
  — copy `build_recipe_database.py` + `recipes_thang9.json` (dữ liệu đã
  trích xuất từ 3 tuần thật) vào làm tài liệu tham khảo. **Đây KHÔNG được
  gọi tự động bởi bất kỳ Controller nào** — vẫn là script chạy tay riêng,
  vì phần "tự sinh thực đơn mới hoàn toàn từ recipe database" vẫn đang dở
  dang (xem ghi chú ở `TongHopCode.zip`).

**Đổi khác 1 chỗ nhỏ so với bản Swagger gốc**: `PythonExecutable` mặc định
đổi từ `python` sang `python3` cho khớp môi trường Linux — nếu deploy trên
Windows, đổi lại thành đường dẫn `python.exe` thật trong `appsettings.json`.

## Bổ sung sau bản MVC — Dashboard: lưới thực đơn tuần có màu, chỉnh sửa trực tiếp

Theo đúng 2 ảnh mẫu bạn gửi (thực đơn Mẫu giáo tô xanh lá, Nhà trẻ tô hồng),
`/dashboard` giờ là **trang riêng thật sự** (không còn chỉ redirect sang
Soạn thực đơn):

- Chọn Cơ sở + Khối + Tuần (Thứ 2 đầu tuần) → hiện lưới **Thứ 2→Thứ 7 × các
  bữa**, tô màu theo khối:
  - Mẫu giáo: header `#8FAD6E` (xanh lá đậm), cột tên bữa `#E2EFDA`, ô dữ
    liệu `#F3F8F0`.
  - Nhà trẻ: header `#D98A8A` (hồng), cột tên bữa `#F2DCDB`, ô dữ liệu `#FCEFEF`.
  - Đổi theme tự động theo `Model.TenKhoiHienTai` — không cần chọn tay.
- Mỗi ô là 1 dropdown chọn món **ngay trên lưới** (đúng yêu cầu "có thể
  chỉnh sửa menu trên này") — đổi món ở ô nào chỉ ảnh hưởng đúng ô đó,
  không cần vào Soạn thực đơn của từng ngày riêng lẻ.
- 2 nút "Lưu nháp cả tuần" / "Gửi duyệt cả tuần" — gom toàn bộ 6 ngày ×
  N bữa thành 1 lần lưu (`POST /dashboard/luu`), tái dùng đúng logic
  Nháp/Chờ duyệt đã có ở `/soan-thuc-don`.
- **Quan hệ với `/soan-thuc-don`**: 2 trang độc lập nhưng cùng ghi vào đúng
  1 bảng `thuc_don` — sửa ở trang nào cũng phản ánh đúng dữ liệu, không
  trùng lặp hay xung đột.

**Giới hạn**: mã màu (`#8FAD6E`, `#D98A8A`...) là ước lượng bằng mắt từ ảnh
chụp, chưa đối chiếu mã màu chính xác (hex code) từ file Excel gốc — nếu
cần khớp tuyệt đối, gửi file Excel gốc (không phải ảnh chụp) để lấy đúng
mã màu qua `cell.fill`.

## Bổ sung trước đó — Chọn món ngẫu nhiên ở Soạn thực đơn

- Menu có thêm mục **"Dashboard"** (`/dashboard`) — chỉ redirect thẳng sang
  `/soan-thuc-don`, không phải trang riêng, vì tính năng thật (chọn ngẫu
  nhiên) được gắn trực tiếp vào trang Soạn thực đơn để không lặp code.
- Trang Soạn thực đơn có thêm nút **"🎲 Chọn ngẫu nhiên"** — gọi
  `GET /soan-thuc-don/mon-ngau-nhien?khoiId=..` (mới thêm trong
  `ThucDonController`), tự điền món cho tất cả các bữa của khối đang chọn
  (lấy mẫu KHÔNG LẶP trong phạm vi có thể, tránh 2 bữa cùng ngày trùng
  món), rồi tính lại y như khi chọn tay. Người dùng vẫn sửa tay từng ô sau
  đó nếu không ưng — không có gì bị khoá cứng.
- **Giới hạn**: vì `MonAn` trong schema hiện tại không phân loại theo bữa
  (không có cột "phù hợp với Canh/Mặn/Xào..."), nút ngẫu nhiên chọn từ
  TOÀN BỘ danh mục món ăn, có thể ra kết quả không hợp lý về mặt ẩm thực
  (VD món tráng miệng bị gán cho bữa Mặn). Muốn chặt chẽ hơn cần thêm cột
  phân loại món ăn theo bữa vào `mon_an` hoặc `dinh_luong_nguyen_lieu`.

## Giới hạn đã biết (giữ nguyên từ bản Blazor, chưa xử lý thêm)

- `ExcelExportService` khớp Khối ↔ Nhóm chi phí bằng dò tên chuỗi (chưa có
  FK chính thức) — xem chi tiết trong chính file đó.
- Xuất Excel theo từng ngày, chưa gộp cả tuần như file gốc.
- `DinhLuongNguyenLieu` (định lượng nguyên liệu/suất của từng món) chưa có
  dữ liệu thật, cần nhập tay hoặc suy ra từ lịch sử.

## Bổ sung mới nhất — Giữ lựa chọn dang dở khi đổi tab Cơ sở/Ngày (Chi tiết ngày)

Trang `/chi-tiet-ngay`: trước đây, tab Cơ sở (TC/PH/ĐT) và tab Ngày trong tuần
là các link `<a href>` điều hướng cả trang (tải lại từ server) — nên nếu người
dùng vừa đổi vài ô chọn món mà CHƯA bấm "Lưu nháp"/"Gửi duyệt", rồi lỡ tay bấm
sang tab khác, các lựa chọn dang dở đó biến mất, quay lại tab cũ là mất trắng.

**Đã sửa** bằng cách thêm cơ chế lưu tạm (draft) vào `localStorage` của trình
duyệt, trong `wwwroot/js/chi-tiet-ngay.js`:
- Mỗi lần người dùng đổi 1 ô chọn món hoặc sĩ số/tiền ăn, draft của ĐÚNG cặp
  (Cơ sở, Ngày) đang xem được lưu lại (khóa dạng `chiTietNgayDraft:{coSoId}:{ngay}`).
- Khi trang tải lại (do đổi tab Cơ sở/Ngày, hoặc bấm F5), nếu đúng cặp
  (Cơ sở, Ngày) đó có draft, các ô chọn/sĩ số sẽ tự điền lại theo draft đó
  TRƯỚC khi tính toán nguyên liệu lần đầu — nên đổi qua đổi lại giữa các tab
  không còn mất lựa chọn chưa lưu nữa.
- Draft tự hết hạn sau 24 giờ (tránh trường hợp 1 draft cũ ở lại vĩnh viễn và
  che mất dữ liệu thật đã được người khác lưu chính thức sau đó).
- Bấm "Lưu nháp"/"Gửi duyệt" thành công sẽ xóa draft của đúng ngày đó ngay lập
  tức, để lần sau mở lại luôn thấy đúng dữ liệu đã lưu thật trong CSDL.

**Lưu ý**: đây là lưu tạm ở TRÌNH DUYỆT của người dùng đó (localStorage), không
đồng bộ giữa các máy/trình duyệt khác nhau — nếu 2 người cùng mở 1 ngày trên 2
máy khác nhau, mỗi máy có draft riêng, không ai thấy draft của người kia (đúng
như kỳ vọng, vì đây chỉ là "nháp tạm chưa lưu" của riêng từng người).

## Bổ sung mới nhất — Thêm menu ngang cho trang Chi tiết ngày

Trang `/chi-tiet-ngay` dùng `Layout = null` (tự vẽ toàn bộ HTML riêng theo tông
màu ấm, không dùng `_Layout.cshtml` chung có sidebar) — nên trước đó vào trang
này là mất hẳn menu điều hướng, chỉ có mỗi 1 link "Soạn thực đơn".

**Đã thêm** 1 thanh menu ngang (`.topnav`) dính ở đầu trang (sticky), có đủ các
mục y hệt sidebar chính: Trang chủ, Chi tiết thực đơn - Chi tiền chợ (đánh dấu
active), Soạn thực đơn, Nhập/Xuất kho, Xuất Excel, Menu Builder, Quản lý món ăn,
và 2 mục chỉ Hiệu trưởng/Quản trị mới thấy (Duyệt thực đơn, Quản lý người dùng)
— cùng ô "Xin chào, {tên}" + nút Đăng xuất (hoặc nút Đăng nhập nếu chưa đăng
nhập), y hệt logic phân quyền trong `_Layout.cshtml`.

Chỉ sửa 1 file: `Views/ChiTietNgay/Index.cshtml` (thêm CSS `.topnav*` vào
`<style>` sẵn có + thay khối `<div class="topnav">` cũ).

## Bổ sung mới nhất — Menu ngang cho TOÀN site + xóa trang "Soạn thực đơn" dư thừa

**1. Áp menu ngang cho mọi trang.** Trước đó chỉ trang `/chi-tiet-ngay` có menu
ngang riêng (do dùng `Layout = null`, tự vẽ trang); mọi trang khác vẫn dùng
`_Layout.cshtml` với sidebar dọc màu tối cũ. Đã đổi hẳn `_Layout.cshtml` sang
cùng kiểu menu ngang dính đầu trang (`.topnav`/`.topnav-brand`/`.topnav-links`/
`.topnav-account`, cùng tên lớp với bản trong `chi-tiet-ngay`), nên TẤT CẢ các
trang còn lại (Trang chủ, Nhập/Xuất kho, Xuất Excel, Menu Builder, Quản lý món
ăn, Duyệt thực đơn, Quản lý người dùng, Đăng nhập) giờ có menu ngang giống hệt
nhau. `site.css`: xóa `.app-layout/.app-nav*` (sidebar dọc), thay bằng
`.topnav*` (ngang).

**2. Xóa trang "Soạn thực đơn" (`/soan-thuc-don`) vì đã dư thừa.** Trang
`/chi-tiet-ngay` làm được MỌI VIỆC trang này từng làm (chọn khối/ngày/cơ sở,
chọn món theo bữa, Lưu nháp/Gửi duyệt — cùng ghi `TrangThai` y hệt nên tương
thích 100% với `/duyet-thuc-don`), CỘNG THÊM phần "Chi tiền chợ" (gia vị/sáng/
chính+xế) mà trang cũ không có. Đã kiểm tra kỹ không còn chỗ nào khác (kể cả
Menu Builder, Duyệt thực đơn) phụ thuộc vào route hay kiểu dữ liệu riêng của
trang cũ trước khi xóa. Đã xóa: `Controllers/ThucDonController.cs`,
`Views/ThucDon/` (cả thư mục), `ViewModels/ThucDon/SoanThucDonViewModels.cs`,
`wwwroot/js/soan-thuc-don.js`, cùng 2 link menu trỏ tới trang này (ở
`_Layout.cshtml` và menu riêng của `chi-tiet-ngay`). Các service dùng riêng
cho trang cũ (`HangMucNganSachCalculator`, `NganSachNgayCalculator`) CHƯA xóa
(để dành, phòng khi cần dùng lại cho tính năng khác) — chỉ trang/route/JS/
ViewModel dư thừa mới bị xóa.

**Về việc "chọn thực đơn ở 1 trường thì áp dụng cho 2 trường còn lại":** trang
`/chi-tiet-ngay` (action `Luu`) đã làm đúng việc này từ trước — chọn/lưu món ở
mục "1. Thực đơn" khi đang xem BẤT KỲ cơ sở nào cũng tự ghi GIỐNG HỆT xuống cả
3 cơ sở đang active cùng lúc (xem vòng lặp `foreach (var coSo in dsCoSo)` +
`LuuChoKhoiAsync` trong `ChiTietNgayController.Luu`) — không cần sửa gì thêm.

## Bổ sung mới nhất — Đổi nút "Gửi duyệt" thành "Xuất Excel" ở trang Chi tiết ngày

Theo yêu cầu: bỏ nút "Gửi duyệt" ở `/chi-tiet-ngay`, thay bằng nút **"Xuất Excel"**
ngay tại đúng vị trí đó, cạnh nút "Lưu nháp".

- Bấm "Xuất Excel" sẽ tải luôn 2 file (không cần điều hướng sang trang
  `/xuat-excel` riêng): 1 file cho khối Mẫu giáo, 1 file cho khối Nhà trẻ, của
  đúng cơ sở + ngày đang xem trên trang — tận dụng thẳng endpoint có sẵn
  `/xuat-excel/tai?coSoId=...&khoiId=...&ngay=...`.
- Nút "Lưu nháp" giữ nguyên như cũ.
- Trang `/xuat-excel` (menu ngang) vẫn còn nguyên, dùng khi cần chọn cơ sở/khối/
  ngày khác không có sẵn trên `/chi-tiet-ngay`.

**Hệ quả cần biết**: đây là nơi DUY NHẤT từng cho phép đặt `TrangThai =
ChoDuyet` (gửi duyệt) cho thực đơn. Bỏ nút này đi nghĩa là từ giờ sẽ KHÔNG còn
cách nào (qua giao diện) để đưa 1 thực đơn vào trạng thái "chờ duyệt" nữa —
trang `/duyet-thuc-don` (Duyệt thực đơn) vẫn còn đó nhưng sẽ luôn trống, vì
không còn gì được gửi lên để duyệt. Nếu sau này vẫn cần quy trình duyệt, có
2 hướng: thêm lại 1 nút riêng (không thay hẳn) hoặc bỏ luôn trang Duyệt thực
đơn — nói cho mình biết nếu muốn xử lý tiếp hướng nào.

**Xác nhận lại (không phải thay đổi mới)**: việc "chọn thực đơn ở 1 cơ sở áp
dụng cho 2 cơ sở còn lại" đã đúng và hoạt động sẵn từ trước (xem action `Luu`
trong `ChiTietNgayController` — vòng lặp qua mọi cơ sở active) — không cần
sửa gì thêm cho phần này.

## Bổ sung mới nhất — Đồng bộ món ngay khi chọn (không cần đợi Lưu) + Ô chọn món có tìm kiếm

**1. Đồng bộ món sang 2 cơ sở còn lại NGAY khi chọn (chưa cần bấm Lưu).**
Trước đó, việc "món áp dụng cho mọi cơ sở" chỉ đúng ở tầng CSDL (khi bấm "Lưu
nháp"). Nếu người dùng mới chọn món (chưa lưu) rồi đổi tab Cơ sở, tab kia không
thấy gì cả — vì draft tạm (đã làm ở bản trước) lưu riêng theo từng cơ sở, và
CSDL cũng chưa có gì để hiển thị. Nhìn giống như tính năng "áp dụng cho cả 3
cơ sở" không hoạt động, dù phần lưu thật xuống CSDL vẫn đúng.

Đã sửa trong `wwwroot/js/chi-tiet-ngay.js`: mỗi khi đổi 1 ô chọn món, ngoài
việc lưu draft đầy đủ cho cơ sở đang xem, hàm `luuDraftHienTai()` giờ ĐỒNG THỜI
copy riêng phần món (`monMauGiao`/`monNhaTre`) sang draft của các cơ sở khác
(lấy danh sách từ hidden input `dsCoSoIds` mới thêm ở `Index.cshtml`) — KHÔNG
đụng tới sĩ số/tiền ăn của các cơ sở đó (giữ nguyên, vì mỗi cơ sở số cháu khác
nhau). Nhờ vậy đổi tab Cơ sở ngay sau khi chọn món (chưa bấm Lưu) đã thấy đúng
món luôn, đúng như kỳ vọng — không cần đợi lưu xuống CSDL mới thấy.

**2. Ô chọn món giờ gõ tìm kiếm được.** Mỗi `<select class="mon-mau-giao">`/
`"mon-nha-tre">` được bọc thêm 1 ô nhập liệu lọc theo tên (hàm
`enhanceSearchableSelect()`), gõ vài chữ là danh sách món lọc dần theo đúng
tên chứa từ đó — hữu ích khi danh mục món dài, đỡ phải kéo cuộn tìm bằng mắt.
`<select>` gốc vẫn nằm nguyên trong DOM (ẩn đi bằng CSS `display:none`), nên
mọi chỗ khác đọc `.value` của nó (draft, tính toán, lưu xuống CSDL) không cần
sửa gì — chọn 1 dòng trong danh sách lọc chỉ đơn giản set lại `.value` của
`<select>` gốc rồi tự bắn sự kiện `change`, y hệt như người dùng chọn tay trên
`<select>` bình thường.

## Bổ sung mới nhất — Sửa LẠI phân loại "Gia vị" (đối chiếu với 4 file thực đơn thật)

Đối chiếu trực tiếp mục **"2. Theo dõi xuất kho gia vị hằng ngày"** và **"3. Chi
tiền thực phẩm sáng"** trong 4 file `TUẦN__1..4_THÁNG_9_*.xlsx` (sheet "Chợ
TC/PH/ĐT" thật, không phải suy đoán) với dữ liệu đang có trong `hang_hoa`, phát
hiện `loai_hang` bị gán sai trên diện rộng — không phải lỗi vặt mà là gán nhầm
CẢ NGUYÊN TẮC phân loại:

- `05_seed_hang_hoa.sql` gán `'Gia vị'` theo **nhóm sản phẩm trong sheet "Bảng
  giá"** gốc — nhóm này gộp chung 162 mặt hàng, phần lớn là **đồ khô/đồ hộp
  dùng riêng cho từng món** (nui, miến, mì, bột chiên/bột năng, cá hộp, xúc
  xích, tương ớt/cà, ruốc, tôm khô...), không phải gia vị nêm nếm hằng ngày.
- `08_sua_phan_loai_gia_vi.sql` (bản sửa lần 1) gán thêm cả rau thơm/củ gia vị
  **tươi** (hành, tỏi, ớt, gừng, nghệ, ngò, sả, lá chanh, me chua, dầu mè) vào
  `'Gia vị'` — cũng sai, vì đối chiếu dữ liệu thật thì các thứ này luôn nằm ở
  mục "3"/"4" (nguyên liệu riêng của món trong ngày), không phải mục "2".

Hệ quả trực tiếp lên UI: mục "2. Theo dõi xuất kho gia vị hằng ngày" ở trang
Chi tiết ngày bị "phình" ra với đủ loại nguyên liệu món ăn không liên quan
(SAI), trong khi mục "3. Chi tiền thực phẩm sáng" lại bị THIẾU đúng những
nguyên liệu đó (vì bị lọc `!LaGiaVi` loại mất) — đúng như phản ánh.

**Đã sửa** bằng `Database/10_sua_lai_phan_loai_gia_vi_doi_chieu_thuc_te.sql`
(chạy sau `08`, xem chi tiết + đối chiếu số liệu ngay trong comment của file):
reset toàn bộ `'Gia vị'` hiện tại về `'Thực phẩm tươi'`, rồi CHỈ gán lại đúng 7
nhóm đã đối chiếu với 4 file thật — Gạo, Đường trắng (không gồm đường
phèn/vàng/bát), Dầu ăn thường (không gồm dầu mè/hào/gấc), Muối, Nước mắm, Xì
dầu, Bột nêm/Hạt nêm/Bột canh/Mì chính. Kết quả: từ 188 mặt hàng `'Gia vị'`
(sai) còn lại đúng 54 mặt hàng, khớp với thực tế 4 tuần dữ liệu đã cung cấp.
File `kho_thuc_pham_mam_non.db` đính kèm trong lần gửi này đã chạy sẵn script
10 — nếu bạn tự dựng DB từ đầu bằng script SQL thì nhớ chạy đủ thứ tự 01→10 ở
mục "Việc cần làm để chạy lần đầu" phía trên.

## Bổ sung mới nhất — Sửa lỗi gõ tìm món có dấu bị trượt (Bánh/Bún...)

Ô tìm kiếm món (thêm ở bản trước) lọc ra kết quả khi gõ chữ không dấu ("B") nhưng
gõ có dấu ("Bánh", "Bún"...) lại không ra gì, dù nhìn bằng mắt chữ giống hệt
trong danh sách. Nguyên nhân: cùng 1 chữ có dấu tiếng Việt có thể được máy tính
lưu ở 2 dạng Unicode khác nhau — dạng "dựng sẵn" (NFC, 1 ký tự) hoặc dạng "tổ
hợp" (NFD, chữ cái + dấu rời) — trông giống hệt nhau trên màn hình nhưng so
sánh chuỗi bằng `indexOf` lại thấy khác nhau. Một số bàn phím/hệ điều hành (hay
gặp nhất là Mac) gõ ra dạng NFD, trong khi tên món lấy từ CSDL lại ở dạng NFC.

**Đã sửa** trong `wwwroot/js/chi-tiet-ngay.js`: thêm hàm `chuanHoa()` gọi
`.normalize('NFC')` cho CẢ từ khóa gõ vào LẪN tên món trước khi so sánh, đảm
bảo luôn so sánh cùng 1 dạng Unicode bất kể gõ từ máy/bàn phím nào.

## Bổ sung mới nhất — Nút "Xuất Excel" ở Chi tiết ngày ra ĐÚNG bố cục file gốc

Trước đây nút này gọi `/xuat-excel/tai` (2 lần, 1 lần/khối) — ra 2 file, mỗi
file có sheet "Chợ" tự chế kiểu bảng đơn giản (Nguyên liệu/ĐVT/SL/Thành tiền),
khác hẳn bố cục "BẢNG KÊ THỰC ĐƠN - CHI TIỀN CHỢ" thật (đối chiếu trực tiếp
`TUẦN__4_THÁNG_9_D.xlsx`, sheet "Chợ TC" — xem cấu trúc 1 ngày ở đó: tiêu đề
UBND/Trường, mục "1. Thực đơn" + khung ngân sách góc phải, mục "2. Theo dõi
xuất kho gia vị hằng ngày" (3 cột SL/Thành tiền theo Ăn sáng/Nhà trẻ/Mẫu
giáo), mục "3. Chi tiền thực phẩm sáng" (1 cột), mục "4. Chi tiền thực phẩm ăn
chính + xế" (2 cột Nhà trẻ/Mẫu giáo + cột Tổng cộng), cuối cùng là hàng ký tên
Nhà bếp/Thủ kho/Kế toán/Hiệu trưởng).

**Đã sửa:**
- Thêm `ChiTietNgayTinhToanService` — tách phần tính "gộp gia vị/sáng/chính+xế
  từ 3 nguồn KetQuaNguyenLieu thành 3 bảng DTO" ra khỏi
  `ChiTietNgayController.TinhToan` thành 1 service dùng chung, để trang Chi
  tiết ngày (AJAX, số liệu nháp) và file Excel xuất ra (số liệu đã lưu) LUÔN
  cho cùng 1 kết quả — tránh trường hợp 2 nơi tính khác nhau rồi lệch số.
- Thêm `ExcelExportService.XuatBangKeChoNgayAsync(coSoId, ngay)` — sinh ĐÚNG 1
  sheet "Chợ" đúng bố cục trên (font Times New Roman 12, tiêu đề 16pt đậm, viền
  ô, gộp ô tiêu đề nhiều dòng/cột, độ rộng cột), số dòng mục 2/3/4 co giãn theo
  đúng số nguyên liệu thực tế của ngày đó (không cố định 8 dòng như file mẫu
  gõ tay) — cột "Mã hàng/Tỷ lệ ngân sách" tham chiếu ở file gốc (cột K-N,
  thuộc bảng `dinh_muc_ty_le_ngan_sach` riêng) KHÔNG được đưa vào vì không
  liên quan trực tiếp tới việc chi tiền chợ. Hàng ký tên giữ nhãn "Nhà
  bếp/Thủ kho/Kế toán/Hiệu trưởng" nhưng để trống tên (không chép tên người cụ
  thể từ file mẫu vào phần mềm dùng chung).
- Route mới `GET /xuat-excel/bang-ke-cho?coSoId=&ngay=` (1 file duy nhất/ngày,
  thay vì 2 file/khối như cũ).
- `wwwroot/js/chi-tiet-ngay.js`: `chiTietNgayXuatExcel()` gọi thẳng route mới,
  bỏ hẳn cách tải 2 file cách nhau 700ms như trước.

Lưu ý: file xuất ra đọc từ dữ liệu ĐÃ LƯU trong DB (giống nút Lưu/Gửi duyệt) —
nếu vừa đổi món/sĩ số trên trang mà chưa bấm Lưu thì bấm Xuất Excel sẽ chưa có
thay đổi đó, đúng theo hành vi đã có sẵn của nút này.

## Bổ sung mới nhất — Xuất Excel: gộp nhiều sheet (Thực đơn + Chợ TC/PH/ĐT) trong 1 file

Bản trước xuất 1 file chỉ có 1 sheet "Chợ" của đúng cơ sở đang xem trên trang
— thiếu sheet "Thực đơn" và thiếu 2 cơ sở còn lại so với file mẫu gốc (file
mẫu có sẵn cả sheet "Thực đơn tuần ok" + "Chợ TC" + "Chợ PH" + "Chợ ĐT" trong
CÙNG 1 workbook).

**Đã sửa** `ExcelExportService.XuatBangKeChoNgayAsync(DateOnly ngay)` (bỏ
tham số `coSoId` — không cần chọn cơ sở nữa): xuất 1 file duy nhất cho đúng
ngày đó, gồm:
- Sheet **"Thực đơn"**: món của đúng ngày đó (Mẫu giáo + Nhà trẻ) — dùng chung
  cho mọi cơ sở nên chỉ cần 1 sheet (khác file mẫu là cả bảng 1 TUẦN, vì trang
  Chi tiết ngày làm việc theo từng ngày, không theo tuần).
- Sheet **"Chợ {Mã cơ sở}"** — 1 sheet/cơ sở active (TC/PH/ĐT), vì mỗi cơ sở
  có sĩ số + tiền ăn 1 cháu khác nhau nên bảng "Chợ" (mục 2/3/4 + đối chiếu
  ngân sách) khác nhau dù dùng chung 1 thực đơn.

Route `GET /xuat-excel/bang-ke-cho?ngay=yyyy-MM-dd` (bỏ `coSoId`).
`chi-tiet-ngay.js`: `chiTietNgayXuatExcel()` chỉ còn cần chọn ngày, không phụ
thuộc cơ sở đang xem trên dropdown nữa.

**Vì sao số liệu trong ảnh chụp toàn là 0:** cơ sở/ngày đang thử chưa được Lưu
sĩ số + tiền ăn (mục "Số tiền ăn trên 1 cháu"/"Xuất ăn trong ngày" ở trang Chi
tiết ngày) — trang này lưu sĩ số/tiền ăn RIÊNG cho từng cơ sở (khác món ăn là
dùng chung), nên phải vào từng cơ sở, điền sĩ số + tiền ăn, bấm Lưu/Gửi duyệt
thì cả 3 sheet "Chợ TC/PH/ĐT" mới có số khác 0. Nếu đã điền đủ cho cả 3 cơ sở
mà vẫn ra 0, đó mới là lỗi cần báo lại.

## Bổ sung mới nhất — Sheet "Thực đơn": đúng mẫu tuần gốc (Thứ 2 → Thứ 7)

Bản trước sheet "Thực đơn" chỉ liệt kê món của ĐÚNG 1 ngày (bảng 2 cột Bữa/Món
đơn giản) — không giống mẫu gốc là bảng CẢ TUẦN (Thứ 2 → Thứ 7) có viền/merge
tiêu đề/chữ ký riêng.

**Đã sửa** `ExcelExportService.GhiSheetThucDonTuan`: dựng lại đúng bố cục sheet
"Thực đơn tuần ok" gốc — 2 khối xếp dọc trong CÙNG 1 sheet, mỗi khối gồm tiêu
đề UBND/Trường, tên tuần "THỰC ĐƠN {KHỐI} (dd/MM/yyyy - dd/MM/yyyy)", bảng Thứ
2→Thứ 7 × đúng các bữa của khối đó (Mẫu giáo: Sáng/Canh/Mặn/Xào/Tráng
miệng/Xế + dòng "Sữa" phụ không gắn bữa nào trong hệ thống nên luôn để trống;
Nhà trẻ: Sáng/Canh/Mặn/Tráng miệng/Xế/Chính chiều — không có dòng Sữa riêng vì
"Xế" của Nhà trẻ chính là bữa sữa), dòng "Ngũ Hành Sơn, ngày lập..." +
"Người lập"/"Hiệu trưởng" (để trống tên) — cùng font Times New Roman, cỡ chữ,
viền như file mẫu.

- **Tuần lấy dữ liệu** = tuần (Thứ 2 → Thứ 7) CHỨA ngày đang xuất, không phải
  tuần dương lịch cố định.
- Ngày nào trong tuần đã lưu thực đơn (ở bất kỳ cơ sở nào — món dùng chung) thì
  điền đúng món; ngày đánh dấu "Nghỉ lễ"/"Nghỉ khác" (ở `ngay_hoc.trang_thai`)
  hiện chữ "Nghỉ lễ"/"Nghỉ" + ghi chú nếu có; ngày chưa soạn thực đơn để
  TRỐNG (không suy diễn/không tự bịa món).
- File mẫu ghi thêm "TUẦN 4/09/2026" (số thứ tự tuần trong tháng) trước
  khoảng ngày — phần này bỏ qua vì số thứ tự tuần trong tháng không có cách
  tính chắc chắn/không mơ hồ, chỉ giữ lại khoảng ngày (dd/MM/yyyy - dd/MM/yyyy)
  vốn đã đủ để xác định đúng tuần nào.

## Bổ sung mới nhất — Xuất Excel: lấy đúng dữ liệu đang chọn + sheet Chợ đủ cả tuần

Hai chỗ chưa đúng ý:
1. Bấm "Xuất Excel" trước đó chỉ đọc dữ liệu ĐÃ LƯU trong DB — nếu vừa chọn
   món/sĩ số trên màn hình mà chưa bấm "Lưu nháp" thì file xuất ra không có
   thay đổi đó, phải lưu tay trước rồi mới xuất được.
2. Mỗi sheet "Chợ {Mã}" chỉ có 1 khối (đúng 1 ngày) — trong khi file mẫu mỗi
   sheet "Chợ TC/PH/ĐT" có sẵn LẦN LƯỢT 6 khối, mỗi khối 1 ngày trong tuần
   (Thứ 2 → Thứ 7) xếp dọc trong cùng 1 sheet.

**Đã sửa:**
- Nút "Xuất Excel" giờ **tự Lưu nháp** đúng những gì đang hiện trên màn hình
  (món đã chọn + sĩ số/tiền ăn) trước, rồi mới chuyển sang tải file — không
  cần bấm "Lưu nháp" tay trước nữa. Cách làm: gộp thêm 1 field ẩn
  `xuatExcelSauKhiLuu` vào form Lưu có sẵn; `ChiTietNgayController.Luu` sau khi
  lưu xong, nếu field này bật thì `RedirectToAction` thẳng sang
  `XuatExcelController.BangKeCho` thay vì quay lại trang Index. Trạng thái lưu
  vẫn là "Nhập" (nháp) như bấm nút "Lưu nháp", không tự chuyển "Gửi duyệt".
- `ExcelExportService.GhiKhoiChoMotNgay` — tách phần "ghi 1 khối Chợ của 1
  ngày" ra khỏi việc tạo sheet, nhận thêm dòng bắt đầu (`hangBatDau`) và trả về
  dòng bắt đầu cho khối kế tiếp. `XuatBangKeChoNgayAsync` giờ tạo MỖI sheet
  "Chợ {Mã}" đúng 1 lần, rồi gọi hàm này LẦN LƯỢT cho cả 6 ngày trong tuần
  (Thứ 2 → Thứ 7 chứa ngày đang xuất — cùng tuần với sheet "Thực đơn"), xếp
  chồng các khối trong cùng 1 sheet, đúng cấu trúc file mẫu.

Vì mỗi sheet giờ luôn xuất đủ cả tuần bất kể đang bấm nút từ ngày nào trong
tuần đó, nên chỉ cần Lưu/Xuất Excel 1 lần ở BẤT KỲ ngày nào trong tuần là đủ
kéo ra đủ 6 khối — miễn những ngày còn lại trong tuần cũng đã được lưu từ
trước (ngày chưa lưu vẫn hiện trống, không suy diễn).

## Bổ sung mới nhất — Tách bữa Sáng riêng cho Nhà trẻ (không dùng chung với Mẫu giáo)

Thực tế Nhà trẻ có món ăn sáng RIÊNG, không ăn chung món với Mẫu giáo như phần
mềm giả định trước đó (trang Chi tiết ngày trước đây ẩn hẳn ô chọn món "SÁNG"
của Nhà trẻ, kèm ghi chú "dùng chung với Mẫu giáo ở trên").

Hạ tầng lưu món (bảng `thuc_don`, khóa theo `khoi_id + loai_bua_an_id`) vốn đã
sẵn sàng cho việc này — `loai_bua_an` đã có sẵn đúng 1 dòng "SÁNG" riêng cho
MỖI khối từ đầu (xem `04_nhom_bua_an.sql`). Cái thiếu là (1) giao diện đang ẩn
ô chọn của Nhà trẻ, và (2) sĩ số/tiền ăn sáng chỉ có 1 nhóm chi phí "Ăn sáng"
DÙNG CHUNG — không tách được khi 2 khối ăn 2 món khác nhau, số cháu ăn sáng
mỗi khối cũng khác nhau.

**Đã sửa:**
- `Database/11_tach_an_sang_nha_tre.sql` — thêm nhóm chi phí mới **"Ăn sáng -
  Nhà trẻ"**. Nhóm "Ăn sáng" cũ GIỮ NGUYÊN tên/ý nghĩa (nay hiểu là sĩ số/tiền
  ăn sáng của Mẫu giáo) để không phải sửa lại dữ liệu ngày đã lưu trước đây.
- `Views/ChiTietNgay/Index.cshtml`: bỏ hẳn việc ẩn ô "SÁNG" của Nhà trẻ (giờ
  hiện đầy đủ như mọi bữa khác) + thêm cặp ô "Ăn sáng — Nhà trẻ (suất)" /
  "Tiền/cháu — Sáng (Nhà trẻ)" cạnh cặp ô Mẫu giáo tương ứng.
- `ChiTietNgayController.TinhToan` / `ExcelExportService`: nguyên liệu mục
  "2. Gia vị" và "3. Sáng" vẫn ra ĐÚNG 1 cột "Ăn sáng" duy nhất như cũ (không
  đổi hình dạng bảng) — nhưng giờ gộp từ CẢ 2 nguồn (món Sáng của Mẫu giáo ×
  sĩ số ăn sáng Mẫu giáo, VÀ món Sáng của Nhà trẻ × sĩ số ăn sáng Nhà trẻ)
  trong CÙNG 1 lần gọi `NguyenLieuCalculator.TinhNhuCauNguyenLieu` — hàm này
  vốn đã tự cộng dồn đúng theo từng hàng hoá nên không cần sửa gì thêm ở đó.
- Bảng ngân sách mini (mục 1 Excel + panel "Sĩ số & tiền ăn"): cột "Ăn sáng"
  — dòng "Xuất ăn"/"Được phép chi" cộng gộp cả 2 khối (cộng số suất/cộng tiền
  là hợp lệ); riêng dòng "Số tiền ăn trên 1 cháu" là ĐƠN GIÁ nên KHÔNG cộng
  được — chỉ hiện số khi 2 khối cùng 1 mức tiền, khác nhau thì để trống thay
  vì hiện 1 số sai. Dòng "Sáng :" ở mục 1 hiện tên món của Mẫu giáo và Nhà trẻ
  cạnh nhau (dạng "MG: ... / NT: ...") khi 2 món khác nhau.
- `wwwroot/js/chi-tiet-ngay.js`: đồng bộ đủ field mới vào payload
  `/tinh-toan`, draft nháp (localStorage), và form Lưu.

Nếu chưa chạy `Database/11_tach_an_sang_nha_tre.sql`, sĩ số/tiền ăn sáng của
Nhà trẻ sẽ không lưu được (bị bỏ qua lặng lẽ vì `LuuDinhMucAsync` không tìm
thấy nhóm chi phí tương ứng) — nhớ chạy đủ theo đúng thứ tự ở mục "Việc cần
làm để chạy lần đầu".

## Bổ sung mới nhất — Sửa lỗi build + hoàn thiện việc đổi mục 2 sang dữ liệu thật

Bản trước báo lỗi build (`CS0117: 'ChiTietNgayTinhToanResponse' does not
contain a definition for 'GiaVi'...`) vì DTO đã được cắt bớt (mục 2 chuyển
sang đọc phiếu xuất kho thật) nhưng `Services/ChiTietNgayTinhToanService.cs`
— vốn cũng ĐANG ĐƯỢC DÙNG THẬT bởi cả `ChiTietNgayController` LẪN
`ExcelExportService` (đăng ký DI ở `Program.cs`) — chưa được cập nhật theo.

**Đã sửa đồng bộ ở cả 2 nơi dùng chung:**
- `ChiTietNgayTinhToanService.Tinh(...)`: bỏ hẳn phần tính "Gia vị" tự động,
  chỉ còn tính mục 3 (Sáng) + mục 4 (Chính+xế) như trước.
- Thêm hàm mới **`ChiTietNgayTinhToanService.LayGiaViThatAsync(coSoId, ngay)`**
  — đọc mục 2 THẲNG từ phiếu xuất kho thật (loại "Gia vị hằng ngày", lập ở
  `/kho/xuat-kho`), pivot theo 3 cột hiển thị (Ăn sáng/Nhà trẻ/Mẫu giáo). Cột
  "Ăn sáng" gộp CẢ 2 nhóm chi phí "Ăn sáng" (Mẫu giáo) và "Ăn sáng - Nhà trẻ"
  làm 1 — đúng quy ước chỉ có 1 cột "ăn sáng" duy nhất trên giao diện/Excel dù
  giờ 2 khối có sĩ số ăn sáng khác nhau (xem `11_tach_an_sang_nha_tre.sql`).
  Hàm này giờ là **nguồn dữ liệu gia vị DUY NHẤT** cho toàn hệ thống — cả
  trang web lẫn Excel đều gọi đúng 1 chỗ này, tránh lặp lại đúng loại lỗi
  "2 nơi tính khác nhau rồi lệch nhau" đã từng gặp.
- `ChiTietNgayController.Index`: gọi `LayGiaViThatAsync` để đổ vào
  `ChiTietNgayViewModel.GiaViThat`/`CoPhieuGiaVi`, hiển thị TĨNH (không qua
  AJAX) ở mục 2 trên trang.
- `ExcelExportService.XuatBangKeChoNgayAsync`: trong vòng lặp xuất từng
  ngày/cơ sở, gọi THÊM `LayGiaViThatAsync` (song song với `Tinh(...)` vốn có
  sẵn cho mục 3/4), truyền cả 2 kết quả vào `GhiKhoiChoMotNgay` — sheet
  "Chợ ..." trong file Excel xuất ra giờ có mục 2 đúng số liệu thật, khớp
  100% với những gì đang hiển thị trên trang web.
- `wwwroot/js/chi-tiet-ngay.js`: bỏ `renderGiaVi()`; `guiTinhToan()` giờ lấy
  3 số tổng tiền gia vị đã render sẵn từ server (qua 3 hidden input mới) rồi
  cộng với tổng tiền Sáng/Chính+xế từ AJAX.

**Việc bạn cần làm:** với mỗi ngày muốn xem trên `/chi-tiet-ngay`, phải lập
phiếu "Gia vị hằng ngày" ở `/kho/xuat-kho` trước — mục 2 chỉ hiển thị đúng
những gì đã nhập tay, không tự tính từ công thức món nữa (xem lý do đầy đủ ở
mục "Mục 2 'Gia vị hằng ngày' đổi lại về nhập tay" phía trên, nếu bản README
này đã có sẵn mục đó từ trước).

## Bổ sung mới nhất — Sửa lỗi runtime "no such column: c.phieu_id"

Sau khi sửa lỗi build, chạy lên gặp tiếp lỗi runtime khi mở `/chi-tiet-ngay`:
`SQLite Error 1: 'no such column: c.phieu_id'` ngay tại
`ChiTietNgayTinhToanService.LayGiaViThatAsync` (dòng gọi
`_db.PhieuXuatKhos...Include(p => p.ChiTiets)`).

Đây là lỗi mình đã từng gặp và sửa ở một nhánh project khác trước đó, nhưng
nhánh này (`ThucDonDbContext.cs`) chưa có bản vá đó. Nguyên nhân: quan hệ
`ChiTietNhapKho.Phieu` / `ChiTietXuatKho.Phieu` chưa được khai báo tường minh
trong `OnModelCreating`. EF Core tự đoán khóa ngoại theo quy ước tên
`"<TênNavigation>Id"` → tìm property tên `"PhieuId"`, không khớp với property
thật (`PhieuNhapId`/`PhieuXuatId`) → EF tự sinh thêm 1 **shadow property
"PhieuId"** (map ra cột `phieu_id` không tồn tại trong DB thật) thay vì dùng
đúng cột `phieu_nhap_id`/`phieu_xuat_id` đã có sẵn.

**Đã sửa** bằng 2 dòng khai báo tường minh trong `ThucDonDbContext.OnModelCreating`:
```csharp
b.Entity<ChiTietNhapKho>().HasOne(x => x.Phieu).WithMany(x => x.ChiTiets)
    .HasForeignKey(x => x.PhieuNhapId).OnDelete(DeleteBehavior.Restrict);
b.Entity<ChiTietXuatKho>().HasOne(x => x.Phieu).WithMany(x => x.ChiTiets)
    .HasForeignKey(x => x.PhieuXuatId).OnDelete(DeleteBehavior.Restrict);
```
Đã rà lại toàn bộ 23 property khóa ngoại (`*Id`) trong solution — đây là 2
trường hợp DUY NHẤT có tên property lệch quy ước `"<TênNavigation>Id"`, các
quan hệ còn lại đều khớp quy ước nên không bị ảnh hưởng.

## Bổ sung mới nhất — Mục 3/4 khi xuất Excel: đổi sang đọc phiếu xuất kho thật (giống mục 2)

Sau khi mục 2 ("Gia vị hằng ngày") đã đổi sang đọc phiếu xuất kho thật (xem
mục README phía trên), mục 3 ("Chi tiền thực phẩm sáng") và mục 4 ("Chi tiền
thực phẩm ăn chính + xế") ở **file Excel xuất ra** vẫn còn tính theo kiểu CŨ —
`ExcelExportService` vẫn gọi `NguyenLieuCalculator.TinhNhuCauNguyenLieu(...)`
rồi `ChiTietNgayTinhToanService.Tinh(...)` (suy ra từ công thức món ăn đã
chọn), trong khi trang `/cho/chi-tiet` (`ChoController`) đã đọc THẲNG từ phiếu
xuất kho thật (loại "Thực phẩm sáng"/"Thực phẩm chính + xế") từ trước — 2 nơi
lệch nhau, nên Excel xuất ra không khớp với thực tế/Excel gốc.

Rà lại còn phát hiện thêm NGUYÊN NHÂN GỐC vì sao công thức món ăn không đáng
tin cậy để dùng làm số liệu chính thức: bảng `dinh_luong_nguyen_lieu` có lỗi
trích xuất — 20 dòng gắn NHẦM "Sữa Ellac Multi"/"Sữa chua..." làm nguyên liệu
của 19 món "Canh...", "Cá nấu...", "Nấm nấu...", "Tôm nấu...", "Bánh mì bò
kho" (cùng 1 con số 0.0125 giống hệt nhau tuyệt đối trên diện rộng — dấu hiệu
lỗi công cụ trích xuất, không phải công thức thật). Đã có sẵn
`Database/11_xoa_gan_nham_sua_vao_mon_canh.sql` xử lý đúng lỗi này (viết từ
trước nhưng **CHƯA được chạy vào `kho_thuc_pham_mam_non.db`** — đã chạy vào
bản đính kèm lần này).

**Đã sửa:**
- `Database/11_xoa_gan_nham_sua_vao_mon_canh.sql` — đã chạy vào DB đính kèm
  (xoá 20 dòng gắn nhầm sữa/sữa chua vào 19 món Canh/Cá nấu/Nấm nấu/Tôm
  nấu/Bánh mì bò kho).
- Thêm **`ChiTietNgayTinhToanService.LayThucPhamThatAsync(coSoId, ngay)`** —
  đọc mục 3 + mục 4 THẲNG từ phiếu xuất kho thật (loại "Thực phẩm sáng" /
  "Thực phẩm chính + xế"), y hệt cách `ChoController.XayThucPhamSang`/
  `XayChinhXe` đang làm — giờ là nguồn dữ liệu DUY NHẤT cho mục 3/4 ở cả
  Excel lẫn trang `/cho/chi-tiet`, tránh lặp lại lỗi "2 nơi tính khác nhau".
- `ExcelExportService.XuatBangKeChoNgayAsync`: bỏ hẳn việc gọi
  `NguyenLieuCalculator`/`ChiTietNgayTinhToanService.Tinh(...)` trong vòng lặp
  xuất "Chợ {Mã cơ sở}" — thay bằng `LayThucPhamThatAsync`, dựng `kq` từ kết
  quả đó. Không còn cần `Include(... DinhLuongNguyenLieu ...)` khi tải
  `ThucDon` nữa (chỉ còn dùng để hiện tên món ở mục "1. Thực đơn").
- `ChiTietNgayTinhToanService.Tinh(...)` (tính từ công thức món) VẪN GIỮ
  NGUYÊN — vẫn là cách trang "Chi tiết ngày" ước lượng/xem thử khi đang CHỌN
  món (trước khi có phiếu xuất kho thật cho ngày đó), không xoá bỏ.

**Việc bạn cần làm:** để mục 3/4 trong Excel/`/cho/chi-tiet` ra đúng số, phải
lập đủ 2 phiếu "Thực phẩm sáng" + "Thực phẩm chính + xế" ở `/kho/xuat-kho` cho
đúng (cơ sở, ngày) muốn xuất — ngày/mục nào chưa lập phiếu sẽ ra TRỐNG (không
tự lấy tạm số ước lượng từ công thức món ăn nữa, giống hệt mục 2 đã đổi trước
đó). `Database/10_dinh_muc_gia_vi_hang_ngay.sql` (định mức gia vị cố định
theo suất, hướng tiếp cận được thử TRƯỚC KHI chọn phiếu xuất kho thật) đã bị
thay thế hoàn toàn bởi hướng phiếu xuất kho — file này còn trong thư mục
`Database/` nhưng KHÔNG được chạy và không còn được dùng, có thể bỏ qua/xoá.

## Bổ sung mới nhất — Gia vị hằng ngày: định mức CỐ ĐỊNH cho mọi ngày (không cần lập phiếu mỗi ngày)

Yêu cầu: "gia vị hằng ngày cho cố định tất cả các ngày" — tức KHÔNG muốn phải vào
`/kho/xuat-kho` lập phiếu "Gia vị hằng ngày" bằng tay mỗi ngày (vì các mặt hàng
và định lượng gần như không đổi ngày này qua ngày khác, chỉ có sĩ số ăn là
khác nhau).

**Đã thêm:**
- Bảng mới `dinh_muc_gia_vi_chuan` (`Database/12_dinh_muc_gia_vi_chuan.sql`) —
  1 dòng = định lượng/suất CỐ ĐỊNH của 1 hàng hoá cho 1 trong 4 nhóm chi phí
  (Ăn sáng / Ăn sáng - Nhà trẻ / Nhà trẻ / Mẫu giáo). Seed sẵn 13 mặt hàng với
  số liệu lấy lại từ `Database/10_dinh_muc_gia_vi_hang_ngay.sql` (định mức
  trung bình gộp qua 72 ngày quan sát được trước đây) — kèm bổ sung 2 mặt hàng
  còn thiếu trong `hang_hoa` ("Gạo cỏ may", "Muối iốt 500g...").
- Trang quản lý mới **`/kho/dinh-muc-gia-vi`** (menu "Định mức gia vị") — bảng
  cho sửa trực tiếp định lượng/suất của từng hàng hoá × 4 nhóm, lưu 1 lần cho
  mọi ngày sau này.
- `ChiTietNgayTinhToanService.LayGiaViThatAsync` — đổi thứ tự ưu tiên: (1) ĐÃ
  có phiếu xuất kho thật cho đúng ngày → dùng phiếu thật (cho phép 1 ngày cụ
  thể lệch định mức khi thực tế khác); (2) CHƯA có phiếu → TỰ ĐỘNG tính = định
  mức cố định × sĩ số ăn ngày đó (`dinh_muc_an_ngay`) — không cần lập gì thêm.
  Áp dụng cho CẢ Excel lẫn trang `/cho/chi-tiet` (đã sửa `ChoController` dùng
  chung đúng 1 chỗ tính này, bỏ hàm `XayGiaVi` cũ chỉ đọc phiếu thật).
- Trang `/cho/chi-tiet`: khi mục 2 đang hiện số tự động (chưa có phiếu thật),
  hiện ghi chú nhỏ "số tự tính theo định mức cố định" kèm link sang trang sửa
  định mức.

**Lưu ý quan trọng khi dùng:** vì số liệu gốc seed là TRUNG BÌNH gộp nhiều
tuần đổi brand khác nhau, có 3 loại Gạo + 2 loại Muối + 2 loại Xì dầu cùng có
định mức > 0 cùng lúc — vào `/kho/dinh-muc-gia-vi` **xóa/đặt về 0** các brand
không còn dùng, chỉ giữ đúng 1 loại đang dùng thật cho mỗi loại gia vị, để mục
2 không bị cộng trùng nhiều brand cùng lúc. Cột "Ăn sáng - Nhà trẻ" seed tạm
BẰNG cột "Ăn sáng" (Mẫu giáo) vì số liệu quan sát gốc có trước khi tách 2
nhóm này — cũng cần rà lại cho khớp thực tế Nhà trẻ.

## Bổ sung mới nhất — Sửa lỗi "đổi suất ăn không thay đổi định lượng/số lượng" ở /cho/chi-tiet

Nguyên nhân: trang "/cho/chi-tiet" có ô "xem thử" đổi số suất ăn — cách tính
cũ luôn quy đổi theo TỈ LỆ `suất hiện tại / suất gốc đã lưu`. Nếu ngày đó
CHƯA từng lưu sĩ số gốc (`dinh_muc_an_ngay` chưa có dòng nào, tức "gốc" = 0),
code cũ ép tỉ lệ về 0 (tránh chia cho 0) — nghĩa là bất kể gõ số suất "xem
thử" bao nhiêu, mọi số lượng/thành tiền đều bị nhân với 0, nhìn như đổi ô
"suất ăn" không có tác dụng gì.

**Đã sửa:**
- Mục 2 (đang tự tính từ định mức cố định — xem mục trên): đổi
  `ChiTietNgayTinhToanService.LayGiaViThatAsync` nhận thêm 3 tham số tùy chọn
  `anSangHienTai/nhaTreHienTai/mauGiaoHienTai` — nhánh "chưa có phiếu thật"
  giờ NHÂN THẲNG định mức với đúng sĩ số đang xem thử (không cần biết "sĩ số
  gốc" là bao nhiêu, không đi qua bước quy tỉ lệ nữa). `ChoController` truyền
  thẳng `vm.AnSangHienTai/NhaTreHienTai/MauGiaoHienTai` vào đây, và
  `ApDungTyLe` KHÔNG co giãn thêm lần nữa cho mục 2 khi đang ở nhánh tự động
  (tránh nhân đúp sai số). Excel (`ExcelExportService`) gọi hàm này KHÔNG
  truyền override (giữ nguyên hành vi: luôn dùng đúng sĩ số đã lưu, vì báo cáo
  chính thức không nên phụ thuộc vào giá trị "xem thử" tạm thời trên trang).
- Mục 3/4 (đọc từ phiếu xuất kho thật — không đổi cách tính) vẫn dùng cơ chế
  quy tỉ lệ cũ, nhưng đổi tỉ lệ mặc định khi thiếu sĩ số gốc từ **0** thành
  **1** (giữ nguyên số liệu thật, không co giãn) — trước đây lỡ chưa lưu sĩ số
  gốc thì cả mục 3/4 cũng bị nhân về 0 hết, che mất số liệu thật đã ghi sổ
  trong phiếu; giờ ít nhất vẫn hiện đúng số thật, chỉ là ô "xem thử" chưa có
  tác dụng cho tới khi có sĩ số gốc thật để quy đổi tỉ lệ (hạn chế còn lại của
  cơ chế tỉ lệ này, chưa xử lý triệt để như mục 2).

## Bổ sung mới nhất — Sửa lỗi "no such table: DinhMucGiaViChuans"

Mở `/kho/dinh-muc-gia-vi` báo lỗi `SQLite Error 1: 'no such table:
DinhMucGiaViChuans'`. Nguyên nhân: khi thêm entity `DinhMucGiaViChuan` (bảng
"định mức gia vị chuẩn", cố định cho mọi ngày), người viết quên khai báo
`.ToTable("dinh_muc_gia_vi_chuan")` như MỌI entity khác trong
`ThucDonDbContext` đều có — EF Core mặc định lấy luôn tên property DbSet
(`DinhMucGiaViChuans`, số nhiều, PascalCase) làm tên bảng, trong khi bảng thật
tạo bằng `12_dinh_muc_gia_vi_chuan.sql` tên là `dinh_muc_gia_vi_chuan` (số ít,
snake_case) — 2 tên lệch hẳn nhau nên EF báo "không tìm thấy bảng".

**Phát hiện thêm 1 lỗi cùng loại lỗi `TienAn1Chau` đã gặp trước đây**: cột
`DinhLuong1Suat` cũng bị bộ tự-chuyển-snake_case cho ra `dinh_luong1_suat`
(thiếu `_` trước số 1), trong khi cột thật là `dinh_luong_1_suat`.

**Đã sửa cả 2** trong `ThucDonDbContext.cs`:
```csharp
b.Entity<DinhMucGiaViChuan>().ToTable("dinh_muc_gia_vi_chuan");
b.Entity<DinhMucGiaViChuan>().Property(x => x.DinhLuong1Suat).HasColumnName("dinh_luong_1_suat");
```
(cùng dòng loại trừ `DinhLuong1Suat` khỏi vòng lặp tự-chuyển bên dưới, y hệt
cách đã làm với `TienAn1Chau`).

Đã rà lại TOÀN BỘ `DbSet<...>` trong `ThucDonDbContext` so với danh sách
`.ToTable(...)` — xác nhận `DinhMucGiaViChuan` là entity DUY NHẤT bị thiếu,
không còn sót entity nào khác. File `kho_thuc_pham_mam_non.db` đính kèm đã có
sẵn bảng `dinh_muc_gia_vi_chuan` với 52 dòng dữ liệu định mức — chỉ là code
C# trỏ sai tên bảng, không cần sửa gì ở phía CSDL.

## Bổ sung mới nhất — Mục 2 "Gia vị hằng ngày": luôn hiện đủ danh sách cố định + sửa tạm thời ngay tại trang Chi tiết ngày

Yêu cầu: "những gia vị trên file excel luôn cố định — ngày nào cũng có —
nhưng trên trang Chi tiết ngày có thể chỉnh sửa được tạm thời".

**Đã sửa:**
- `ChiTietNgayTinhToanService.LayGiaViThatAsync`: bỏ điều kiện lọc bớt dòng có
  số lượng = 0 — giờ LUÔN trả về ĐỦ danh sách hàng hoá đã cấu hình ở
  `/kho/dinh-muc-gia-vi` (kể cả dòng đang = 0), để Excel/`/cho/chi-tiet`/trang
  Chi tiết ngày ngày nào cũng hiện cùng 1 danh sách cố định, không bị thiếu
  dòng ngẫu nhiên theo từng ngày.
- `NguyenLieuBaCotDto` (DTO mục 2) có thêm `HangHoaId` — cần để biết đang sửa
  đúng hàng hoá nào khi lưu lại.
- Trang **Chi tiết ngày**, mục "2. Theo dõi xuất kho gia vị hằng ngày": 3 cột
  số lượng (SL Sáng/N.Trẻ/M.Giáo) giờ là Ô NHẬP SỐ sửa được trực tiếp (trước
  đây chỉ hiện chữ, không sửa được, và còn bị ẩn hẳn khi chưa có phiếu thật).
  Nút **"Lưu gia vị (cho riêng ngày này)"** mới — action
  `ChiTietNgayController.LuuGiaVi` — lập/GHI ĐÈ 1 phiếu xuất kho loại "Gia vị
  hằng ngày" cho ĐÚNG (cơ sở, ngày) đang xem, y hệt phiếu lập ở
  `/kho/xuat-kho` nhưng tiện sửa ngay tại chỗ, không cần qua trang khác.
  **"Tạm thời"** đúng nghĩa: chỉ ảnh hưởng ĐÚNG 1 ngày đó (ghi vào phiếu của
  riêng ngày đó), KHÔNG đụng tới định mức cố định chung ở
  `/kho/dinh-muc-gia-vi` — định mức chung vẫn áp dụng y nguyên cho mọi ngày
  khác chưa từng sửa tay kiểu này.
- Dùng `<form>` POST thường (có `@Html.AntiForgeryToken()`), không dùng
  fetch/AJAX — nhất quán với toàn bộ các trang Kho/Chi tiết ngày khác trong
  codebase, tránh phải tự xử lý CSRF token cho JS.

Lưu ý: nếu đang sửa sĩ số ăn (ô "Ăn sáng/Nhà trẻ/Mẫu giáo (suất)") ở mục bên
cạnh thì bảng gia vị mục 2 KHÔNG tự tính lại theo sĩ số mới (để tránh ghi đè
mất những gì đang sửa tay dở dang trong bảng) — muốn thấy số gia vị tự động
theo sĩ số mới thì bấm "Lưu nháp"/tải lại trang trước, rồi mới sửa gia vị.

## Bổ sung mới nhất — Mục 2: hiện chữ bình thường + mỗi dòng có nút Sửa/Xóa riêng

Bản trước làm CẢ BẢNG thành ô nhập số (sửa được) kèm 1 nút "Lưu gia vị" duy
nhất cho toàn bảng. Đổi theo yêu cầu: hiện bình thường (chữ, không phải ô
nhập), mỗi DÒNG có riêng 2 nút "Sửa" và "Xóa".

**Đã sửa:**
- `ChiTietNgayTinhToanService.LayGiaViThatAsync`: đổi từ "chọn 1 trong 2 nguồn
  (phiếu HOẶC định mức)" sang **GHÉP theo từng hàng hoá** — định mức tự động
  làm nền cho TOÀN BỘ danh sách cố định, phiếu xuất kho (nếu có) chỉ GHI ĐÈ
  đúng những hàng hoá đã từng được chỉnh tay, hàng hoá nào chưa đụng tới vẫn
  giữ nguyên số tự động. Nhờ vậy sửa/xóa 1 dòng không bao giờ làm mất các
  dòng khác, và danh sách luôn đủ/cố định dù phiếu đang trống hay đầy đủ.
- Trang Chi tiết ngày, mục 2: quay lại hiện TEXT bình thường (không phải ô
  nhập) ở dòng chính; mỗi dòng có:
  - Nút **"Sửa"** — bật ra 1 dòng phụ ngay bên dưới với 3 ô nhập (SL
    Sáng/N.Trẻ/M.Giáo) + nút "Lưu"/"Hủy" (chỉ là JS ẩn/hiện, chưa gửi gì lên
    server cho tới khi bấm "Lưu"). Nút "Lưu" gọi action mới
    `ChiTietNgayController.LuuMotDongGiaVi` — chỉ ghi đè ĐÚNG hàng hoá dòng
    đó vào phiếu của ngày này, các hàng hoá khác trong phiếu giữ nguyên.
  - Nút **"Xóa"** (có hỏi xác nhận) — gọi action mới `XoaDongGiaVi`, ghi 1
    dòng = 0 cho đúng hàng hoá đó vào phiếu ngày này (KHÔNG xoá hẳn khỏi bảng
    — mục 2 vẫn luôn hiện đủ danh sách cố định, chỉ là dòng đó về 0; cũng
    KHÔNG đụng định mức chung nên các ngày khác chưa xóa vẫn bình thường).
- Bỏ action `LuuGiaVi` (lưu cả bảng 1 lần) — thay hẳn bằng 2 action theo
  từng dòng ở trên, dùng chung 1 hàm private `LuuMotHangHoaVaoPhieuGiaViAsync`
  (tạo phiếu nếu chưa có, chỉ xoá+ghi lại đúng 3 dòng Ăn sáng/Nhà trẻ/Mẫu
  giáo của ĐÚNG hàng hoá đang sửa/xóa).
- Vẫn dùng `<form>` POST thường (có token chống CSRF), không AJAX — mỗi dòng
  "Sửa"/"Xóa" có `<form>` riêng, submit xong tải lại trang như các action
  Lưu/Xuất Excel khác trong codebase.
