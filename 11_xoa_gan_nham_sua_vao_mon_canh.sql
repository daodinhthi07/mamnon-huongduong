-- =============================================================================
-- 11_xoa_gan_nham_sua_vao_mon_canh.sql
--
-- Phát hiện khi rà soát vì sao "mục 2. Gia vị hằng ngày" không khớp Excel
-- (xem trao đổi ngày liên quan trong lịch sử dự án): khi kiểm tra định lượng
-- nguyên liệu của món "Cá nấu thơm, cà, giá" phát hiện bị gắn NHẦM "Sữa Ellac
-- Multi" làm nguyên liệu — vô lý vì sữa là món uống/tráng miệng riêng trong
-- ngày, không phải gia vị nấu cá.
--
-- Rà soát rộng ra cả bảng dinh_luong_nguyen_lieu thì phát hiện đây là lỗi HỆ
-- THỐNG, không phải 1 dòng lẻ: CÓ TỚI 19 món — hầu hết là các món "Canh ..."
-- (Canh bí đỏ, Canh bầu, Canh cải ngọt, Canh rau cải, Canh rau mồng tơi,
-- Canh rau tập tàng, Canh thịt bò đậu hũ, Canh xương hầm táo đỏ, Canh khoai
-- mỡ, Canh củ cải cà rốt, Cá nấu canh chua, Cá nấu thơm cà giá, Nấm nấu canh
-- chua, Tôm nấu khoai mỡ, Bánh mì bò kho...) — ĐỀU bị gắn "Sữa Ellac Multi"
-- với CÙNG 1 con số 0.0125 kg/suất giống hệt nhau tuyệt đối, và một số món
-- còn bị gắn thêm "Sữa chua uống lên men yakult" / "Sữa chua lên men tự
-- nhiên". Con số giống hệt nhau tuyệt đối trên diện rộng như vậy gần như
-- chắc chắn là lỗi của công cụ trích xuất công thức từ Excel (Tools/
-- RecipeDatabase) — có thể do dòng "Sữa Ellac Multi" trong bảng Excel gốc
-- (vốn là 1 dòng thực đơn RIÊNG trong ngày, đứng gần các món Canh) bị công cụ
-- hiểu nhầm là nguyên liệu của món Canh đứng gần đó.
--
-- Ảnh hưởng thực tế: những dòng sai này khiến mục "4. Chi tiền thực phẩm ăn
-- chính + xế" trên trang /chi-tiet-ngay hiện thêm dòng "Sữa Ellac Multi"/
-- "Sữa chua..." vào bất kỳ ngày nào có chọn 1 trong 19 món Canh nói trên —
-- sai hoàn toàn so với thực tế.
--
-- Xóa toàn bộ các dòng dinh_luong_nguyen_lieu ghép (mon_an có tên bắt đầu
-- "Canh"/"Cá nấu"/"Nấm nấu"/"Tôm nấu"/"Bánh mì bò kho") x (hàng hóa là 1
-- trong 3 loại sữa/sữa chua kể trên). KHÔNG đụng tới các dòng dinh_luong khác
-- của chính các món này (rau, thịt, xương... vẫn giữ nguyên) — chỉ xóa đúng
-- 3 mặt hàng sữa bị gán nhầm.
-- =============================================================================

DELETE FROM dinh_luong_nguyen_lieu
WHERE hang_hoa_id IN (
    SELECT id FROM hang_hoa
    WHERE ten_hang IN ('Sữa Ellac Multi', 'Sữa chua uống lên men yakult (50 chai/thùng)', 'Sữa chua lên men tự nhiên 50GR')
)
AND mon_an_id IN (
    SELECT id FROM mon_an
    WHERE ten_mon LIKE 'Canh %'
       OR ten_mon LIKE 'Cá nấu%'
       OR ten_mon LIKE 'Nấm nấu%'
       OR ten_mon LIKE 'Tôm nấu%'
       OR ten_mon = 'Bánh mì bò kho'
);
