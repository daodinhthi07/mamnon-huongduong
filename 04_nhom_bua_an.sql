-- =====================================================================
-- BỔ SUNG: NHÓM BỮA ĂN (dùng để LỌC DROPDOWN chọn món theo đúng loại bữa)
-- Chạy sau 01/02/03_*.sql
--
-- Bài toán: khi soạn thực đơn, dòng "CANH" không được phép hiện món
-- "Tráng miệng"/"Xế" trong danh sách chọn — người soạn dễ chọn nhầm nếu
-- dropdown gộp chung mọi món. Giải pháp: tạo 1 danh mục "nhóm bữa" DÙNG
-- CHUNG CHO MỌI KHỐI (Sáng/Phụ sáng/Trưa/Canh/Mặn/Xào/Tráng miệng/Xế/
-- Chính chiều — đúng 9 giá trị loai_bua_an.ten_bua đang có ở cả 3 khối),
-- rồi gắn mỗi loai_bua_an VÀ mỗi mon_an vào đúng 1 nhóm. Nhờ vậy 1 món
-- "Canh" tạo 1 lần dùng chung được cho "CANH" của cả Mẫu giáo lẫn
-- Nhà trẻ (18-24T), không phải tạo trùng theo từng khối.
-- =====================================================================

PRAGMA foreign_keys = ON;

CREATE TABLE nhom_bua_an (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ten_nhom  TEXT    NOT NULL,
    thu_tu    INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX uq_nhombuaan_ten ON nhom_bua_an (ten_nhom);

INSERT INTO nhom_bua_an (ten_nhom, thu_tu) VALUES
    ('Sáng', 1),
    ('Phụ sáng', 2),
    ('Trưa', 3),
    ('Canh', 4),
    ('Mặn', 5),
    ('Xào', 6),
    ('Tráng miệng', 7),
    ('Xế', 8),
    ('Chính chiều', 9);

-- Lưu ý SQLite: ALTER TABLE ADD COLUMN không hỗ trợ thêm NOT NULL khi
-- bảng đã có dữ liệu và không có DEFAULT hằng số phù hợp -> để NULL,
-- UPDATE gán giá trị ngay sau đó (giống cách làm với hang_muc_id ở
-- file 03). Tầng ứng dụng (C#) sẽ ép buộc "phải chọn" khi tạo/sửa món.
ALTER TABLE loai_bua_an ADD COLUMN nhom_bua_an_id INTEGER NULL REFERENCES nhom_bua_an(id);
ALTER TABLE mon_an      ADD COLUMN nhom_bua_an_id INTEGER NULL REFERENCES nhom_bua_an(id);

-- Gắn nhóm bữa cho toàn bộ loai_bua_an hiện có (áp dụng chung cho cả 3 khối).
-- Dùng CASE tường minh thay vì so chuỗi hoa/thường trực tiếp — cùng lý do
-- đã ghi chú ở file 03 (SQLite không tự nhận diện chữ Việt có dấu qua
-- COLLATE NOCASE).
UPDATE loai_bua_an
SET nhom_bua_an_id = (
    SELECT nb.id FROM nhom_bua_an nb
    WHERE nb.ten_nhom = CASE loai_bua_an.ten_bua
        WHEN 'SÁNG'         THEN 'Sáng'
        WHEN 'PHỤ SÁNG'     THEN 'Phụ sáng'
        WHEN 'TRƯA'         THEN 'Trưa'
        WHEN 'CANH'         THEN 'Canh'
        WHEN 'MẶN'          THEN 'Mặn'
        WHEN 'XÀO'          THEN 'Xào'
        WHEN 'TRÁNG MIỆNG'  THEN 'Tráng miệng'
        WHEN 'XẾ'           THEN 'Xế'
        WHEN 'CHÍNH CHIỀU'  THEN 'Chính chiều'
    END
);
