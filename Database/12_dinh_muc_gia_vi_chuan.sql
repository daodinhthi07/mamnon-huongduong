-- =====================================================================
-- ĐỊNH MỨC GIA VỊ HẰNG NGÀY CỐ ĐỊNH (áp dụng cho MỌI ngày, không phải nhập
-- tay từng ngày) — trả lời yêu cầu "gia vị hằng ngày cho cố định tất cả
-- các ngày".
--
-- Cách hoạt động (xem ChiTietNgayTinhToanService.LayGiaViThatAsync):
--   - Nếu (cơ sở, ngày) đã có PHIẾU XUẤT KHO THẬT loại "Gia vị hằng ngày"
--     (lập tay ở /kho/xuat-kho) → dùng phiếu thật, KHÔNG đụng tới bảng này —
--     cho phép 1 ngày cụ thể lệch định mức khi thực tế khác (hết hàng phải
--     đổi loại khác, mua thêm...).
--   - Nếu CHƯA có phiếu → tự tính = định mức (dinh_luong_1_suat) × sĩ số ăn
--     của đúng ngày đó (dinh_muc_an_ngay), không cần nhập gì thêm.
--
-- Dữ liệu seed dưới đây lấy lại từ Database/10_dinh_muc_gia_vi_hang_ngay.sql
-- (định mức trung bình gộp qua 72 ngày quan sát được, trước khi tách riêng
-- nhóm "Ăn sáng - Nhà trẻ") — cột "Ăn sáng - Nhà trẻ" tạm seed BẰNG cột "Ăn
-- sáng" (Mẫu giáo) vì dữ liệu quan sát gốc chưa tách 2 nhóm này, CẦN VÀO
-- trang /kho/dinh-muc-gia-vi rà lại cho khớp thực tế Nhà trẻ.
--
-- LƯU Ý QUAN TRỌNG: có 3 loại Gạo (Gạo quê vàng/Gạo cỏ may/Gạo bồ câu) và 2
-- loại Muối, 2 loại Xì dầu cùng có định mức > 0 — vì số liệu gốc là TRUNG
-- BÌNH gộp nhiều tuần đổi brand khác nhau, không phải trường dùng ĐỒNG THỜI
-- cả 3 loại gạo 1 ngày. Vào /kho/dinh-muc-gia-vi XÓA bớt các dòng brand
-- không dùng nữa, chỉ giữ đúng 1 loại gạo / 1 loại muối / 1 loại xì dầu
-- đang dùng thật, để mục 2 tự động không bị tính trùng.
-- =====================================================================

CREATE TABLE IF NOT EXISTS dinh_muc_gia_vi_chuan (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    hang_hoa_id        INTEGER NOT NULL REFERENCES hang_hoa(id),
    nhom_chi_phi_id    INTEGER NOT NULL REFERENCES nhom_chi_phi(id),
    dinh_luong_1_suat  DECIMAL(14,6) NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_dmgvc_hanghoa_nhom ON dinh_muc_gia_vi_chuan (hang_hoa_id, nhom_chi_phi_id);

-- 2 mặt hàng còn thiếu trong hang_hoa (chưa từng được seed ở 05_seed_hang_hoa.sql) —
-- bổ sung, phân loại 'Gia vị' luôn cho đúng (xem 10_sua_lai_phan_loai_gia_vi_doi_chieu_thuc_te.sql).
INSERT OR IGNORE INTO hang_hoa (ma_hang, ten_hang, dvt_id, loai_hang, don_gia_gan_nhat, is_active) VALUES
    (NULL, 'Gạo cỏ may', (SELECT id FROM don_vi_tinh WHERE ten_dvt = 'Kg'), 'Gia vị', 21000, 1),
    (NULL, 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', (SELECT id FROM don_vi_tinh WHERE ten_dvt = 'Gói'), 'Gia vị', 4000, 1);

-- Xóa sạch trước khi seed lại, để chạy script này nhiều lần không bị trùng dòng.
DELETE FROM dinh_muc_gia_vi_chuan WHERE hang_hoa_id IN (
    SELECT id FROM hang_hoa WHERE ten_hang IN (
        'Gạo quê vàng', 'Gạo bồ câu (Cỏ may)', 'Gạo cỏ may',
        'Đường trắng RS (50kg/bao)', 'Dầu tường an Cooking 1 lít', 'Dầu simply 1 lít',
        'Muối iốt', 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)',
        'Nước mắm đệ nhị 800ml', 'Bột nêm Knor 900gr', 'Bột canh gà 200gr (50 gói/thùng)',
        'Xì dầu đầu bếp 4L (4 can/thùng)', 'Xì dầu Maggi 700ml (12 chai/thùng)'
    )
);

INSERT INTO dinh_muc_gia_vi_chuan (hang_hoa_id, nhom_chi_phi_id, dinh_luong_1_suat)
SELECT h.id, n.id, v.dinh_luong
FROM (
    -- ten_hang, ten_nhom, dinh_luong_1_suat
    SELECT 'Gạo quê vàng' ten_hang, 'Ăn sáng' ten_nhom, 0.001969 dinh_luong UNION ALL
    SELECT 'Gạo quê vàng', 'Ăn sáng - Nhà trẻ', 0.001969 UNION ALL
    SELECT 'Gạo quê vàng', 'Nhà trẻ', 0.059661 UNION ALL
    SELECT 'Gạo quê vàng', 'Mẫu giáo', 0.056717 UNION ALL

    SELECT 'Gạo bồ câu (Cỏ may)', 'Ăn sáng', 0.005353 UNION ALL
    SELECT 'Gạo bồ câu (Cỏ may)', 'Ăn sáng - Nhà trẻ', 0.005353 UNION ALL
    SELECT 'Gạo bồ câu (Cỏ may)', 'Nhà trẻ', 0.064605 UNION ALL
    SELECT 'Gạo bồ câu (Cỏ may)', 'Mẫu giáo', 0.064245 UNION ALL

    SELECT 'Gạo cỏ may', 'Ăn sáng', 0.0 UNION ALL
    SELECT 'Gạo cỏ may', 'Ăn sáng - Nhà trẻ', 0.0 UNION ALL
    SELECT 'Gạo cỏ may', 'Nhà trẻ', 0.083571 UNION ALL
    SELECT 'Gạo cỏ may', 'Mẫu giáo', 0.080484 UNION ALL

    SELECT 'Đường trắng RS (50kg/bao)', 'Ăn sáng', 0.001251 UNION ALL
    SELECT 'Đường trắng RS (50kg/bao)', 'Ăn sáng - Nhà trẻ', 0.001251 UNION ALL
    SELECT 'Đường trắng RS (50kg/bao)', 'Nhà trẻ', 0.003282 UNION ALL
    SELECT 'Đường trắng RS (50kg/bao)', 'Mẫu giáo', 0.002032 UNION ALL

    SELECT 'Dầu tường an Cooking 1 lít', 'Ăn sáng', 0.001335 UNION ALL
    SELECT 'Dầu tường an Cooking 1 lít', 'Ăn sáng - Nhà trẻ', 0.001335 UNION ALL
    SELECT 'Dầu tường an Cooking 1 lít', 'Nhà trẻ', 0.003898 UNION ALL
    SELECT 'Dầu tường an Cooking 1 lít', 'Mẫu giáo', 0.007929 UNION ALL

    SELECT 'Dầu simply 1 lít', 'Ăn sáng', 0.001139 UNION ALL
    SELECT 'Dầu simply 1 lít', 'Ăn sáng - Nhà trẻ', 0.001139 UNION ALL
    SELECT 'Dầu simply 1 lít', 'Nhà trẻ', 0.00382 UNION ALL
    SELECT 'Dầu simply 1 lít', 'Mẫu giáo', 0.006334 UNION ALL

    SELECT 'Muối iốt', 'Ăn sáng', 0.003386 UNION ALL
    SELECT 'Muối iốt', 'Ăn sáng - Nhà trẻ', 0.003386 UNION ALL
    SELECT 'Muối iốt', 'Nhà trẻ', 0.011093 UNION ALL
    SELECT 'Muối iốt', 'Mẫu giáo', 0.003813 UNION ALL

    SELECT 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', 'Ăn sáng', 0.00182 UNION ALL
    SELECT 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', 'Ăn sáng - Nhà trẻ', 0.00182 UNION ALL
    SELECT 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', 'Nhà trẻ', 0.008964 UNION ALL
    SELECT 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', 'Mẫu giáo', 0.002046 UNION ALL

    SELECT 'Nước mắm đệ nhị 800ml', 'Ăn sáng', 0.001296 UNION ALL
    SELECT 'Nước mắm đệ nhị 800ml', 'Ăn sáng - Nhà trẻ', 0.001296 UNION ALL
    SELECT 'Nước mắm đệ nhị 800ml', 'Nhà trẻ', 0.004509 UNION ALL
    SELECT 'Nước mắm đệ nhị 800ml', 'Mẫu giáo', 0.004595 UNION ALL

    SELECT 'Bột nêm Knor 900gr', 'Ăn sáng', 0.00062 UNION ALL
    SELECT 'Bột nêm Knor 900gr', 'Ăn sáng - Nhà trẻ', 0.00062 UNION ALL
    SELECT 'Bột nêm Knor 900gr', 'Nhà trẻ', 0.002975 UNION ALL
    SELECT 'Bột nêm Knor 900gr', 'Mẫu giáo', 0.002431 UNION ALL

    SELECT 'Bột canh gà 200gr (50 gói/thùng)', 'Ăn sáng', 0.00105 UNION ALL
    SELECT 'Bột canh gà 200gr (50 gói/thùng)', 'Ăn sáng - Nhà trẻ', 0.00105 UNION ALL
    SELECT 'Bột canh gà 200gr (50 gói/thùng)', 'Nhà trẻ', 0.004704 UNION ALL
    SELECT 'Bột canh gà 200gr (50 gói/thùng)', 'Mẫu giáo', 0.005939 UNION ALL

    SELECT 'Xì dầu đầu bếp 4L (4 can/thùng)', 'Ăn sáng', 0.000882 UNION ALL
    SELECT 'Xì dầu đầu bếp 4L (4 can/thùng)', 'Ăn sáng - Nhà trẻ', 0.000882 UNION ALL
    SELECT 'Xì dầu đầu bếp 4L (4 can/thùng)', 'Nhà trẻ', 0.001667 UNION ALL
    SELECT 'Xì dầu đầu bếp 4L (4 can/thùng)', 'Mẫu giáo', 0.00212 UNION ALL

    SELECT 'Xì dầu Maggi 700ml (12 chai/thùng)', 'Ăn sáng', 0.00108 UNION ALL
    SELECT 'Xì dầu Maggi 700ml (12 chai/thùng)', 'Ăn sáng - Nhà trẻ', 0.00108 UNION ALL
    SELECT 'Xì dầu Maggi 700ml (12 chai/thùng)', 'Nhà trẻ', 0.003558 UNION ALL
    SELECT 'Xì dầu Maggi 700ml (12 chai/thùng)', 'Mẫu giáo', 0.002218
) v
JOIN hang_hoa h ON h.ten_hang = v.ten_hang
JOIN nhom_chi_phi n ON n.ten_nhom = v.ten_nhom;
