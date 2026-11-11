-- =====================================================================
-- SỬA PHÂN LOẠI: các gia vị/rau thơm bị gắn nhầm 'Thực phẩm tươi'/'Khác'
-- thay vì 'Gia vị' (do ánh xạ theo NHÓM trong Bảng giá quá thô — hành/tỏi/ớt
-- nằm chung nhóm với rau củ quả, muối/dầu mè nằm chung nhóm với 'Khác').
-- Chạy sau 05_seed_hang_hoa.sql. Ảnh hưởng trực tiếp tới mục
-- "2. Xuất kho gia vị" ở trang Chi tiết ngày — trước đây bị thiếu vì các
-- nguyên liệu này không được nhận diện là gia vị.
-- =====================================================================

UPDATE hang_hoa SET loai_hang = 'Gia vị' WHERE ten_hang IN (
    'Hành lá',
    'Hành ngò',
    'Hành Tây',
    'Hành tím',
    'Hành tím lột vỏ',
    'Gừng quê',
    'Gừng to',
    'Nghệ tươi',
    'Nghệ tươi xay',
    'Ngò (mùi)',
    'Ngò gai',
    'Ớt chuông đỏ',
    'Ớt chuông vàng',
    'Ớt chuông xanh',
    'Ớt trái',
    'Sả',
    'Sả băm',
    'Tỏi khô',
    'Tỏi khô sơ chế',
    'Tỏi tím hà nội',
    'Lá chanh',
    'Me chua',
    'Muối iốt',
    'Muối đầu bếp',
    'Muối hạt',
    'Dầu mè trắng Tường An 250ml (12 chai/thùng)'
);