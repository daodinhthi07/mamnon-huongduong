-- =====================================================================
-- SỬA LẠI (LẦN 2) PHÂN LOẠI 'Gia vị' — ĐỐI CHIẾU VỚI 4 FILE THỰC ĐƠN
-- THẬT (TUẦN__1..4_THÁNG_9_*.xlsx, sheet "Chợ TC/PH/ĐT" + sheet
-- "Gia vị" định lượng theo tuần). Chạy sau 08_sua_phan_loai_gia_vi.sql
-- (và dĩ nhiên sau 05_seed_hang_hoa.sql).
--
-- VÌ SAO PHẢI SỬA LẠI:
-- Cột hang_hoa.loai_hang chỉ được code dùng cho ĐÚNG 1 việc: tách
-- bảng "2. Theo dõi xuất kho gia vị hằng ngày" ra khỏi "3. Chi tiền
-- thực phẩm sáng" / "4. Chi tiền thực phẩm ăn chính + xế" ở trang
-- Chi tiết ngày (xem ChiTietNgayController.TinhToan, hàm
-- LaGiaVi(h) => h.LoaiHang == GiaVi). Nhưng:
--   - 05_seed_hang_hoa.sql gán 'Gia vị' theo NHÓM sản phẩm của sheet
--     "Bảng giá" gốc — nhóm đó gộp chung 162 mặt hàng, phần lớn là
--     ĐỒ KHÔ/ĐỒ HỘP dùng riêng cho từng món (nui, miến, mì, bột
--     chiên/bột năng, cá hộp, xúc xích, tương ớt/cà, ruốc, tôm khô...),
--     KHÔNG PHẢI gia vị nêm nếm hằng ngày.
--   - 08_sua_phan_loai_gia_vi.sql sau đó gán thêm cả rau thơm/củ gia
--     vị TƯƠI (hành, tỏi, ớt, gừng, nghệ, ngò, sả, lá chanh, me chua,
--     dầu mè) vào 'Gia vị'.
-- Đối chiếu trực tiếp 4 file Chợ thật: mục "2. Theo dõi xuất kho gia
-- vị hằng ngày" của TẤT CẢ các ngày/tuần/cơ sở CHỈ GỒM ĐÚNG 1 nhóm cố
-- định — Gạo, Đường trắng, Dầu ăn, Muối, Nước mắm, Xì dầu, Bột
-- nêm/Hạt nêm/Bột canh (đi cùng họ bột ngọt/mì chính) — hoàn toàn
-- KHÔNG có hành/tỏi/ớt/gừng/nghệ/ngò/sả/nui/miến/mì/đồ hộp nào cả;
-- các thứ đó đều xuất hiện đúng ở mục "3"/"4" (nguyên liệu riêng của
-- từng món trong ngày). Ví dụ trực tiếp từ "Chợ TC" tuần 1 (20/07 và
-- các ngày khác): mục 2 = Gạo quê vàng, Đường trắng RS, Dầu tường an
-- Cooking, Muối iốt, Nước mắm đệ nhị, Bột nêm Knor 900gr; mục 3 cùng
-- ngày = Nui sò Safoco, Tôm loại 1, xương heo nạc, Cà rốt Đà lạt,
-- Hành Tây, Hành ngò, Hành tím.
--
-- CÁCH SỬA: đưa TOÀN BỘ hàng hóa đang là 'Gia vị' (cả phần 05 lẫn
-- phần 08 mới gán) về lại 'Thực phẩm tươi', rồi CHỈ gán lại 'Gia vị'
-- cho đúng danh sách đã đối chiếu với dữ liệu thật ở trên. Riêng
-- 'Gạo' (Gạo quê vàng...) trước đó KHÔNG hề được 05/08 gán vào 'Gia
-- vị' (đang là 'Thực phẩm tươi' mặc định) dù rõ ràng có mặt ở mục 2
-- thật của cả 4 tuần — script này bổ sung luôn.
-- =====================================================================

-- Bước 1: reset toàn bộ 'Gia vị' hiện tại về 'Thực phẩm tươi'
UPDATE hang_hoa SET loai_hang = 'Thực phẩm tươi' WHERE loai_hang = 'Gia vị';

-- Bước 2: gán lại đúng 7 nhóm gia vị/đồ khô hằng ngày, theo dữ liệu thật

-- 2a. Muối
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Muối iốt',
    'Muối đầu bếp',
    'Muối hạt'
);

-- 2b. Đường trắng (chỉ đường trắng nêm nếm hằng ngày — KHÔNG gồm đường
-- phèn/đường vàng/đường bát/đường thốt nốt/đường vàng An Khê, các loại
-- này là nguyên liệu riêng cho món chè/tráng miệng cụ thể — xem đúng
-- mục 4 thật, ví dụ "Đường phèn"/"Đường vàng" trong Chợ TC tuần 1)
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Đường trắng RS (50kg/bao)',
    'Đường trắng Biên Hòa'
);

-- 2c. Dầu ăn thường (chai/can, chiên/xào hằng ngày — KHÔNG gồm dầu
-- mè/dầu hào/dầu gấc, các loại này tạo mùi riêng cho món cụ thể — xem
-- đúng mục 3/4 thật, ví dụ "Dầu mè trắng Tường An" trong Chợ tuần 1)
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Dầu simply 1 lít', 'Dầu simply 2 lít', 'Dầu simply 5 lít',
    'Dầu tường an Cooking 1 lít', 'Dầu Tường An Cooking 2L (6 chai/thùng)', 'Dầu Tường An Cooking 5L (4 can/thùng)',
    'Dầu tường an Olita 0,4 lít', 'Dầu tường an Olita 1 lít', 'Dầu tường an Olita 2 lít', 'Dầu tường an Olita 5 lít', 'Dầu tường an Olita 25 lít',
    'Dầu neptune 1 lít', 'Dầu neptune 2 lít', 'Dầu neptune 5 lít',
    'Dầu Meizan 1L (12 chai/thùng)', 'Dầu Meizan 5L (4 can/thùng)',
    'Dầu Cái Lân 5L (4 can/thùng)'
);

-- 2d. Nước mắm
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Nước mắm đệ nhị 800ml',
    'Nước mắm Mười Thu 0.5 lít (12c/t)', 'Nước mắm Mười thu 1 lít (24c/t)',
    'Nước mắm nam ngư 500ml', 'Nước mắm nam ngư 750ml', 'Nước mắm nam ngư siêu tiết kiệm 800ml'
);

-- 2e. Xì dầu
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Xì dầu đầu bếp 4L (4 can/thùng)',
    'Xì dầu Maggi 300ml (24 chai/thùng)', 'Xì dầu Maggi 700ml (12 chai/thùng)',
    'Xì dầu Phú Sĩ 500ml (24 chai/thùng)',
    'Xì dầu Tam Thái tử nhất ca 500ml (24 chai/thùng)',
    'Xì dầu Tam Thái tử nhị ca 500ml (24 chai/ thùng, 25 chai/thùngK)'
);

-- 2f. Bột nêm / Hạt nêm / Bột canh / Mì chính (bột ngọt) — cùng họ bột
-- nêm nếm hằng ngày (đối chiếu "Bột nêm Knor 900gr" + "Bột canh gà" có
-- mặt trực tiếp ở mục 2 thật, và "Knorr"/"Bột canh gà" cũng có tên
-- trong sheet "Gia vị" định lượng theo tuần)
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Bột canh gà 200gr (50 gói/thùng)',
    'Bột nêm  chay Knor 50gr', 'Bột nêm Ajinngon 2kg', 'Hạt nêm Knorr 1.8kg ( 6gói/ thùngK)',
    'Bột nêm Knor 170gr', 'Bột nêm Knor 400gr', 'Bột nêm Knor 50gr', 'Bột nêm Knor 900gr',
    'Bột nêm magi chay 200gr', 'Bột nêm magi chay 450gr',
    'Mì chính Ajinomoto 1.8kg', 'Mì chính Ajinomoto 1kg cánh lớn', 'Mì chính Ajinomoto 1kg cánh nhỏ',
    'Mì chính Ajinomoto 400gr cánh lớn', 'Mì chính Ajinomoto 454gr cánh lớn',
    'Mì chính Aone 100gr (120g/t)', 'Mì chính Aone 454Gr (50g/t)'
);

-- 2g. Gạo — bị bỏ sót hoàn toàn ở 05/08 (đang mặc định 'Thực phẩm
-- tươi'), dù "Gạo quê vàng" có mặt trực tiếp ở mục 2 thật của cả 4 tuần
UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Gạo bồ câu (Cỏ may)',
    'Gạo dẻo',
    'Gạo quê vàng'
);

-- =====================================================================
-- Kết quả mong đợi sau khi chạy: 'Gia vị' còn đúng ~48 mặt hàng (thay
-- vì 188 trước đó) — toàn bộ rau thơm/củ gia vị tươi (hành/tỏi/ớt/
-- gừng/nghệ/ngò/sả/lá chanh/me chua/dầu mè) và toàn bộ đồ khô/đồ hộp
-- dùng riêng cho món (nui/miến/mì/bột chiên/bột năng/cá hộp/xúc
-- xích/tương ớt/tương cà/ruốc/tôm khô/mật ong/sữa/rau câu...) quay về
-- 'Thực phẩm tươi', nên sẽ hiện đúng ở mục "3. Chi tiền thực phẩm
-- sáng" / "4. Chi tiền thực phẩm ăn chính + xế" thay vì bị gộp nhầm
-- vào mục "2. Theo dõi xuất kho gia vị hằng ngày".
-- =====================================================================
