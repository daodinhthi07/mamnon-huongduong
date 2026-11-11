-- =====================================================================
-- TÁCH bữa "SÁNG" của Nhà trẻ ra khỏi Mẫu giáo — thực tế Nhà trẻ có món
-- sáng RIÊNG, không dùng chung với Mẫu giáo như phần mềm giả định trước đó.
--
-- Hạ tầng lưu món (bảng thuc_don, khóa theo khoi_id + loai_bua_an_id) đã sẵn
-- sàng cho việc này từ đầu — loai_bua_an đã có đúng 1 dòng "SÁNG" ring cho
-- MỖI khối (xem 04_nhom_bua_an.sql). Cái thiếu chỉ là:
--   1) Giao diện trang Chi tiết ngày đang ẩn ô chọn món "SÁNG" của Nhà trẻ
--      (dùng chung với Mẫu giáo) — sửa ở Views/ChiTietNgay/Index.cshtml.
--   2) Sĩ số/tiền ăn sáng đang chỉ có 1 nhóm chi phí "Ăn sáng" DÙNG CHUNG —
--      cần thêm 1 nhóm chi phí riêng cho sĩ số ăn sáng của Nhà trẻ, vì 2 khối
--      giờ có thể ăn 2 món sáng khác nhau, số cháu ăn sáng cũng khác nhau,
--      không thể gộp chung 1 con số như trước — script này thêm đúng nhóm đó.
--
-- Nhóm "Ăn sáng" cũ GIỮ NGUYÊN tên/ý nghĩa (sĩ số + tiền ăn sáng của Mẫu
-- giáo) để không phải sửa lại dữ liệu ngày đã lưu trước đây.
-- =====================================================================

INSERT INTO nhom_chi_phi (ten_nhom)
SELECT 'Ăn sáng - Nhà trẻ'
WHERE NOT EXISTS (SELECT 1 FROM nhom_chi_phi WHERE ten_nhom = 'Ăn sáng - Nhà trẻ');
