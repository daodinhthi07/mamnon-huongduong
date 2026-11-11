-- =====================================================================
-- GỘP KHỐI: chỉ còn 2 khối "Mẫu giáo" / "Nhà trẻ" (bỏ tách theo tuổi)
-- Chạy sau 01/02/03/04/05/06_*.sql
--
-- Xác nhận thực tế: toàn trường (mọi cơ sở) chỉ vận hành 2 khối, không tách
-- Nhà trẻ theo độ tuổi. Cấu trúc "Nhà trẻ (6-18T)" / "Nhà trẻ (18-24T)" ở
-- 01/02/03_*.sql là dữ liệu cũ/sai, gộp lại thành 1 khối "Nhà trẻ".
--
-- GIỮ LẠI khối "Nhà trẻ (18-24T)" (đổi tên thành "Nhà trẻ") vì cơ cấu bữa
-- của nó — SÁNG / CANH / MẶN / TRÁNG MIỆNG / XẾ / CHÍNH CHIỀU — khớp đúng
-- với file thực đơn Excel thực tế đang dùng. Cơ cấu bữa của "Nhà trẻ
-- (6-18T)" (SÁNG / PHỤ SÁNG / TRƯA / TRÁNG MIỆNG / XẾ / CHÍNH CHIỀU) không
-- còn dùng nữa nên bị xoá bỏ, KHÔNG cố map sang cơ cấu mới vì tên bữa không
-- tương ứng 1-1 (PHỤ SÁNG/TRƯA không có bữa nào trùng bên kia).
-- =====================================================================

PRAGMA foreign_keys = ON;

-- 1. Chuyển định mức ngân sách (Canh/Mặn/Xào/Gạo/Gia vị/Tráng miệng/Xế/Sữa)
--    đang gắn với "Nhà trẻ (6-18T)" sang khối sống sót "Nhà trẻ (18-24T)".
--    "Nhà trẻ (18-24T)" trước giờ chưa có định mức riêng (xem 03_*.sql) nên
--    không có xung đột UNIQUE INDEX (khoi_id, hang_muc_id, ngay_ap_dung_tu).
UPDATE dinh_muc_ty_le_ngan_sach
SET khoi_id = (SELECT id FROM khoi_hoc WHERE ten_khoi = 'Nhà trẻ (18-24T)')
WHERE khoi_id = (SELECT id FROM khoi_hoc WHERE ten_khoi = 'Nhà trẻ (6-18T)');

-- 2. Xoá thực đơn (nếu người dùng đã lỡ soạn) gắn với các bữa của khối cũ
--    "Nhà trẻ (6-18T)" — không map được sang cơ cấu bữa mới nên phải xoá,
--    không giữ lại được.
DELETE FROM thuc_don
WHERE loai_bua_an_id IN (
    SELECT id FROM loai_bua_an
    WHERE khoi_id = (SELECT id FROM khoi_hoc WHERE ten_khoi = 'Nhà trẻ (6-18T)')
);

-- 3. Xoá các bữa (loai_bua_an) thuộc khối cũ "Nhà trẻ (6-18T)"
DELETE FROM loai_bua_an
WHERE khoi_id = (SELECT id FROM khoi_hoc WHERE ten_khoi = 'Nhà trẻ (6-18T)');

-- 4. Xoá khối cũ "Nhà trẻ (6-18T)"
DELETE FROM khoi_hoc WHERE ten_khoi = 'Nhà trẻ (6-18T)';

-- 5. Đổi tên khối còn lại thành "Nhà trẻ" (bỏ chú thích độ tuổi)
UPDATE khoi_hoc SET ten_khoi = 'Nhà trẻ', thu_tu = 2 WHERE ten_khoi = 'Nhà trẻ (18-24T)';
