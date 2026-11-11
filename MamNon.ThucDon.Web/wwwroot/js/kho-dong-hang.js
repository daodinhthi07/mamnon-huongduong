(function () {
    'use strict';

    function formatTien(so) {
        return Math.round(so).toLocaleString('vi-VN') + 'đ';
    }

    function tinhThanhTien(tr) {
        var soLuong = parseFloat(tr.querySelector('.so-luong').value || '0');
        var donGia = parseFloat(tr.querySelector('.don-gia').value || '0');
        var thanhTien = soLuong * donGia;
        tr.querySelector('.thanh-tien').textContent = formatTien(thanhTien);
        return thanhTien;
    }

    function tinhTongCong() {
        var tong = 0;
        document.querySelectorAll('#bang-dong-body tr').forEach(function (tr) {
            tong += tinhThanhTien(tr);
        });
        var el = document.getElementById('tong-cong');
        if (el) el.innerHTML = '<strong>' + formatTien(tong) + '</strong>';
    }

    // QUAN TRỌNG: MVC model binder cho List<T> chỉ nhận index liên tục 0,1,2...
    // — nếu xóa 1 dòng giữa bảng mà không đánh số lại, các dòng sau đó sẽ bị
    // bỏ qua hoàn toàn khi submit (binder dừng lại ở index bị thiếu).
    function danhSoLaiCacDong() {
        document.querySelectorAll('#bang-dong-body tr').forEach(function (tr, idx) {
            tr.querySelectorAll('[name^="CacDong["]').forEach(function (el) {
                el.name = el.name.replace(/CacDong\[\d+\]/, 'CacDong[' + idx + ']');
            });
        });
    }

    function ganSuKienDong(tr) {
        tr.querySelectorAll('.so-luong, .don-gia').forEach(function (input) {
            input.addEventListener('input', tinhTongCong);
        });
        var chonHang = tr.querySelector('.chon-hang');
        if (chonHang) {
            chonHang.addEventListener('change', function () {
                var opt = chonHang.options[chonHang.selectedIndex];
                var gia = opt ? opt.getAttribute('data-gia') : null;
                if (gia && gia !== '') {
                    tr.querySelector('.don-gia').value = gia;
                    tinhTongCong();
                }
            });
        }
        var btnXoa = tr.querySelector('.xoa-dong');
        if (btnXoa) {
            btnXoa.addEventListener('click', function () {
                tr.remove();
                danhSoLaiCacDong();
                tinhTongCong();
            });
        }
    }

    function themDong() {
        var soDongHienTai = document.querySelectorAll('#bang-dong-body tr').length;
        var mau = document.getElementById('mau-dong');
        var html = mau.innerHTML.replace(/__IDX__/g, soDongHienTai);
        var tbody = document.getElementById('bang-dong-body');
        var tempTable = document.createElement('table');
        tempTable.innerHTML = '<tbody>' + html + '</tbody>';
        var tr = tempTable.querySelector('tr');
        tbody.appendChild(tr);
        ganSuKienDong(tr);
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('#bang-dong-body tr').forEach(ganSuKienDong);
        tinhTongCong();

        var btnThem = document.getElementById('them-dong');
        if (btnThem) btnThem.addEventListener('click', themDong);
    });
})();
