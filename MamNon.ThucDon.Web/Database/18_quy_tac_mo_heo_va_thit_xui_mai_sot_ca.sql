-- 18: Quy tắc nguyên liệu mới
--   1) Mỡ heo (hàng 12): món nào có mỡ heo thì cố định  TC, PH = 0,5 kg ; ĐT = 0,2 kg  (sáng + Mẫu giáo; Nhà trẻ = 0)
--   2) Món "Thịt xúi mại sốt cà": quy tắc THEO MÓN (cột mon_an_id)
--        TC, PH : bánh mì ổ lớn Quốc Doanh (558) 8 ổ, cà chua (135) 4 kg, cà chua lon (435) 3 lon
--        ĐT     : bánh mì ổ lớn Quốc Doanh (558) 3 ổ, cà chua (135) 0,7 kg, cà chua lon (435) 1 lon
-- Ứng dụng tự chạy bước này lúc khởi động (QuyTacNguyenLieuInitializer) — file này chỉ để chạy tay trên DB khác. Chạy lại vô hại.
-- Nếu cột mon_an_id chưa có, chạy trước (chỉ 1 lần):
--   ALTER TABLE quy_tac_nguyen_lieu ADD COLUMN mon_an_id INTEGER NULL REFERENCES mon_an(id) ON DELETE CASCADE;

-- 1) Mỡ heo (quy tắc theo mặt hàng)
INSERT INTO quy_tac_nguyen_lieu (hang_hoa_id, mon_an_id, ma_co_so, loai_ngay, che_do, sl_an_sang, sl_nha_tre, sl_mau_giao, khong_dung, ghi_chu, is_active)
SELECT 12, NULL, 'TC,PH', 0, 1, 0.5, 0, 0.5, 0, 'Mỡ heo cố định 0,5 kg (TC, PH)', 1
WHERE EXISTS (SELECT 1 FROM hang_hoa WHERE id = 12)
  AND NOT EXISTS (SELECT 1 FROM quy_tac_nguyen_lieu WHERE hang_hoa_id = 12 AND mon_an_id IS NULL AND ma_co_so = 'TC,PH' AND che_do = 1);
INSERT INTO quy_tac_nguyen_lieu (hang_hoa_id, mon_an_id, ma_co_so, loai_ngay, che_do, sl_an_sang, sl_nha_tre, sl_mau_giao, khong_dung, ghi_chu, is_active)
SELECT 12, NULL, 'ĐT', 0, 1, 0.2, 0, 0.2, 0, 'Mỡ heo cố định 0,2 kg (ĐT)', 1
WHERE EXISTS (SELECT 1 FROM hang_hoa WHERE id = 12)
  AND NOT EXISTS (SELECT 1 FROM quy_tac_nguyen_lieu WHERE hang_hoa_id = 12 AND mon_an_id IS NULL AND ma_co_so = 'ĐT' AND che_do = 1);

-- 2) Thịt xúi mại sốt cà (quy tắc theo món) — số đặt ở cột Mẫu giáo (số tổng của chợ)
WITH v(hang, co_so, sl) AS (VALUES
  (558, 'TC,PH', 8), (135, 'TC,PH', 4), (435, 'TC,PH', 3),
  (558, 'ĐT',    3), (135, 'ĐT',    0.7), (435, 'ĐT',    1))
INSERT INTO quy_tac_nguyen_lieu (hang_hoa_id, mon_an_id, ma_co_so, loai_ngay, che_do, sl_an_sang, sl_nha_tre, sl_mau_giao, khong_dung, ghi_chu, is_active)
SELECT v.hang, m.id, v.co_so, 0, 1, 0, 0, v.sl, 0, 'Thịt xúi mại sốt cà — cố định theo món', 1
FROM v JOIN mon_an m ON m.ten_mon = 'Thịt xúi mại sốt cà'
WHERE EXISTS (SELECT 1 FROM hang_hoa h WHERE h.id = v.hang)
  AND NOT EXISTS (SELECT 1 FROM quy_tac_nguyen_lieu q WHERE q.hang_hoa_id = v.hang AND q.mon_an_id = m.id AND q.ma_co_so = v.co_so AND q.che_do = 1);

-- 3) Cháo nấm (bữa SÁNG, cột ăn sáng): TC,PH đậu ngự khô (369) 2 kg, cà rốt Đà lạt (138) 4 kg ; ĐT 0,5 kg / 1 kg
WITH v(hang, co_so, sl) AS (VALUES
  (369, 'TC,PH', 2), (138, 'TC,PH', 4), (369, 'ĐT', 0.5), (138, 'ĐT', 1))
INSERT INTO quy_tac_nguyen_lieu (hang_hoa_id, mon_an_id, ma_co_so, loai_ngay, che_do, sl_an_sang, sl_nha_tre, sl_mau_giao, khong_dung, ghi_chu, is_active)
SELECT v.hang, m.id, v.co_so, 0, 1, v.sl, 0, 0, 0, 'Cháo nấm — cố định theo món', 1
FROM v JOIN mon_an m ON m.ten_mon = 'Cháo nấm'
WHERE EXISTS (SELECT 1 FROM hang_hoa h WHERE h.id = v.hang)
  AND NOT EXISTS (SELECT 1 FROM quy_tac_nguyen_lieu q WHERE q.hang_hoa_id = v.hang AND q.mon_an_id = m.id AND q.ma_co_so = v.co_so AND q.che_do = 1);
