-- =====================================================================
-- BỔ SUNG: ĐỊNH MỨC TỶ LỆ NGÂN SÁCH THEO HẠNG MỤC MÓN
-- Chạy sau 01_kho_thuc_pham_mam_non.sql và 02_thuc_don_mon_an.sql
--
-- Bài toán: món ăn cụ thể được chọn LINH HOẠT mỗi tuần (giá thực tế
-- thay đổi theo món), nhưng tổng tiền chi cho mỗi HẠNG MỤC (Canh, Mặn,
-- Xào, Tráng miệng, Xế, Sữa, Gạo, Gia vị) trên 1 suất ăn phải giữ đúng
-- định mức cố định theo khối — đúng theo bảng "Tỷ lệ" thấy trong file
-- Excel (Canh 3.000đ, Mặn 9.230-9.490đ ... cộng đúng bằng 28.000đ/suất
-- của Nhà trẻ/Mẫu giáo). Bữa "SÁNG" không nằm trong 8 hạng mục này vì
-- có ngân sách riêng (nhóm chi phí "Ăn sáng", 10.000đ/suất).
-- =====================================================================

PRAGMA foreign_keys = ON;

-- ---------------------------------------------------------------------
-- 1. Hạng mục ngân sách — danh mục cố định, dùng chung mọi cơ sở
-- ---------------------------------------------------------------------
CREATE TABLE hang_muc_ngan_sach (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_hang_muc  TEXT NOT NULL,                    -- Canh, Mặn, Xào, Tráng miệng, Xế, Sữa, Gạo, Gia vị
    thu_tu        INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX uq_hangmuc_ten ON hang_muc_ngan_sach (ten_hang_muc);

-- ---------------------------------------------------------------------
-- 2. Định mức tỷ lệ ngân sách: số tiền/suất ăn cho mỗi hạng mục, theo
--    khối, có hiệu lực theo thời gian (giống ghi chú "tăng/giảm so với
--    năm trước" thấy ở sheet Bảng giá — định mức có thể đổi theo năm).
-- ---------------------------------------------------------------------
CREATE TABLE dinh_muc_ty_le_ngan_sach (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    khoi_id          INTEGER NOT NULL,
    hang_muc_id      INTEGER NOT NULL,
    tien_dinh_muc    DECIMAL(10,2) NOT NULL,        -- Đồng / suất ăn
    ngay_ap_dung_tu  TEXT NOT NULL,
    ghi_chu          TEXT NULL,
    FOREIGN KEY (khoi_id) REFERENCES khoi_hoc(id),
    FOREIGN KEY (hang_muc_id) REFERENCES hang_muc_ngan_sach(id)
);
CREATE UNIQUE INDEX uq_dmtl ON dinh_muc_ty_le_ngan_sach (khoi_id, hang_muc_id, ngay_ap_dung_tu);

-- ---------------------------------------------------------------------
-- 3. Gắn mỗi loại bữa ăn với hạng mục ngân sách tương ứng.
--    NULL cho các bữa thuộc nhóm "Ăn sáng" (ngân sách riêng, không
--    thuộc 8 hạng mục Canh/Mặn/Xào...).
--    Lưu ý SQLite: ALTER TABLE ADD COLUMN không hỗ trợ "AFTER cột" hay
--    gộp nhiều ADD trong 1 câu lệnh như MySQL — nhưng CHO PHÉP khai báo
--    REFERENCES ngay trong ADD COLUMN, nên vẫn có FOREIGN KEY đầy đủ.
-- ---------------------------------------------------------------------
ALTER TABLE loai_bua_an ADD COLUMN hang_muc_id INTEGER NULL REFERENCES hang_muc_ngan_sach(id);

-- ---------------------------------------------------------------------
-- 4. Dữ liệu mẫu
-- ---------------------------------------------------------------------
INSERT INTO hang_muc_ngan_sach (ten_hang_muc, thu_tu) VALUES
    ('Canh', 1), ('Mặn', 2), ('Xào', 3), ('Gạo', 4),
    ('Gia vị', 5), ('Tráng miệng', 6), ('Xế', 7), ('Sữa', 8);

-- Định mức mẫu theo đúng số liệu quan sát được (Nhà trẻ & Mẫu giáo, áp dụng từ đầu năm học 2026)
INSERT INTO dinh_muc_ty_le_ngan_sach (khoi_id, hang_muc_id, tien_dinh_muc, ngay_ap_dung_tu)
SELECT k.id, h.id, v.tien, '2026-09-01'
FROM (
    SELECT 'Nhà trẻ' AS ten_khoi, 'Canh' AS hang_muc, 3000 AS tien
    UNION ALL SELECT 'Nhà trẻ', 'Mặn', 9230
    UNION ALL SELECT 'Nhà trẻ', 'Xào', 0
    UNION ALL SELECT 'Nhà trẻ', 'Gạo', 770
    UNION ALL SELECT 'Nhà trẻ', 'Gia vị', 700
    UNION ALL SELECT 'Nhà trẻ', 'Tráng miệng', 2300
    UNION ALL SELECT 'Nhà trẻ', 'Xế', 7000
    UNION ALL SELECT 'Nhà trẻ', 'Sữa', 5000
    UNION ALL SELECT 'Mẫu giáo', 'Canh', 3000
    UNION ALL SELECT 'Mẫu giáo', 'Mặn', 9490
    UNION ALL SELECT 'Mẫu giáo', 'Xào', 1600
    UNION ALL SELECT 'Mẫu giáo', 'Gạo', 910
    UNION ALL SELECT 'Mẫu giáo', 'Gia vị', 700
    UNION ALL SELECT 'Mẫu giáo', 'Tráng miệng', 2300
    UNION ALL SELECT 'Mẫu giáo', 'Xế', 7000
    UNION ALL SELECT 'Mẫu giáo', 'Sữa', 3000
) v
JOIN khoi_hoc k ON k.ten_khoi = v.ten_khoi
JOIN hang_muc_ngan_sach h ON h.ten_hang_muc = v.hang_muc;

-- Gắn loại bữa ăn hiện có với hạng mục tương ứng (SÁNG/PHỤ SÁNG/TRƯA/CHÍNH CHIỀU giữ NULL)
-- 2 lưu ý khi chuyển từ MySQL sang SQLite:
--   1. SQLite không hỗ trợ UPDATE ... JOIN kiểu MySQL -> dùng subquery tương quan.
--   2. QUAN TRỌNG: bản MySQL so khớp trực tiếp ten_bua = ten_hang_muc (VD
--      'CANH' = 'Canh') và chạy đúng vì mặc định dùng collation
--      utf8mb4_unicode_ci (không phân biệt hoa/thường). SQLite mặc định
--      PHÂN BIỆT hoa/thường và không có collation Unicode-aware cho tiếng
--      Việt có dấu (COLLATE NOCASE chỉ xử lý được A-Z, không xử lý được
--      Ặ/ặ...) — nếu giữ nguyên cách so chuỗi trực tiếp, toàn bộ hang_muc_id
--      sẽ bị NULL mà không báo lỗi gì. Đã kiểm chứng bằng cách chạy thử
--      thật, không chỉ suy luận. Sửa bằng ánh xạ CASE tường minh, không
--      phụ thuộc so khớp chuỗi hoa/thường nữa.
UPDATE loai_bua_an
SET hang_muc_id = (
    SELECT hm.id FROM hang_muc_ngan_sach hm
    WHERE hm.ten_hang_muc = CASE loai_bua_an.ten_bua
        WHEN 'CANH' THEN 'Canh'
        WHEN 'MẶN' THEN 'Mặn'
        WHEN 'XÀO' THEN 'Xào'
        WHEN 'TRÁNG MIỆNG' THEN 'Tráng miệng'
        WHEN 'XẾ' THEN 'Xế'
    END
)
WHERE ten_bua IN ('CANH', 'MẶN', 'XÀO', 'TRÁNG MIỆNG', 'XẾ');

-- ---------------------------------------------------------------------
-- 5. VIEW: đối chiếu chi phí ước tính thực tế theo hạng mục (từ thực
--    đơn đã duyệt + định lượng nguyên liệu + đơn giá gần nhất) so với
--    định mức tỷ lệ ngân sách — giúp phát hiện món cụ thể tuy linh
--    hoạt nhưng đã làm vượt định mức của cả hạng mục.
-- ---------------------------------------------------------------------
DROP VIEW IF EXISTS v_doi_chieu_dinh_muc_hang_muc;
CREATE VIEW v_doi_chieu_dinh_muc_hang_muc AS
SELECT
    td.co_so_id,
    nh.ngay,
    td.khoi_id,
    hm.id                    AS hang_muc_id,
    hm.ten_hang_muc,
    ROUND(SUM(dl.dinh_luong * COALESCE(hh.don_gia_gan_nhat, 0)), 2) AS chi_phi_uoc_tinh_1_suat,
    dm.tien_dinh_muc,
    ROUND(SUM(dl.dinh_luong * COALESCE(hh.don_gia_gan_nhat, 0)) - dm.tien_dinh_muc, 2) AS chenh_lech_1_suat
FROM thuc_don td
JOIN ngay_hoc nh              ON nh.id = td.ngay_hoc_id
JOIN loai_bua_an lb            ON lb.id = td.loai_bua_an_id AND lb.hang_muc_id IS NOT NULL
JOIN hang_muc_ngan_sach hm     ON hm.id = lb.hang_muc_id
JOIN dinh_luong_nguyen_lieu dl ON dl.mon_an_id = td.mon_an_id
JOIN hang_hoa hh               ON hh.id = dl.hang_hoa_id
LEFT JOIN dinh_muc_ty_le_ngan_sach dm
       ON dm.khoi_id = td.khoi_id
      AND dm.hang_muc_id = hm.id
      AND dm.ngay_ap_dung_tu = (
            SELECT MAX(d2.ngay_ap_dung_tu)
            FROM dinh_muc_ty_le_ngan_sach d2
            WHERE d2.khoi_id = td.khoi_id
              AND d2.hang_muc_id = hm.id
              AND d2.ngay_ap_dung_tu <= nh.ngay
          )
WHERE td.trang_thai = 'Đã duyệt'
GROUP BY td.co_so_id, nh.ngay, td.khoi_id, hm.id, hm.ten_hang_muc, dm.tien_dinh_muc;
