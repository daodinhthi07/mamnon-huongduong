-- DỌN DỮ LIỆU RÁC do bản nhập Excel cũ để lại.
-- Bản cũ đọc nhầm CHÂN TRANG file ("Người thực hiện", tên người ký, "Cẩm Lệ, ngày … tháng … năm …", "HIỆU TRƯỞNG")
-- thành MÓN ĂN: tạo 5 món rác trong mon_an (nhóm Xế) và gán vào thực đơn Nhà trẻ - Xế các ngày 31/8, 1/9, 3/9, 4/9.
-- Script này xóa các ô thực đơn dùng 5 món đó rồi xóa 5 món. Chỉ đụng món có ghi chú "Tạo khi nhập thực đơn từ Excel".
-- Chạy lại nhiều lần vô hại.
BEGIN TRANSACTION;

CREATE TEMP TABLE mon_rac AS
SELECT id FROM mon_an
WHERE ghi_chu LIKE 'Tạo khi nhập thực đơn từ Excel%'
  AND ten_mon IN ('Người thực hiện', 'Mai Thị Mỹ Linh', 'Cẩm Lệ, ngày 28 tháng 8 năm 2026', 'HIỆU TRƯỞNG', 'Đinh Thị Hà');

DELETE FROM thuc_don                         WHERE mon_an_id IN (SELECT id FROM mon_rac);
DELETE FROM dinh_luong_nguyen_lieu           WHERE mon_an_id IN (SELECT id FROM mon_rac);
DELETE FROM dinh_luong_nguyen_lieu_theo_co_so WHERE mon_an_id IN (SELECT id FROM mon_rac);
DELETE FROM quy_tac_nguyen_lieu              WHERE mon_an_id IN (SELECT id FROM mon_rac);
DELETE FROM mon_an                           WHERE id IN (SELECT id FROM mon_rac);

DROP TABLE mon_rac;
COMMIT;

-- (TUỲ CHỌN) File Excel ghi Thứ 2 → Thứ 4 tuần 31/8 là "Nghỉ Lễ 2-9"; hệ thống mới chỉ đánh dấu nghỉ ngày 2/9.
-- Bỏ dấu -- ở 2 dòng dưới nếu muốn 31/8 và 1/9 cũng là ngày nghỉ lễ:
-- UPDATE ngay_hoc SET trang_thai = 'Nghỉ lễ', ghi_chu = 'Nghỉ Lễ 2-9' WHERE ngay IN ('2026-08-31', '2026-09-01');
