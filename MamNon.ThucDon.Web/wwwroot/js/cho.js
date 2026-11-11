// Dùng chung cho CẢ trang /cho/chi-tiet (ChiTiet.cshtml) LẪN panel nhúng AJAX trong
// Dashboard (_ChiTietChoPanel.cshtml gọi qua dashboard.js). Tách riêng file này để
// trang /cho/chi-tiet không phải nạp toàn bộ dashboard.js (vốn chỉ dành cho lưới
// thực đơn) chỉ để có được các hàm bên dưới.
(function () {
    'use strict';

    // ---- Panel "Chi tiết chợ" nhúng bằng AJAX ngay dưới lưới thực đơn trên Dashboard
    // (không dùng trên trang /cho/chi-tiet standalone — ở đó không có #cho-panel) ----
    function taiChoPanelQs(qs) {
        var panel = document.getElementById('cho-panel');
        if (!panel) return;
        panel.style.display = 'block';
        panel.innerHTML = '<p class="cho-panel-loading">Đang tải dữ liệu chợ…</p>';

        fetch('/cho/chi-tiet-panel?' + qs)
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text();
            })
            .then(function (html) {
                panel.innerHTML = html;
            })
            .catch(function () {
                panel.innerHTML = '<p class="cho-panel-loading">Không tải được dữ liệu chợ. Vui lòng thử lại.</p>';
            });
    }

    function taiChoPanel(ngay, coSoId) {
        var qs = 'ngay=' + encodeURIComponent(ngay);
        if (coSoId) qs += '&coSoId=' + encodeURIComponent(coSoId);
        taiChoPanelQs(qs);
    }

    function xemChoTrongTrang(ngay) {
        taiChoPanel(ngay, null);
        var panel = document.getElementById('cho-panel');
        if (panel) panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    window.xemChoTrongTrang = xemChoTrongTrang;

    // Gọi khi bấm tab đổi Cơ sở (TC/PH/ĐT) NGAY TRONG panel — không cuộn lại trang
    // vì panel đã đang hiện sẵn trước mắt người dùng.
    function xemChoCoSo(ngay, coSoId) {
        taiChoPanel(ngay, coSoId);
    }
    window.xemChoCoSo = xemChoCoSo;

    function dongChoPanel() {
        var panel = document.getElementById('cho-panel');
        if (!panel) return;
        panel.style.display = 'none';
        panel.innerHTML = '';
    }
    window.dongChoPanel = dongChoPanel;

    // ---- "Nhập số suất ăn trong ngày" — Tính lại / Khôi phục số gốc ----
    // Hoạt động ở CẢ 2 nơi:
    //  - Trang đầy đủ /cho/chi-tiet: .cho-editbox nằm trong khối có [data-cho-mode="page"]
    //    -> điều hướng cả trang (window.location.href) với query string mới.
    //  - Panel nhúng trong Dashboard: .cho-editbox nằm trong [data-cho-mode="panel"]
    //    -> gọi lại AJAX (taiChoPanelQs) để panel tự cập nhật tại chỗ, không rời trang.
    function choLayQuaTiHienTai(box, themSuat) {
        var qs = 'ngay=' + encodeURIComponent(box.getAttribute('data-ngay'))
               + '&coSoId=' + encodeURIComponent(box.getAttribute('data-coso'));
        if (themSuat) {
            var anSang = box.querySelector('[data-field="an-sang"]').value;
            var nhaTre = box.querySelector('[data-field="nha-tre"]').value;
            var mauGiao = box.querySelector('[data-field="mau-giao"]').value;
            qs += '&anSang=' + encodeURIComponent(anSang)
                + '&nhaTre=' + encodeURIComponent(nhaTre)
                + '&mauGiao=' + encodeURIComponent(mauGiao);
        }
        return qs;
    }

    function choDieuHuong(box, qs) {
        var panelMode = box.closest('[data-cho-mode="panel"]');
        if (panelMode) {
            taiChoPanelQs(qs);
        } else {
            window.location.href = '/cho/chi-tiet?' + qs;
        }
    }

    function choTinhLai(btn) {
        var box = btn.closest('.cho-editbox');
        if (!box) return;
        choDieuHuong(box, choLayQuaTiHienTai(box, true));
    }
    window.choTinhLai = choTinhLai;

    function choKhoiPhuc(btn) {
        var box = btn.closest('.cho-editbox');
        if (!box) return;
        choDieuHuong(box, choLayQuaTiHienTai(box, false));
    }
    window.choKhoiPhuc = choKhoiPhuc;
})();
