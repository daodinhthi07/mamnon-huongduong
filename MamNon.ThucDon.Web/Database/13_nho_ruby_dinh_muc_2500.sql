-- 13_nho_ruby_dinh_muc_2500.sql
-- Nho Ruby (tráng miệng): ĐỊNH MỨC 2.500 đ/cháu -> định lượng = 2.500 / đơn giá nho đỏ không hạt (kg/suất).
-- Với đơn giá 175.000 đ/kg: 2.500 / 175.000 = 0,014286 kg/suất. Khi đơn giá đổi, chạy lại script này để tính lại.
UPDATE dinh_luong_nguyen_lieu
SET dinh_luong = ROUND(2500.0 / (SELECT don_gia_gan_nhat FROM hang_hoa WHERE id = 792), 6)
WHERE hang_hoa_id = 792
  AND mon_an_id = (SELECT id FROM mon_an WHERE ten_mon = 'Nho Ruby');

UPDATE mon_an
SET ghi_chu = 'Định mức 2.500 đ/cháu (= 2.500 / đơn giá nho đỏ không hạt)'
WHERE ten_mon = 'Nho Ruby';
