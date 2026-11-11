-- =====================================================================================
-- 12_reset_va_nap_mon_sang.sql
-- XOÁ dữ liệu món ăn cũ (đang lẫn lộn) rồi NẠP LẠI: món ăn SÁNG + nguyên liệu/số lượng mỗi suất
-- + định mức GIA VỊ ăn sáng — tổng hợp từ 5 file Excel TUẦN 1..5 THÁNG 9 (sheet Chợ TC/PH/ĐT).
--
-- BƯỚC 0 (BẮT BUỘC): SAO LƯU file kho_thuc_pham_mam_non.db trước khi chạy.
--
-- SẼ XOÁ:   thuc_don (thực đơn đã chọn theo ngày) · dinh_luong_nguyen_lieu · mon_an (TẤT CẢ món)
--           · dinh_muc_gia_vi_chuan của nhóm "Ăn sáng" và "Ăn sáng - Nhà trẻ"
--           · dinh_muc_an_ngay của nhóm "Ăn sáng - Nhà trẻ" (nhóm đã bỏ — dùng chung 1 sĩ số ăn sáng)
-- GIỮ NGUYÊN: hang_hoa + giá, đơn vị tính, cơ sở, khối, loại bữa, người dùng, phiếu nhập/xuất kho,
--           sĩ số/tiền ăn từng ngày (dinh_muc_an_ngay), định mức gia vị của "Nhà trẻ" và "Mẫu giáo".
-- SAU KHI CHẠY: chỉ còn các món bữa SÁNG — các bữa Canh/Mặn/Xào/Tráng miệng/Xế sẽ trống cho đến
--           khi nạp tiếp.
-- Chạy trong 1 transaction: lỗi ở bất kỳ bước nào thì không thay đổi gì.
-- =====================================================================================
PRAGMA foreign_keys = ON;
BEGIN TRANSACTION;

-- ---------- 1) XOÁ ----------
DELETE FROM thuc_don;
DELETE FROM dinh_luong_nguyen_lieu;
DELETE FROM mon_an;
DELETE FROM dinh_muc_gia_vi_chuan
 WHERE nhom_chi_phi_id IN (SELECT id FROM nhom_chi_phi WHERE ten_nhom IN ('Ăn sáng', 'Ăn sáng - Nhà trẻ'));
DELETE FROM dinh_muc_an_ngay
 WHERE nhom_chi_phi_id IN (SELECT id FROM nhom_chi_phi WHERE ten_nhom = 'Ăn sáng - Nhà trẻ');
DELETE FROM sqlite_sequence WHERE name IN ('mon_an', 'dinh_luong_nguyen_lieu');

-- ---------- 2) DỮ LIỆU TẠM: định lượng mỗi suất của từng món sáng ----------
-- (ten_mon, ten_hang, dvt, don_gia_tham_khao, dinh_luong cho 1 suất theo ĐVT của hàng hoá)
CREATE TEMP TABLE stg_dl (ten_mon TEXT, ten_hang TEXT, dvt TEXT, don_gia REAL, dinh_luong REAL);
INSERT INTO stg_dl VALUES
  ('Bánh canh cá bã trầu', 'cá bã tràu trụi', 'kg', 210000.0, 0.027541),
  ('Bánh canh cá bã trầu', 'Chả quết heo', 'kg', 190000.0, 0.013115),
  ('Bánh canh cá bã trầu', 'Bột mì sợi cắt 5 phân', 'kg', 30000.0, 0.029836),
  ('Bánh canh cá bã trầu', 'Mỡ heo', 'kg', 75000.0, 0.004098),
  ('Bánh canh cá bã trầu', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.004918),
  ('Bánh canh cá bã trầu', 'Hành tím', 'Kg', 75000.0, 0.001148),
  ('Bánh canh cá bã trầu', 'Hành ngò', 'Kg', 50000.0, 0.001148),
  ('Bánh canh cá khế', 'Cá khế  y con', 'kg', 220000.0, 0.027632),
  ('Bánh canh cá khế', 'Chả quết heo', 'kg', 190000.0, 0.011842),
  ('Bánh canh cá khế', 'Bột mì sợi cắt 3 phân', 'kg', 30000.0, 0.032895),
  ('Bánh canh cá khế', 'Mỡ heo', 'kg', 75000.0, 0.002632),
  ('Bánh canh cá khế', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.004211),
  ('Bánh canh cá khế', 'Hành tím', 'Kg', 75000.0, 0.001053),
  ('Bánh canh tôm, thịt', 'Tôm loại 1', 'kg', 230000.0, 0.02512),
  ('Bánh canh tôm, thịt', 'xương heo nạc', 'kg', 110000.0, 0.02112),
  ('Bánh canh tôm, thịt', 'Bột mì sợi cắt 3 phân', 'kg', 30000.0, 0.032),
  ('Bánh canh tôm, thịt', 'Thịt nạc vai', 'Kg', 140000.0, 0.00352),
  ('Bánh canh tôm, thịt', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.0048),
  ('Bánh canh tôm, thịt', 'Hành tím', 'Kg', 75000.0, 0.0008),
  ('Bánh canh tôm, thịt', 'Hành ngò', 'Kg', 50000.0, 0.00048),
  ('Bánh canh xương chả', 'xương heo nạc', 'kg', 110000.0, 0.028857),
  ('Bánh canh xương chả', 'Chả quết heo', 'kg', 190000.0, 0.016),
  ('Bánh canh xương chả', 'Chả bò đòn', 'Kg', 220000.0, 0.007143),
  ('Bánh canh xương chả', 'Bột mì sợi cắt 5 phân', 'kg', 30000.0, 0.034857),
  ('Bánh canh xương chả', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.008571),
  ('Bánh canh xương chả', 'Bột năng không bao bì', 'Kg', 35000.0, 0.005143),
  ('Bánh canh xương chả', 'Hành Tây', 'Kg', 25000.0, 0.004857),
  ('Bánh canh xương chả', 'Hành tím', 'Kg', 75000.0, 0.001429),
  ('Bánh canh xương chả', 'Hành ngò', 'Kg', 50000.0, 0.001429),
  ('Bún sườn', 'Sườn non', 'Kg', 195000.0, 0.028033),
  ('Bún sườn', 'Bún tươi 5h', 'kg', 12000.0, 0.12623),
  ('Bún sườn', 'Huyết bò', 'Kg', 60000.0, 0.019836),
  ('Bún sườn', 'Cà chua', 'Kg', 42000.0, 0.010984),
  ('Bún sườn', 'Thịt bò phô,phi lê', 'Kg', 280000.0, 0.001639),
  ('Bún sườn', 'Thơm không cùi', 'Kg', 34000.0, 0.010656),
  ('Bún sườn', 'Sả', 'Kg', 24000.0, 0.007377),
  ('Bún sườn', 'Hành tím', 'Kg', 75000.0, 0.001148),
  ('Bún sườn', 'Hành ngò', 'Kg', 50000.0, 0.001148),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Thịt bò sườn lọc mỡ', 'Kg', 230000.0, 0.023443),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Đậu hà lan tươi', 'Kg', 320000.0, 0.009344),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008197),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Bí đỏ', 'Kg', 23000.0, 0.011967),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Nếp hương', 'Kg', 32000.0, 0.006885),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Hành tím', 'Kg', 75000.0, 0.001148),
  ('Cháo bò bí đỏ, đậu Hà Lan', 'Hành ngò', 'Kg', 50000.0, 0.001148),
  ('Cháo bồ câu củ dên', 'Bồ câu làm sạch', 'Con', 90000.0, 0.0896),
  ('Cháo bồ câu củ dên', 'Đậu xanh XK không vỏ', 'Kg', 56000.0, 0.00768),
  ('Cháo bồ câu củ dên', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008),
  ('Cháo bồ câu củ dên', 'Nếp hương', 'Kg', 32000.0, 0.008),
  ('Cháo bồ câu củ dên', 'Củ dền', 'Kg', 40000.0, 0.0048),
  ('Cháo bồ câu củ dên', 'Hành tím', 'Kg', 75000.0, 0.00112),
  ('Cháo bồ câu củ dên', 'Hành ngò', 'Kg', 50000.0, 0.00064),
  ('Cháo lươn khoai môn', 'Lươn làm sạch', 'kg', 260000.0, 0.027302),
  ('Cháo lươn khoai môn', 'Khoai môn tàu', 'Kg', 50000.0, 0.031587),
  ('Cháo lươn khoai môn', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.009524),
  ('Cháo lươn khoai môn', 'Nếp hương', 'Kg', 32000.0, 0.006825),
  ('Cháo lươn khoai môn', 'Hành tím', 'Kg', 75000.0, 0.000952),
  ('Cháo lươn khoai môn', 'Hành ngò', 'Kg', 50000.0, 0.000317),
  ('Cháo nấm', 'Nấm rơm tươi (ng 14+15+30+1 âm lịch)', 'kg', 260000.0, 0.027227),
  ('Cháo nấm', 'Dầu gấc Vio 200ml (250ml)', 'chai', 50000.0, 0.011765),
  ('Cháo nấm', 'Đậu xanh XK không vỏ', 'Kg', 56000.0, 0.009244),
  ('Cháo nấm', 'Khoai tây Đà lạt', 'Kg', 45000.0, 0.010084),
  ('Cháo nấm', 'Nếp hương', 'Kg', 32000.0, 0.009244),
  ('Cháo nấm', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.007899),
  ('Cháo nấm', 'Hành tím', 'Kg', 75000.0, 0.001176),
  ('Cháo nấm', 'Hành ngò', 'Kg', 50000.0, 0.000672),
  ('Cháo thịt bò cà rốt, khoai tây', 'Thịt bò phô,phi lê', 'Kg', 280000.0, 0.028571),
  ('Cháo thịt bò cà rốt, khoai tây', 'Khoai tây Đà lạt', 'Kg', 45000.0, 0.008929),
  ('Cháo thịt bò cà rốt, khoai tây', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.008929),
  ('Cháo thịt bò cà rốt, khoai tây', 'Nếp hương', 'Kg', 32000.0, 0.008929),
  ('Cháo thịt bò cà rốt, khoai tây', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.007143),
  ('Cháo thịt bò cà rốt, khoai tây', 'Hành tím', 'Kg', 75000.0, 0.001071),
  ('Cháo thịt bò cà rốt, khoai tây', 'Hành ngò', 'Kg', 50000.0, 0.001071),
  ('Cháo tôm rim', 'Tôm loại 1', 'kg', 230000.0, 0.03472),
  ('Cháo tôm rim', 'Gia vị kho thịt Cholimex 50gr (10 gói / hộp, 12 hộp/ thùng)', 'Gói', 10000.0, 0.0384),
  ('Cháo tôm rim', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008),
  ('Cháo tôm rim', 'Nếp hương', 'Kg', 32000.0, 0.00832),
  ('Cháo tôm rim', 'Lá dứa', 'Kg', 60000.0, 0.00176),
  ('Cháo tôm rim', 'Hành tím', 'Kg', 75000.0, 0.00128),
  ('Cháo tôm rim', 'Nước mắm đệ nhị 800ml', 'Chai', 23000.0, 0.00352),
  ('Cháo tôm rim', 'Hành ngò', 'Kg', 50000.0, 0.00112),
  ('Cháo tôm rim', 'Đường trắng RS (50kg/bao)', 'kg', 27000.0, 0.00192),
  ('Miến gà, chả quết', 'Thịt gà tam hoàng (ko đầu , cánh)', 'Kg', 120000.0, 0.034711),
  ('Miến gà, chả quết', 'Miến sợi to Hàn quốc', 'Kg', 110000.0, 0.026446),
  ('Miến gà, chả quết', 'Chả quết heo', 'kg', 190000.0, 0.010744),
  ('Miến gà, chả quết', 'Thịt ức gà', 'Kg', 100000.0, 0.003306),
  ('Miến gà, chả quết', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.004132),
  ('Miến gà, chả quết', 'Hành Tây', 'Kg', 25000.0, 0.003967),
  ('Miến gà, chả quết', 'Hành tím', 'Kg', 75000.0, 0.000826),
  ('Miến gà, chả quết', 'Hành ngò', 'Kg', 50000.0, 0.000496),
  ('Miến nấu tôm, thịt', 'Tôm loại 1', 'kg', 230000.0, 0.015635),
  ('Miến nấu tôm, thịt', 'Miến sợi to Hàn quốc', 'Kg', 110000.0, 0.026059),
  ('Miến nấu tôm, thịt', 'xương heo nạc', 'kg', 110000.0, 0.024756),
  ('Miến nấu tôm, thịt', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.008143),
  ('Miến nấu tôm, thịt', 'Hành tím', 'Kg', 75000.0, 0.001629),
  ('Miến nấu tôm, thịt', 'Hành ngò', 'Kg', 50000.0, 0.001303),
  ('Miến tôm chả', 'Tôm loại 1', 'kg', 230000.0, 0.016393),
  ('Miến tôm chả', 'Chả quết heo', 'kg', 190000.0, 0.014754),
  ('Miến tôm chả', 'Miến sợi to Hàn quốc', 'Kg', 110000.0, 0.02459),
  ('Miến tôm chả', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008197),
  ('Miến tôm chả', 'Hành tím', 'Kg', 75000.0, 0.001148),
  ('Miến tôm chả', 'Hành ngò', 'Kg', 50000.0, 0.001311),
  ('Miến tôm chả', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.001148),
  ('Miến tôm, thịt', 'Tôm loại 1', 'kg', 230000.0, 0.0192),
  ('Miến tôm, thịt', 'xương heo nạc', 'kg', 110000.0, 0.02176),
  ('Miến tôm, thịt', 'Miến sợi to Hàn quốc', 'Kg', 110000.0, 0.0208),
  ('Miến tôm, thịt', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008),
  ('Miến tôm, thịt', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.00704),
  ('Miến tôm, thịt', 'Hành tím', 'Kg', 75000.0, 0.00112),
  ('Miến tôm, thịt', 'Hành ngò', 'Kg', 50000.0, 0.00112),
  ('Mỳ quảng', 'Nấm rơm tươi (ng 14+15+30+1 âm lịch)', 'kg', 260000.0, 0.022459),
  ('Mỳ quảng', 'Mỳ quảng tươi 5h', 'kg', 13000.0, 0.101639),
  ('Mỳ quảng', 'Khoai môn tàu', 'Kg', 50000.0, 0.01541),
  ('Mỳ quảng', 'Đậu khuôn chiên (kg)', 'kg', 55000.0, 0.013115),
  ('Mỳ quảng', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.013115),
  ('Mỳ quảng', 'Cà chua', 'Kg', 42000.0, 0.007705),
  ('Mỳ quảng', 'Chao Đại Bình Dương 750g (3 hủ/lốc, 12 hủ/thùng)', 'Hũ', 53000.0, 0.003279),
  ('Mỳ quảng', 'Hành tím', 'Kg', 75000.0, 0.001148),
  ('Mỳ quảng', 'Hành ngò', 'Kg', 50000.0, 0.001148),
  ('Mỳ quảng', 'Chao nhỏ Mikiirii 180gr (12hũ/ lốc)', 'Hũ', 22000.0, 0.001639),
  ('Nui nấu thịt bò', 'Thịt bò phô,phi lê', 'Kg', 280000.0, 0.026349),
  ('Nui nấu thịt bò', 'Nui sò Safoco 500gr (18 gói/thùng)', 'Gói', 40000.0, 0.050794),
  ('Nui nấu thịt bò', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.006508),
  ('Nui nấu thịt bò', 'Hành Tây', 'Kg', 25000.0, 0.003968),
  ('Nui nấu thịt bò', 'Hành tím', 'Kg', 75000.0, 0.000952),
  ('Nui nấu thịt bò', 'Hành ngò', 'Kg', 50000.0, 0.000317),
  ('Nui nấu tôm, thịt', 'Tôm loại 1', 'kg', 230000.0, 0.019643),
  ('Nui nấu tôm, thịt', 'xương heo nạc', 'kg', 110000.0, 0.026964),
  ('Nui nấu tôm, thịt', 'Nui sò Safoco 500gr (18 gói/thùng)', 'Gói', 40000.0, 0.046429),
  ('Nui nấu tôm, thịt', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.008214),
  ('Nui nấu tôm, thịt', 'Hành tím', 'Kg', 75000.0, 0.001071),
  ('Nui nấu tôm, thịt', 'Hành ngò', 'Kg', 50000.0, 0.001071),
  ('Nui sò nấu tôm', 'Tôm loại 1', 'kg', 230000.0, 0.03216),
  ('Nui sò nấu tôm', 'Nui sò Safoco 500gr (18 gói/thùng)', 'Gói', 40000.0, 0.0464),
  ('Nui sò nấu tôm', 'Mỡ heo', 'kg', 75000.0, 0.00352),
  ('Nui sò nấu tôm', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.0048),
  ('Nui sò nấu tôm', 'Hành tím', 'Kg', 75000.0, 0.00112),
  ('Nui sò nấu tôm', 'Hành ngò', 'Kg', 50000.0, 0.00064),
  ('Nui sò nấu xương, chả', 'Chả quết heo', 'kg', 190000.0, 0.022308),
  ('Nui sò nấu xương, chả', 'xương heo nạc', 'kg', 110000.0, 0.024359),
  ('Nui sò nấu xương, chả', 'Nui sò Safoco 500gr (18 gói/thùng)', 'Gói', 40000.0, 0.053846),
  ('Nui sò nấu xương, chả', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.006667),
  ('Nui sò nấu xương, chả', 'Thịt nạc vai', 'Kg', 140000.0, 0.001282),
  ('Nui sò nấu xương, chả', 'Hành tím', 'Kg', 75000.0, 0.001282),
  ('Nui sò nấu xương, chả', 'Hành ngò', 'Kg', 50000.0, 0.001282),
  ('Phở gà', 'Thịt gà tam hoàng', 'kg', 110000.0, 0.064127),
  ('Phở gà', 'Phở tươi 5h', 'kg', 14000.0, 0.109524),
  ('Phở gà', 'Thịt ức gà', 'Kg', 100000.0, 0.008254),
  ('Phở gà', 'Hoa hồi, quế', 'kg', 250000.0, 0.000794),
  ('Phở gà', 'Hành Tây', 'Kg', 25000.0, 0.004921),
  ('Phở gà', 'Hành tím', 'Kg', 75000.0, 0.000794),
  ('Phở gà', 'Hành ngò', 'Kg', 50000.0, 0.000317),
  ('Súp bắp gà, chả quết, rau củ', 'Thịt gà tam hoàng', 'kg', 110000.0, 0.042373),
  ('Súp bắp gà, chả quết, rau củ', 'Chả quết heo', 'kg', 190000.0, 0.011864),
  ('Súp bắp gà, chả quết, rau củ', 'Bắp Mỹ (quả)', 'Quả', 15000.0, 0.061017),
  ('Súp bắp gà, chả quết, rau củ', 'Bột bán (10 kg/bao)', 'kg', 37000.0, 0.022034),
  ('Súp bắp gà, chả quết, rau củ', 'Trứng gà kg', 'kg', 65000.0, 0.009322),
  ('Súp bắp gà, chả quết, rau củ', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.007627),
  ('Súp bắp gà, chả quết, rau củ', 'Hành tím', 'Kg', 75000.0, 0.001186),
  ('Súp bắp gà, chả quết, rau củ', 'Hành Tây', 'Kg', 25000.0, 0.002542),
  ('Súp bắp gà, chả quết, rau củ', 'Hành ngò', 'Kg', 50000.0, 0.001186),
  ('Súp bắp gà, chả quết, rau củ', 'Bột bắp 150gr (40g/1 thùng)', 'Gói', 11000.0, 0.003729),
  ('Súp cua', 'Thanh cua Surimi 1kg (20 gói/ thùng)', 'Gói', 190000.0, 0.013223),
  ('Súp cua', 'Chả quết heo', 'kg', 190000.0, 0.01157),
  ('Súp cua', 'Thịt nạc vai', 'Kg', 140000.0, 0.010083),
  ('Súp cua', 'xương heo nạc', 'kg', 110000.0, 0.009256),
  ('Súp cua', 'Trứng gà kg', 'kg', 65000.0, 0.011074),
  ('Súp cua', 'Bắp lon xay', 'Lon', 18000.0, 0.026446),
  ('Súp cua', 'Bột bán (10 kg/bao)', 'kg', 37000.0, 0.01157),
  ('Súp cua', 'Bột năng không bao bì', 'Kg', 33000.0, 0.011901),
  ('Súp cua', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.00843),
  ('Súp cua', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.006612),
  ('Súp cua', 'Hành tím', 'Kg', 75000.0, 0.000826),
  ('Súp cua', 'Ngò (mùi)', 'kg', 60000.0, 0.000496),
  ('Súp thập cẩm', 'Óc heo', 'Bộ', 40000.0, 0.054098),
  ('Súp thập cẩm', 'Tôm loại 1', 'kg', 230000.0, 0.008197),
  ('Súp thập cẩm', 'Chả quết heo', 'kg', 190000.0, 0.009016),
  ('Súp thập cẩm', 'xương heo nạc', 'kg', 110000.0, 0.013607),
  ('Súp thập cẩm', 'Trứng gà kg', 'kg', 65000.0, 0.008197),
  ('Súp thập cẩm', 'Bắp lon xay', 'Lon', 18000.0, 0.02623),
  ('Súp thập cẩm', 'Bột bán (10 kg/bao)', 'kg', 37000.0, 0.009836),
  ('Súp thập cẩm', 'Bột năng không bao bì', 'Kg', 33000.0, 0.010164),
  ('Súp thập cẩm', 'Dầu mè trắng Tường An 250ml (12 chai/thùng)', 'Chai', 38000.0, 0.008197),
  ('Súp thập cẩm', 'Thịt vai', 'Kg', 140000.0, 0.001639),
  ('Súp thập cẩm', 'Cà rốt Đà lạt', 'Kg', 33000.0, 0.006066),
  ('Súp thập cẩm', 'Hành tím', 'Kg', 75000.0, 0.000984),
  ('Súp thập cẩm', 'Hành ngò', 'Kg', 50000.0, 0.000328);

-- ---------- 3) DỮ LIỆU TẠM: định mức GIA VỊ ăn sáng (lượng / 1 suất) ----------
CREATE TEMP TABLE stg_gv (ten_hang TEXT, dvt TEXT, don_gia REAL, dinh_luong REAL);
INSERT INTO stg_gv VALUES
  ('Gạo bồ câu (Cỏ may)', 'Kg', 21000.0, 0.006098),
  ('Đường trắng RS (50kg/bao)', 'kg', 27000.0, 0.000854),
  ('Dầu simply 1 lít', 'chai', 75000.0, 0.000899),
  ('Muối iốt 500g (0.5kg/gói; 100 gói/bao)', 'Gói', 4000.0, 0.002115),
  ('Nước mắm đệ nhị 800ml', 'Chai', 23000.0, 0.001082),
  ('Bột nêm Knor 900gr', 'Gói', 97000.0, 0.000473),
  ('Bột canh gà 200gr (50 gói/thùng)', 'Gói', 4200.0, 0.000838),
  ('Xì dầu Maggi 700ml (12 chai/thùng)', 'Chai', 32000.0, 0.001143);

-- ---------- 4) Hàng hoá chưa có trong danh mục thì tạo mới (cùng ĐVT/giá trong Excel) ----------
INSERT INTO don_vi_tinh (ten_dvt)
SELECT DISTINCT x.dvt FROM (SELECT dvt FROM stg_dl UNION SELECT dvt FROM stg_gv) x
 WHERE NOT EXISTS (SELECT 1 FROM don_vi_tinh d WHERE lower(d.ten_dvt) = lower(x.dvt));

INSERT INTO hang_hoa (ma_hang, ten_hang, dvt_id, loai_hang, don_gia_gan_nhat, is_active)
SELECT NULL, x.ten_hang,
       (SELECT d.id FROM don_vi_tinh d WHERE lower(d.ten_dvt) = lower(x.dvt) ORDER BY d.id LIMIT 1),
       'Thực phẩm tươi', x.don_gia, 1
  FROM (SELECT ten_hang, MAX(dvt) AS dvt, MAX(don_gia) AS don_gia FROM
          (SELECT ten_hang, dvt, don_gia FROM stg_dl UNION ALL SELECT ten_hang, dvt, don_gia FROM stg_gv)
         GROUP BY ten_hang) x
 WHERE NOT EXISTS (SELECT 1 FROM hang_hoa h WHERE h.ten_hang = x.ten_hang);

-- ---------- 5) NẠP món ăn bữa SÁNG + định lượng ----------
INSERT INTO mon_an (ten_mon, ghi_chu, is_active, nhom_bua_an_id)
SELECT DISTINCT ten_mon, 'Tổng hợp từ file Excel TUẦN 1-5 THÁNG 9', 1,
       (SELECT id FROM nhom_bua_an WHERE ten_nhom = 'Sáng')
  FROM stg_dl ORDER BY ten_mon;

INSERT INTO dinh_luong_nguyen_lieu (mon_an_id, hang_hoa_id, dinh_luong)
SELECT m.id, (SELECT h.id FROM hang_hoa h WHERE h.ten_hang = s.ten_hang ORDER BY h.id LIMIT 1), s.dinh_luong
  FROM stg_dl s JOIN mon_an m ON m.ten_mon = s.ten_mon;

-- ---------- 6) NẠP định mức GIA VỊ nhóm "Ăn sáng" ----------
INSERT INTO dinh_muc_gia_vi_chuan (hang_hoa_id, nhom_chi_phi_id, dinh_luong_1_suat)
SELECT (SELECT h.id FROM hang_hoa h WHERE h.ten_hang = s.ten_hang ORDER BY h.id LIMIT 1),
       (SELECT id FROM nhom_chi_phi WHERE ten_nhom = 'Ăn sáng'), s.dinh_luong
  FROM stg_gv s;

DROP TABLE stg_dl;
DROP TABLE stg_gv;

COMMIT;

-- ---------- Kiểm tra nhanh sau khi chạy ----------
-- SELECT COUNT(*) AS so_mon FROM mon_an;                       -- kỳ vọng: 24
-- SELECT COUNT(*) AS so_dinh_luong FROM dinh_luong_nguyen_lieu; -- kỳ vọng: 187
-- SELECT COUNT(*) AS so_gia_vi_an_sang FROM dinh_muc_gia_vi_chuan
--  WHERE nhom_chi_phi_id = (SELECT id FROM nhom_chi_phi WHERE ten_nhom = 'Ăn sáng'); -- kỳ vọng: 8
