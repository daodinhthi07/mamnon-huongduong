-- 16_ty_le_trang_mieng_2250.sql
-- Bảng tỷ lệ chuẩn (ảnh): Tráng miệng 2.250đ/suất cho cả Nhà trẻ và Mẫu giáo (tổng 29.000đ). Áp dụng từ 02/11/2026. Chạy lại vô hại.
INSERT OR IGNORE INTO dinh_muc_ty_le_ngan_sach (khoi_id, hang_muc_id, tien_dinh_muc, ngay_ap_dung_tu, ghi_chu)
SELECT k.id, h.id, 2250, '2026-11-02', 'Theo bảng tỷ lệ chuẩn (tráng miệng 2.250)'
FROM khoi_hoc k, hang_muc_ngan_sach h WHERE h.ten_hang_muc = 'Tráng miệng';
