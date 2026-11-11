-- 14_chot_theo_bang_ke_mau_2026_11.sql
-- Các sửa DB đã áp vào kho_thuc_pham_mam_non.db (đối chiếu 5 bảng kê mẫu T9 tuần 5 -> T11 tuần 1). Chạy lại vô hại.
-- Nho Ruby PH / ĐT: định mức 2.500đ/cháu (= 2.500 / 175.000)
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.014286
WHERE hang_hoa_id = 792 AND mon_an_id = (SELECT id FROM mon_an WHERE ten_mon = 'Nho Ruby');
-- Miến gà PH: trước đây thịt gà = 0 (PH không có thịt gà); bảng kê 26/10 PH có 12,2 kg / 385 trẻ
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.03169
WHERE mon_an_id = 152 AND hang_hoa_id = 30 AND ma_co_so = 'PH' AND dinh_luong = 0;
-- Món Chuối tiêu: bỏ dòng thừa "Chuối lớn 7h" = 0 (làm hiện cả 2 loại chuối)
DELETE FROM dinh_luong_nguyen_lieu WHERE mon_an_id = 77 AND hang_hoa_id = 310 AND dinh_luong = 0;
-- Cháo bồ câu củ dên: nếp hương / đậu xanh PH, ĐT tính lại theo bảng kê 27/10
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.008  WHERE mon_an_id = 7 AND hang_hoa_id = 387 AND ma_co_so = 'PH';
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.0104 WHERE mon_an_id = 7 AND hang_hoa_id = 376 AND ma_co_so = 'PH';
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.004  WHERE mon_an_id = 7 AND hang_hoa_id = 387 AND ma_co_so = 'ĐT';
UPDATE dinh_luong_nguyen_lieu_theo_co_so SET dinh_luong = 0.008  WHERE mon_an_id = 7 AND hang_hoa_id = 376 AND ma_co_so = 'ĐT';
