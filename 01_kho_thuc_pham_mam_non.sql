-- =====================================================================
-- CƠ SỞ DỮ LIỆU: KHO NHẬP XUẤT THỰC PHẨM / GIA VỊ - TRƯỜNG MẦM NON
-- Thiết kế dựa trên cấu trúc thực tế của file "TUẦN 1 THÁNG 9" (3 cơ sở:
-- TC / PH / ĐT), gồm: danh mục hàng hóa, nhập kho (mua chợ), xuất kho
-- (dùng nấu ăn), thực đơn theo khối, và định mức tiền ăn theo ngày.
--
-- Engine: SQLite 3.31+ (cần bản có hỗ trợ GENERATED ALWAYS AS ... STORED)
-- Không cần CREATE DATABASE — SQLite coi mỗi FILE là 1 database. Chạy:
--   sqlite3 kho_thuc_pham_mam_non.db < 01_kho_thuc_pham_mam_non.sql
--   sqlite3 kho_thuc_pham_mam_non.db < 02_thuc_don_mon_an.sql
--   sqlite3 kho_thuc_pham_mam_non.db < 03_dinh_muc_ty_le_ngan_sach.sql
-- =====================================================================

PRAGMA foreign_keys = ON;

-- ---------------------------------------------------------------------
-- 1. DANH MỤC DÙNG CHUNG
-- ---------------------------------------------------------------------

-- Cơ sở (chi nhánh trường): TC, PH, ĐT ...
CREATE TABLE co_so (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ma_co_so    TEXT    NOT NULL,                 -- Ví dụ: TC, PH, ĐT
    ten_co_so   TEXT    NOT NULL,
    dia_chi     TEXT    NULL,
    is_active   INTEGER NOT NULL DEFAULT 1,        -- 0/1 (SQLite không có kiểu boolean riêng)
    created_at  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_co_so_ma ON co_so (ma_co_so);

-- Đơn vị tính: Kg, Gói, Chai, Hộp, Quả, Trái, Cái...
CREATE TABLE don_vi_tinh (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_dvt  TEXT NOT NULL
);
CREATE UNIQUE INDEX uq_dvt_ten ON don_vi_tinh (ten_dvt);

-- Khối học, dùng cho THỰC ĐƠN: Mẫu giáo / Nhà trẻ
CREATE TABLE khoi_hoc (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_khoi  TEXT    NOT NULL,
    thu_tu    INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX uq_khoi_ten ON khoi_hoc (ten_khoi);

-- Nhóm chi phí, dùng cho BẢNG KÊ CHI TIỀN CHỢ: Ăn sáng / Nhà trẻ / Mẫu giáo
CREATE TABLE nhom_chi_phi (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_nhom   TEXT NOT NULL
);
CREATE UNIQUE INDEX uq_nhom_ten ON nhom_chi_phi (ten_nhom);

-- Danh mục hàng hóa (thực phẩm + gia vị)
CREATE TABLE hang_hoa (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    ma_hang          TEXT    NULL,
    ten_hang         TEXT    NOT NULL,
    dvt_id           INTEGER NOT NULL,
    loai_hang        TEXT    NOT NULL DEFAULT 'Thực phẩm tươi'
                        CHECK (loai_hang IN ('Thực phẩm tươi', 'Gia vị', 'Khác')),
    don_gia_gan_nhat DECIMAL(12,2) NULL,           -- Giá tham khảo gần nhất, chỉ để hiển thị nhanh
    is_active        INTEGER NOT NULL DEFAULT 1,
    created_at       TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (dvt_id) REFERENCES don_vi_tinh(id)
);
CREATE UNIQUE INDEX uq_hanghoa_ten ON hang_hoa (ten_hang);

-- ---------------------------------------------------------------------
-- 2. THỰC ĐƠN
-- ---------------------------------------------------------------------

CREATE TABLE thuc_don (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id   INTEGER NOT NULL,
    khoi_id    INTEGER NOT NULL,
    ngay       TEXT    NOT NULL,                   -- ISO 'YYYY-MM-DD'
    bua_an     TEXT    NOT NULL,                   -- Sáng/Canh/Mặn/Xào/Tráng miệng/Xế/Phụ sáng/Trưa/Chính chiều...
    mon_an     TEXT    NULL,                        -- NULL hoặc "NGHỈ LỄ ..." nếu không có bữa
    created_at TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (co_so_id) REFERENCES co_so(id),
    FOREIGN KEY (khoi_id) REFERENCES khoi_hoc(id)
);
CREATE UNIQUE INDEX uq_thucdon ON thuc_don (co_so_id, khoi_id, ngay, bua_an);

-- ---------------------------------------------------------------------
-- 3. ĐỊNH MỨC TIỀN ĂN THEO NGÀY (đầu mỗi bảng "Chợ ..." trong file gốc)
-- ---------------------------------------------------------------------

CREATE TABLE dinh_muc_an_ngay (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id           INTEGER NOT NULL,
    nhom_chi_phi_id    INTEGER NOT NULL,
    ngay               TEXT    NOT NULL,
    tien_an_1_chau     DECIMAL(12,2) NOT NULL,      -- Số tiền ăn trên 1 cháu
    so_suat_an         INTEGER NOT NULL,            -- Xuất ăn trong ngày
    tien_duoc_phep_chi DECIMAL(14,2) GENERATED ALWAYS AS (tien_an_1_chau * so_suat_an) STORED,
    created_at         TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (co_so_id) REFERENCES co_so(id),
    FOREIGN KEY (nhom_chi_phi_id) REFERENCES nhom_chi_phi(id)
);
CREATE UNIQUE INDEX uq_dinhmuc ON dinh_muc_an_ngay (co_so_id, nhom_chi_phi_id, ngay);

-- ---------------------------------------------------------------------
-- 4. NHẬP KHO (mua thực phẩm/gia vị ngoài chợ)
-- ---------------------------------------------------------------------

CREATE TABLE phieu_nhap_kho (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id    INTEGER NOT NULL,
    ngay_nhap   TEXT    NOT NULL,
    so_phieu    TEXT    NULL,
    nguoi_lap   TEXT    NULL,                       -- Nhà bếp
    thu_kho     TEXT    NULL,
    ke_toan     TEXT    NULL,
    hieu_truong TEXT    NULL,
    ghi_chu     TEXT    NULL,
    created_at  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (co_so_id) REFERENCES co_so(id)
);
CREATE INDEX idx_phieunhap_ngay ON phieu_nhap_kho (co_so_id, ngay_nhap);

CREATE TABLE chi_tiet_nhap_kho (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    phieu_nhap_id  INTEGER NOT NULL,
    hang_hoa_id    INTEGER NOT NULL,
    so_luong       DECIMAL(12,4) NOT NULL,
    don_gia        DECIMAL(12,2) NOT NULL,
    thanh_tien     DECIMAL(14,2) GENERATED ALWAYS AS (so_luong * don_gia) STORED,
    FOREIGN KEY (phieu_nhap_id) REFERENCES phieu_nhap_kho(id) ON DELETE CASCADE,
    FOREIGN KEY (hang_hoa_id) REFERENCES hang_hoa(id)
);
CREATE INDEX idx_ctnhap_hang ON chi_tiet_nhap_kho (hang_hoa_id);

-- ---------------------------------------------------------------------
-- 5. XUẤT KHO (dùng chế biến trong ngày)
-- ---------------------------------------------------------------------

CREATE TABLE phieu_xuat_kho (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    co_so_id    INTEGER NOT NULL,
    ngay_xuat   TEXT    NOT NULL,
    loai_xuat   TEXT    NOT NULL
                  CHECK (loai_xuat IN ('Gia vị hằng ngày', 'Thực phẩm sáng', 'Thực phẩm chính + xế')),
    nguoi_lap   TEXT    NULL,
    thu_kho     TEXT    NULL,
    ke_toan     TEXT    NULL,
    hieu_truong TEXT    NULL,
    created_at  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (co_so_id) REFERENCES co_so(id)
);
CREATE INDEX idx_phieuxuat_ngay ON phieu_xuat_kho (co_so_id, ngay_xuat, loai_xuat);

CREATE TABLE chi_tiet_xuat_kho (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    phieu_xuat_id   INTEGER NOT NULL,
    hang_hoa_id     INTEGER NOT NULL,
    nhom_chi_phi_id INTEGER NULL,                   -- NULL nếu phiếu không tách theo nhóm (VD: Thực phẩm sáng)
    so_luong        DECIMAL(12,4) NOT NULL,
    don_gia         DECIMAL(12,2) NOT NULL,
    thanh_tien      DECIMAL(14,2) GENERATED ALWAYS AS (so_luong * don_gia) STORED,
    FOREIGN KEY (phieu_xuat_id) REFERENCES phieu_xuat_kho(id) ON DELETE CASCADE,
    FOREIGN KEY (hang_hoa_id) REFERENCES hang_hoa(id),
    FOREIGN KEY (nhom_chi_phi_id) REFERENCES nhom_chi_phi(id)
);
CREATE INDEX idx_ctxuat_hang ON chi_tiet_xuat_kho (hang_hoa_id);

-- ---------------------------------------------------------------------
-- 6. VIEW: TỒN KHO (luôn = tổng nhập - tổng xuất, không lưu bảng riêng
--    để tránh đúng lỗi lệch số liệu đã phát hiện trong file Excel gốc,
--    khi "Tổng cộng" được gõ tay thay vì tính tự động)
-- ---------------------------------------------------------------------

DROP VIEW IF EXISTS v_ton_kho;
CREATE VIEW v_ton_kho AS
SELECT
    co.id                AS co_so_id,
    co.ten_co_so,
    hh.id                AS hang_hoa_id,
    hh.ten_hang,
    dvt.ten_dvt,
    COALESCE(nk.tong_nhap, 0)               AS tong_da_nhap,
    COALESCE(xk.tong_xuat, 0)               AS tong_da_xuat,
    COALESCE(nk.tong_nhap, 0) - COALESCE(xk.tong_xuat, 0) AS ton_hien_tai
FROM co_so co
CROSS JOIN hang_hoa hh
JOIN don_vi_tinh dvt ON dvt.id = hh.dvt_id
LEFT JOIN (
    SELECT pn.co_so_id, ct.hang_hoa_id, SUM(ct.so_luong) AS tong_nhap
    FROM chi_tiet_nhap_kho ct
    JOIN phieu_nhap_kho pn ON pn.id = ct.phieu_nhap_id
    GROUP BY pn.co_so_id, ct.hang_hoa_id
) nk ON nk.co_so_id = co.id AND nk.hang_hoa_id = hh.id
LEFT JOIN (
    SELECT px.co_so_id, ct.hang_hoa_id, SUM(ct.so_luong) AS tong_xuat
    FROM chi_tiet_xuat_kho ct
    JOIN phieu_xuat_kho px ON px.id = ct.phieu_xuat_id
    GROUP BY px.co_so_id, ct.hang_hoa_id
) xk ON xk.co_so_id = co.id AND xk.hang_hoa_id = hh.id;

-- ---------------------------------------------------------------------
-- 7. DỮ LIỆU DANH MỤC KHỞI TẠO (theo đúng file Excel gốc)
-- ---------------------------------------------------------------------

INSERT INTO khoi_hoc (ten_khoi, thu_tu) VALUES
    ('Mẫu giáo', 1),
    ('Nhà trẻ', 2);

INSERT INTO nhom_chi_phi (ten_nhom) VALUES
    ('Ăn sáng'),
    ('Nhà trẻ'),
    ('Mẫu giáo');

INSERT INTO don_vi_tinh (ten_dvt) VALUES
    ('Kg'), ('kg'), ('Gói'), ('Chai'), ('Hộp'), ('Trái'), ('Quả'), ('Cái'), ('Can');

INSERT INTO co_so (ma_co_so, ten_co_so) VALUES
    ('TC', 'Trường Mầm non Bạch Dương - TC'),
    ('PH', 'Trường Mầm non Bạch Dương - PH'),
    ('ĐT', 'Trường Mầm non Bạch Dương - ĐT');
