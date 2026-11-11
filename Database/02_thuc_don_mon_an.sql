-- =====================================================================
-- BỔ SUNG: THỰC ĐƠN + MÓN ĂN + ĐỊNH LƯỢNG NGUYÊN LIỆU
-- Chạy sau 01_kho_thuc_pham_mam_non.sql (cùng 1 file .db SQLite, chung
-- các bảng co_so, khoi_hoc, hang_hoa đã tạo trước đó).
-- =====================================================================

PRAGMA foreign_keys = ON;

-- ---------------------------------------------------------------------
-- 1. Trạng thái từng ngày học (học bình thường / nghỉ lễ / nghỉ khác)
--    Tách riêng khỏi thực đơn để không phải ghi lặp "NGHỈ LỄ 2/9" ở
--    từng dòng món ăn như file Excel gốc.
-- ---------------------------------------------------------------------
CREATE TABLE ngay_hoc (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id   INTEGER NOT NULL,
    ngay       TEXT NOT NULL,
    trang_thai TEXT NOT NULL DEFAULT 'Học bình thường'
                 CHECK (trang_thai IN ('Học bình thường', 'Nghỉ lễ', 'Nghỉ khác')),
    ghi_chu    TEXT NULL,                           -- VD: Nghỉ lễ Quốc khánh 2/9
    FOREIGN KEY (co_so_id) REFERENCES co_so(id)
);
CREATE UNIQUE INDEX uq_ngayhoc ON ngay_hoc (co_so_id, ngay);

-- ---------------------------------------------------------------------
-- 2. Loại bữa ăn — cấu hình được theo từng khối, KHÔNG fix cứng trong
--    code, vì Mẫu giáo và Nhà trẻ có tên/số bữa khác nhau hoàn toàn.
--    Dữ liệu mẫu bên dưới lấy đúng theo file Excel gốc.
-- ---------------------------------------------------------------------
CREATE TABLE loai_bua_an (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    khoi_id  INTEGER NOT NULL,
    ten_bua  TEXT    NOT NULL,
    thu_tu   INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (khoi_id) REFERENCES khoi_hoc(id)
);
CREATE UNIQUE INDEX uq_loaibua ON loai_bua_an (khoi_id, ten_bua);

-- ---------------------------------------------------------------------
-- 3. Ngân hàng món ăn dùng chung cho mọi cơ sở/khối/tuần
-- ---------------------------------------------------------------------
CREATE TABLE mon_an (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_mon   TEXT NOT NULL,
    ghi_chu   TEXT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX uq_monan_ten ON mon_an (ten_mon);

-- ---------------------------------------------------------------------
-- 4. Định lượng nguyên liệu cho 1 suất ăn của từng món — đây là mắt
--    xích nối Thực đơn với Kho: từ thực đơn đã duyệt + số suất ăn
--    trong ngày, hệ thống tự tính ra cần xuất bao nhiêu nguyên liệu,
--    thay vì gõ tay lại như bảng "Chợ" trong file Excel.
-- ---------------------------------------------------------------------
CREATE TABLE dinh_luong_nguyen_lieu (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    mon_an_id   INTEGER NOT NULL,
    hang_hoa_id INTEGER NOT NULL,
    dinh_luong  DECIMAL(10,4) NOT NULL,             -- Lượng dùng cho 1 suất ăn, theo ĐVT của hang_hoa
    FOREIGN KEY (mon_an_id) REFERENCES mon_an(id) ON DELETE CASCADE,
    FOREIGN KEY (hang_hoa_id) REFERENCES hang_hoa(id)
);
CREATE UNIQUE INDEX uq_dlnl ON dinh_luong_nguyen_lieu (mon_an_id, hang_hoa_id);

-- ---------------------------------------------------------------------
-- 5. Thay bảng thuc_don cũ (bua_an/mon_an là văn bản tự do) bằng bản
--    có tham chiếu danh mục + quy trình duyệt.
-- ---------------------------------------------------------------------
DROP TABLE IF EXISTS thuc_don;

CREATE TABLE thuc_don (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id        INTEGER NOT NULL,
    khoi_id         INTEGER NOT NULL,
    ngay_hoc_id     INTEGER NOT NULL,
    loai_bua_an_id  INTEGER NOT NULL,
    mon_an_id       INTEGER NULL,                    -- NULL nếu ngày đó không có bữa (VD: nghỉ lễ)
    trang_thai      TEXT NOT NULL DEFAULT 'Nháp'
                      CHECK (trang_thai IN ('Nháp', 'Chờ duyệt', 'Đã duyệt')),
    nguoi_lap       TEXT NULL,
    nguoi_duyet     TEXT NULL,
    ngay_lap        TEXT NULL,                       -- Ngày soạn thảo, có thể khác tuần áp dụng
    ngay_duyet      TEXT NULL,
    created_at      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (co_so_id) REFERENCES co_so(id),
    FOREIGN KEY (khoi_id) REFERENCES khoi_hoc(id),
    FOREIGN KEY (ngay_hoc_id) REFERENCES ngay_hoc(id),
    FOREIGN KEY (loai_bua_an_id) REFERENCES loai_bua_an(id),
    FOREIGN KEY (mon_an_id) REFERENCES mon_an(id)
);
CREATE UNIQUE INDEX uq_thucdon ON thuc_don (co_so_id, khoi_id, ngay_hoc_id, loai_bua_an_id);

-- ---------------------------------------------------------------------
-- 6. Dữ liệu mẫu: loại bữa ăn theo khối, đúng theo file Excel gốc
-- ---------------------------------------------------------------------
INSERT INTO loai_bua_an (khoi_id, ten_bua, thu_tu)
SELECT khoi_hoc.id, seed.bua, seed.thu_tu FROM khoi_hoc
JOIN (
    SELECT 'Mẫu giáo' AS ten_khoi, 'SÁNG' AS bua, 1 AS thu_tu
    UNION ALL SELECT 'Mẫu giáo', 'CANH', 2
    UNION ALL SELECT 'Mẫu giáo', 'MẶN', 3
    UNION ALL SELECT 'Mẫu giáo', 'XÀO', 4
    UNION ALL SELECT 'Mẫu giáo', 'TRÁNG MIỆNG', 5
    UNION ALL SELECT 'Mẫu giáo', 'XẾ', 6
    UNION ALL SELECT 'Nhà trẻ', 'SÁNG', 1
    UNION ALL SELECT 'Nhà trẻ', 'CANH', 2
    UNION ALL SELECT 'Nhà trẻ', 'MẶN', 3
    UNION ALL SELECT 'Nhà trẻ', 'TRÁNG MIỆNG', 4
    UNION ALL SELECT 'Nhà trẻ', 'XẾ', 5
    UNION ALL SELECT 'Nhà trẻ', 'CHÍNH CHIỀU', 6
) seed ON seed.ten_khoi = khoi_hoc.ten_khoi;

-- ---------------------------------------------------------------------
-- 7. VIEW: Nhu cầu nguyên liệu theo ngày, tính tự động từ thực đơn đã
--    duyệt + số suất ăn (bảng dinh_muc_an_ngay) — thay cho việc gõ tay
--    số lượng cần mua như bảng "Chợ" trong file Excel.
-- ---------------------------------------------------------------------
DROP VIEW IF EXISTS v_nhu_cau_nguyen_lieu_theo_ngay;
CREATE VIEW v_nhu_cau_nguyen_lieu_theo_ngay AS
SELECT
    td.co_so_id,
    nh.ngay,
    hh.id            AS hang_hoa_id,
    hh.ten_hang,
    dvt.ten_dvt,
    SUM(dl.dinh_luong * COALESCE(dm.so_suat_an, 0)) AS tong_can_dung
FROM thuc_don td
JOIN ngay_hoc nh          ON nh.id = td.ngay_hoc_id
JOIN dinh_luong_nguyen_lieu dl ON dl.mon_an_id = td.mon_an_id
JOIN hang_hoa hh           ON hh.id = dl.hang_hoa_id
JOIN don_vi_tinh dvt       ON dvt.id = hh.dvt_id
LEFT JOIN dinh_muc_an_ngay dm
       ON dm.co_so_id = td.co_so_id AND dm.ngay = nh.ngay
WHERE td.trang_thai = 'Đã duyệt'
GROUP BY td.co_so_id, nh.ngay, hh.id, hh.ten_hang, dvt.ten_dvt;
