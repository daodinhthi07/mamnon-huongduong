(function () {
    'use strict';

    function danhSoLaiCacDong() {
        document.querySelectorAll('#bang-dong-body tr').forEach(function (tr, idx) {
            tr.querySelectorAll('[name^="CacDong["]').forEach(function (el) {
                el.name = el.name.replace(/CacDong\[\d+\]/, 'CacDong[' + idx + ']');
            });
        });
    }

    function ganSuKienDong(tr) {
        var btnXoa = tr.querySelector('.xoa-dong');
        if (btnXoa) {
            btnXoa.addEventListener('click', function () {
                tr.remove();
                danhSoLaiCacDong();
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

    // ----- Chọn nguyên liệu đã có ở dòng khác -> báo ngay, bỏ lựa chọn trùng và tô sáng dòng đã có -----
    function tenHang(sel) {
        var op = sel.options[sel.selectedIndex];
        return op ? op.textContent.trim() : '';
    }

    function hienCanhBao(msg) {
        var box = document.getElementById('canh-bao-trung');
        if (!box) { alert(msg); return; }
        box.textContent = msg;
        box.style.display = msg ? '' : 'none';
    }

    function toSangDong(tr) {
        if (!tr) return;
        tr.style.transition = 'background-color .3s';
        tr.style.backgroundColor = '#fff3cd';
        tr.scrollIntoView({ behavior: 'smooth', block: 'center' });
        setTimeout(function () { tr.style.backgroundColor = ''; }, 2500);
    }

    function timDongTrung(sel) {
        if (!sel.value) return null;
        var rows = document.querySelectorAll('#bang-dong-body tr');
        for (var i = 0; i < rows.length; i++) {
            var s = rows[i].querySelector('select[name$=".HangHoaId"]');
            if (s && s !== sel && s.value === sel.value) return { tr: rows[i], stt: i + 1 };
        }
        return null;
    }

    function kiemTraTrungKhiChon(e) {
        var sel = e.target;
        if (!sel || !sel.matches || !sel.matches('select[name$=".HangHoaId"]')) return;
        hienCanhBao('');
        var trung = timDongTrung(sel);
        if (!trung) return;
        var ten = tenHang(sel);
        sel.value = '';
        sel.dispatchEvent(new Event('mon-dong-bo'));
        hienCanhBao('Nguyên liệu "' + ten + '" đã có trong công thức (dòng ' + trung.stt + '). Hãy sửa số lượng ở dòng đó thay vì thêm lại.');
        toSangDong(trung.tr);
    }

    function kiemTraTrungKhiLuu(e) {
        var thay = {}, trung = [];
        document.querySelectorAll('#bang-dong-body select[name$=".HangHoaId"]').forEach(function (s) {
            if (!s.value) return;
            if (thay[s.value]) trung.push(tenHang(s)); else thay[s.value] = true;
        });
        if (trung.length === 0) return;
        e.preventDefault();
        hienCanhBao('Không lưu được: nguyên liệu bị chọn trùng — ' + trung.join(', ') + '. Hãy xóa bớt dòng trùng.');
        var box = document.getElementById('canh-bao-trung');
        if (box) box.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    document.addEventListener('DOMContentLoaded', function () {
        var body = document.getElementById('bang-dong-body');
        if (body) body.addEventListener('change', kiemTraTrungKhiChon);
        var form = document.getElementById('form-mon-an');
        if (form) form.addEventListener('submit', kiemTraTrungKhiLuu);
        document.querySelectorAll('#bang-dong-body tr').forEach(ganSuKienDong);
        var btnThem = document.getElementById('them-dong');
        if (btnThem) btnThem.addEventListener('click', themDong);
    });
})();
