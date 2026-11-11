-- =====================================================================
-- SỬA LẠI THIẾT KẾ MỤC "2. Theo dõi xuất kho gia vị hằng ngày"
-- 
-- Phát hiện quan trọng: mục này KHÔNG PHẢI nguyên liệu trích từ công thức
-- món ăn được chọn hôm đó (khác hẳn mục 3/4) — nó là 1 KHẨU PHẦN CỐ ĐỊNH
-- xuất kho mỗi ngày theo đầu suất ăn (gạo/đường/dầu/muối/nước mắm/bột nêm/
-- bột canh/xì dầu), GIỐNG NHAU mọi ngày bất kể món gì được chọn. Vì vậy:
--
--  1) HOÀN TÁC việc phân loại lại 'Gia vị' ở file 08 — hành/tỏi/ớt/sả/gừng/
--     nghệ/ngò/chanh/me vẫn là nguyên liệu THẬT trong công thức món ăn (đúng
--     ra phải hiện ở mục 3/4), không phải khẩu phần cố định của mục 2.
--  2) Tạo bảng 'dinh_muc_gia_vi_ngay' — định mức CỐ ĐỊNH/suất cho từng mặt
--     hàng khô, tách riêng theo nhóm Ăn sáng/Nhà trẻ/Mẫu giáo (vì tỉ lệ khác
--     nhau), tính bằng trung bình gộp qua 72 ngày quan sát được.
--
-- Chạy sau 05/06/07/08/09_*.sql.
-- =====================================================================

PRAGMA foreign_keys = ON;

-- ---- 1) Hoàn tác phân loại sai ở file 08 ----
UPDATE hang_hoa SET loai_hang = 'Thực phẩm tươi' WHERE ten_hang IN (
    'Hành lá',
    'Hành ngò',
    'Hành Tây',
    'Hành tím',
    'Hành tím lột vỏ',
    'Gừng quê',
    'Gừng to',
    'Nghệ tươi',
    'Nghệ tươi xay',
    'Ngò (mùi)',
    'Ngò gai',
    'Ớt chuông đỏ',
    'Ớt chuông vàng',
    'Ớt chuông xanh',
    'Ớt trái',
    'Sả',
    'Sả băm',
    'Tỏi khô',
    'Tỏi khô sơ chế',
    'Tỏi tím hà nội',
    'Lá chanh',
    'Me chua'
);

UPDATE hang_hoa SET loai_hang = 'Khác' WHERE ten_hang IN (
    'Muối iốt',
    'Muối đầu bếp',
    'Muối hạt',
    'Dầu mè trắng Tường An 250ml (12 chai/thùng)'
);

-- ---- 1b) Thêm 2 hàng hoá còn thiếu (chưa có trong 05_seed_hang_hoa.sql) ----
INSERT OR IGNORE INTO hang_hoa (ma_hang, ten_hang, dvt_id, loai_hang, don_gia_gan_nhat, is_active) VALUES
    (NULL, 'Gạo cỏ may', (SELECT id FROM don_vi_tinh WHERE ten_dvt = 'Kg'), 'Khác', 21000, 1),
    (NULL, 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)', (SELECT id FROM don_vi_tinh WHERE ten_dvt = 'Gói'), 'Khác', 4000, 1);

-- ---- 2) Bảng định mức gia vị/hàng khô cố định theo suất ----
CREATE TABLE IF NOT EXISTS dinh_muc_gia_vi_ngay (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    hang_hoa_id         INTEGER NOT NULL REFERENCES hang_hoa(id),
    dinh_luong_an_sang  DECIMAL(14,6) NOT NULL DEFAULT 0,
    dinh_luong_nha_tre  DECIMAL(14,6) NOT NULL DEFAULT 0,
    dinh_luong_mau_giao DECIMAL(14,6) NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_dmgv_hanghoa ON dinh_muc_gia_vi_ngay (hang_hoa_id);

DELETE FROM dinh_muc_gia_vi_ngay WHERE hang_hoa_id IN (
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo quê vàng'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Đường trắng RS (50kg/bao)'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Dầu tường an Cooking 1 lít'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Muối iốt'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Nước mắm đệ nhị 800ml'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Bột nêm Knor 900gr'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Bột canh gà 200gr (50 gói/thùng)'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Xì dầu đầu bếp 4L (4 can/thùng)'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo bồ câu (Cỏ may)'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Dầu simply 1 lít'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Xì dầu Maggi 700ml (12 chai/thùng)'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo cỏ may'),
    (SELECT id FROM hang_hoa WHERE ten_hang = 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)')
);

INSERT INTO dinh_muc_gia_vi_ngay (hang_hoa_id, dinh_luong_an_sang, dinh_luong_nha_tre, dinh_luong_mau_giao)
VALUES
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo quê vàng'), 0.001969, 0.059661, 0.056717),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Đường trắng RS (50kg/bao)'), 0.001251, 0.003282, 0.002032),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Dầu tường an Cooking 1 lít'), 0.001335, 0.003898, 0.007929),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Muối iốt'), 0.003386, 0.011093, 0.003813),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Nước mắm đệ nhị 800ml'), 0.001296, 0.004509, 0.004595),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Bột nêm Knor 900gr'), 0.00062, 0.002975, 0.002431),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Bột canh gà 200gr (50 gói/thùng)'), 0.00105, 0.004704, 0.005939),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Xì dầu đầu bếp 4L (4 can/thùng)'), 0.000882, 0.001667, 0.00212),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo bồ câu (Cỏ may)'), 0.005353, 0.064605, 0.064245),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Dầu simply 1 lít'), 0.001139, 0.00382, 0.006334),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Xì dầu Maggi 700ml (12 chai/thùng)'), 0.00108, 0.003558, 0.002218),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Gạo cỏ may'), 0.0, 0.083571, 0.080484),
    ((SELECT id FROM hang_hoa WHERE ten_hang = 'Muối iốt 500g (0.5kg/gói; 100 gói/bao)'), 0.00182, 0.008964, 0.002046);