(function () {
    'use strict';

    var LOAI_THUC_PHAM_SANG = '1'; // khớp thứ tự enum LoaiXuatKho: GiaViHangNgay=0, ThucPhamSang=1, ThucPhamChinhXe=2

    function capNhatCotNhomChiPhi() {
        var loaiXuat = document.getElementById('loaiXuat').value;
        var an = loaiXuat === LOAI_THUC_PHAM_SANG;
        document.querySelectorAll('.cot-nhom-chi-phi').forEach(function (el) {
            el.style.display = an ? 'none' : '';
        });
    }

    function formatTien(so) {
        return Math.round(so).toLocaleString('vi-VN') + 'đ';
    }

    function taiDoiChieuNganSach() {
        var coSoId = document.getElementById('coSoId').value;
        var ngay = document.getElementById('ngayXuat').value;
        var el = document.getElementById('doi-chieu-ngan-sach');

        if (!coSoId || !ngay) {
            el.innerHTML = '<em>Chọn cơ sở và ngày để xem định mức ngân sách (nếu đã nhập ở trang Soạn thực đơn).</em>';
            return;
        }

        fetch('/kho/dinh-muc-ngay?coSoId=' + coSoId + '&ngay=' + ngay)
            .then(function (res) { return res.json(); })
            .then(function (ds) {
                if (!ds.length) {
                    el.innerHTML = '<em>Ngày này chưa có định mức ngân sách nào.</em>';
                    return;
                }
                var html = '<table class="table"><thead><tr><th>Nhóm chi phí</th><th>Số tiền được phép chi</th></tr></thead><tbody>';
                ds.forEach(function (d) {
                    html += '<tr><td>' + d.tenNhom + '</td><td>' + formatTien(d.soTienDuocPhepChi) + '</td></tr>';
                });
                html += '</tbody></table>';
                el.innerHTML = html;
            });
    }

    document.addEventListener('DOMContentLoaded', function () {
        capNhatCotNhomChiPhi();
        taiDoiChieuNganSach();

        document.getElementById('loaiXuat').addEventListener('change', capNhatCotNhomChiPhi);
        document.getElementById('coSoId').addEventListener('change', taiDoiChieuNganSach);
        document.getElementById('ngayXuat').addEventListener('change', taiDoiChieuNganSach);
    });
})();
