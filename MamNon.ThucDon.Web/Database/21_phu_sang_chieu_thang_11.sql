-- Phụ sáng / Phụ chiều THÁNG 11/2026 (4 tuần: 2/11, 9/11, 16/11, 23/11) lấy từ file THÁNG_11-2026.xlsx.
-- Đã sửa lỗi chính tả: 'Yakul' -> 'Yakult'. Ngày nghỉ (Thứ 7 28/11) để trống. Chạy lại nhiều lần vô hại (INSERT OR REPLACE).
BEGIN TRANSACTION;
CREATE TABLE IF NOT EXISTS cong_khai_phu (tuan_bat_dau TEXT NOT NULL, ngay INTEGER NOT NULL, phu_sang TEXT NULL, phu_chieu TEXT NULL, PRIMARY KEY (tuan_bat_dau, ngay));
-- Tuần 2026-11-02
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 0, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 1, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 2, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 3, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 4, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-02', 5, 'Sữa chua', 'Sữa Ellac Family');
-- Tuần 2026-11-09
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 0, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 1, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 2, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 3, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 4, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-09', 5, 'Sữa chua', 'Sữa Ellac Family');
-- Tuần 2026-11-16
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 0, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 1, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 2, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 3, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 4, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-16', 5, 'Sữa chua', 'Sữa Ellac Family');
-- Tuần 2026-11-23
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 0, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 1, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 2, 'Kem plant', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 3, 'Sữa chua', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 4, 'Yakult', 'Sữa Ellac Family');
INSERT OR REPLACE INTO cong_khai_phu (tuan_bat_dau, ngay, phu_sang, phu_chieu) VALUES ('2026-11-23', 5, '', '');
-- Sửa lỗi chính tả đã lỡ nhập trước đó (nếu có):
UPDATE cong_khai_phu SET phu_sang = 'Yakult' WHERE phu_sang = 'Yakul';
COMMIT;
