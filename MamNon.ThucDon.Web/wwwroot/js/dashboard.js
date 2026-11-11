(function () {
    'use strict';

    function xemLuoi() {
        var tuanBatDau = document.getElementById('tuanBatDau').value;
        if (!tuanBatDau) {
            alert('Chọn tuần trước đã.');
            return;
        }
        window.location.href = '/?tuanBatDau=' + tuanBatDau;
    }

    // Gom lưới của TẤT CẢ khối trên trang thành 1 object: { khoiId: { ngay: { buaId: monAnId|null } } }
    function layLuoiHienTai() {
        var luoi = {};
        document.querySelectorAll('.dashboard-chon-mon').forEach(function (sel) {
            var khoiId = sel.getAttribute('data-khoi-id');
            var buaId = sel.getAttribute('data-bua-id');
            var ngay = sel.getAttribute('data-ngay');
            if (!luoi[khoiId]) luoi[khoiId] = {};
            if (!luoi[khoiId][ngay]) luoi[khoiId][ngay] = {};
            luoi[khoiId][ngay][buaId] = sel.value ? parseInt(sel.value, 10) : null;
        });
        return luoi;
    }

    // Phụ sáng / Phụ chiều đã sửa trên Trang chủ (chỉ các ngày có data-sua-phu="1"): { ngay: { sang, chieu } }
    function layPhuHienTai() {
        var kq = {};
        document.querySelectorAll('.dashboard-phu[data-sua-phu="1"]').forEach(function (s) {
            kq[s.getAttribute('data-ngay')] = { sang: s.getAttribute('data-sang') || '', chieu: s.getAttribute('data-chieu') || '' };
        });
        return kq;
    }

    function dashboardLuu(guiDuyet) {
        document.getElementById('hiddenLuoiJson').value = JSON.stringify(layLuoiHienTai());
        document.getElementById('hiddenPhuJson').value = JSON.stringify(layPhuHienTai());
        document.getElementById('hiddenGuiDuyet').value = guiDuyet ? 'true' : 'false';
        document.getElementById('hiddenXuatExcel').value = 'false';
        document.getElementById('hiddenMoChiTietNgay').value = '';
        document.getElementById('hiddenCongKhai').value = 'false';
        document.getElementById('hiddenCacNgay').value = '';
        document.getElementById('form-dashboard-luu').submit();
    }
    window.dashboardLuu = dashboardLuu;

    // Lưu món + công khai ngay: cả tuần, hoặc chỉ các ngày được tick (T2..T7).
    function dashboardLuuCongKhai() {
        var theoNgay = document.querySelector('input[name="phamViCongKhai"]:checked').value === 'ngay';
        var ngay = [];
        var cauHoi = 'Lưu món và công khai CẢ TUẦN cho phụ huynh?';
        if (theoNgay) {
            // "Đến hết ngày X": phụ huynh thấy Thứ 2 → X; các ngày sau X chưa hiện.
            var sel = document.getElementById('ck-den-ngay');
            var k = parseInt(sel.value, 10);
            for (var i = 0; i <= k; i++) ngay.push(i);
            cauHoi = 'Lưu món và công khai từ Thứ 2 đến hết ' + sel.options[sel.selectedIndex].text +
                '?\nPhụ huynh chỉ thấy các ngày này; các ngày sau đó sẽ chưa hiện.';
        }
        if (!confirm(cauHoi)) return;
        document.getElementById('hiddenLuoiJson').value = JSON.stringify(layLuoiHienTai());
        document.getElementById('hiddenPhuJson').value = JSON.stringify(layPhuHienTai());
        document.getElementById('hiddenGuiDuyet').value = 'false';
        document.getElementById('hiddenXuatExcel').value = 'false';
        document.getElementById('hiddenMoChiTietNgay').value = '';
        document.getElementById('hiddenCongKhai').value = 'true';
        document.getElementById('hiddenCacNgay').value = ngay.join(',');
        document.getElementById('form-dashboard-luu').submit();
    }
    window.dashboardLuuCongKhai = dashboardLuuCongKhai;
    document.addEventListener('DOMContentLoaded', function () {
        var vung = document.getElementById('ck-chon-ngay');
        document.querySelectorAll('input[name="phamViCongKhai"]').forEach(function (r) {
            r.addEventListener('change', function () {
                var ngay = document.querySelector('input[name="phamViCongKhai"]:checked').value === 'ngay';
                if (vung) vung.style.display = ngay ? 'inline-flex' : 'none';
            });
        });
    });

    // Trang chủ chỉ XEM: mỗi ô là 1 ô ẩn giữ id món (value) + tên món (data-ten) — bảng phía trên vẽ lại từ đây.
    function datOChon(sel, giaTri) {
        var v = (giaTri == null) ? '' : String(giaTri);
        sel.value = v;
        sel.setAttribute('data-ten', v ? ((window.DASHBOARD_TEN_MON || {})[v] || '') : '');
    }
    function lamMoiBang() { if (typeof window.dashboardLamMoiBang === 'function') window.dashboardLamMoiBang(); }

    // Ngẫu nhiên cả tuần: lấy món từ tuần gần nhất trong Lịch sử, điền vào lưới (chưa ghi hệ thống — bấm "Lưu nháp cả tuần").
    function dashboardNgauNhienCaTuan() {
        if (!confirm('Tạo ngẫu nhiên thực đơn cả tuần? Món không lặp trong tuần và không trùng món của tuần gần nhất; số suất ăn lấy theo tuần gần nhất. Món đang chọn sẽ bị thay (chưa ghi vào hệ thống đến khi bấm Lưu nháp cả tuần hoặc Xuất Excel).')) return;
        var tuan = document.getElementById('tuanBatDau').value;
        fetch('/chi-tiet-ngay/ngau-nhien-ca-tuan?ngay=' + encodeURIComponent(tuan))
            .then(function (r) { return r.json(); })
            .then(function (kq) {
                if (!kq.ok) { alert(kq.loi || 'Không tạo được.'); return; }
                document.querySelectorAll('.dashboard-chon-mon').forEach(function (sel) {
                    var d = kq.cacNgay[parseInt(sel.getAttribute('data-ngay'), 10)];
                    if (!d) return;
                    var buaId = sel.getAttribute('data-bua-id');
                    var v = d.monMauGiao[buaId];
                    if (v == null) v = d.monNhaTre[buaId];
                    datOChon(sel, v);
                });
                lamMoiBang();
                var canhBao = (kq.canhBao && kq.canhBao.length) ? '\n\nLưu ý:\n- ' + kq.canhBao.join('\n- ') : '';
                alert('Đã tạo ngẫu nhiên cả tuần (tránh món của tuần ' + kq.tuan + '). Bấm "Lưu nháp cả tuần" hoặc "Xuất Excel" để ghi vào hệ thống.' + canhBao);
            })
            .catch(function (err) { console.error(err); alert('Lỗi khi tạo ngẫu nhiên.'); });
    }
    window.dashboardNgauNhienCaTuan = dashboardNgauNhienCaTuan;

    function dashboardXoaCaTuan() {
        if (!confirm('Xóa món đã chọn của CẢ TUẦN (chưa ghi vào hệ thống đến khi bấm Lưu nháp cả tuần)?')) return;
        document.querySelectorAll('.dashboard-chon-mon').forEach(function (sel) { datOChon(sel, null); });
        lamMoiBang();
    }
    window.dashboardXoaCaTuan = dashboardXoaCaTuan;

    // ---- Ô tick "Thứ 7 nghỉ": lưu ngay khi tick / bỏ tick, rồi tô xám + khóa cột Thứ 7 ----
    function hienThuBayNghi(nghi) {
        document.querySelectorAll('[data-cot="5"]').forEach(function (el) { el.classList.toggle('dashboard-ngay-nghi', nghi); });
        document.querySelectorAll('.dashboard-chon-mon[data-ngay="5"]').forEach(function (sel) { sel.disabled = nghi; });
        lamMoiBang();
    }

    function khoiTaoThuBayNghi() {
        var chk = document.getElementById('chk-thu7-nghi');
        var tb = document.getElementById('thu7-tb');
        if (!chk) return;
        chk.addEventListener('change', function () {
            var nghi = chk.checked;
            var token = document.querySelector('#form-dashboard-luu input[name="__RequestVerificationToken"]');
            var body = new URLSearchParams();
            body.set('tuanBatDau', document.getElementById('hiddenTuanBatDau').value);
            body.set('nghi', nghi ? 'true' : 'false');
            body.set('__RequestVerificationToken', token ? token.value : '');
            chk.disabled = true;
            if (tb) tb.textContent = 'Đang lưu...';
            fetch('/dashboard/thu-bay-nghi', {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: body
            }).then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.json();
            }).then(function () {
                hienThuBayNghi(nghi);
                chk.disabled = false;
                if (tb) tb.textContent = nghi ? 'Đã đặt Thứ 7 là ngày nghỉ.' : 'Đã bỏ nghỉ Thứ 7 (học bình thường).';
            }).catch(function (err) {
                console.error(err);
                chk.checked = !nghi;
                chk.disabled = false;
                if (tb) tb.textContent = 'Không lưu được, thử lại.';
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        khoiTaoThuBayNghi();
        var btn = document.getElementById('btn-xem');
        if (btn) btn.addEventListener('click', xemLuoi);
        var btnExcel = document.getElementById('btn-xuat-excel');
        if (btnExcel) btnExcel.addEventListener('click', function () {
            // Lưu lưới đang hiện rồi tải Excel (cùng cách xuất với trang Lịch sử).
            document.getElementById('hiddenLuoiJson').value = JSON.stringify(layLuoiHienTai());
        document.getElementById('hiddenPhuJson').value = JSON.stringify(layPhuHienTai());
            document.getElementById('hiddenGuiDuyet').value = 'false';
            document.getElementById('hiddenXuatExcel').value = 'true';
            document.getElementById('hiddenCongKhai').value = 'false';
            document.getElementById('hiddenCacNgay').value = '';
            document.getElementById('hiddenMoChiTietNgay').value = '';
            window.xuatExcelAjax(document.getElementById('form-dashboard-luu'));
        });
        function moChiTiet(ngay) {
            if (window.khongMoChiTiet) return;   // vai trò Quản lý: không có quyền vào Chi tiết - Chi tiền chợ
            // Tài khoản chỉ xem không có quyền Lưu -> đi thẳng sang Chi tiết, không submit.
            if (document.getElementById('form-dashboard-luu').getAttribute('data-sua') !== '1') { window.location.href = '/chi-tiet-ngay?ngay=' + ngay; return; }
            document.getElementById('hiddenLuoiJson').value = JSON.stringify(layLuoiHienTai());
        document.getElementById('hiddenPhuJson').value = JSON.stringify(layPhuHienTai());
            document.getElementById('hiddenGuiDuyet').value = 'false';
            document.getElementById('hiddenXuatExcel').value = 'false';
            document.getElementById('hiddenMoChiTietNgay').value = ngay;
            document.getElementById('hiddenCongKhai').value = 'false';
            document.getElementById('hiddenCacNgay').value = '';
            document.getElementById('form-dashboard-luu').submit();
        }
        window.dashboardMoChiTiet = moChiTiet;   // bảng tuần (dashboard-bang.js): bấm tiêu đề ngày -> mở Chi tiết ngày đó
        var btnCt = document.getElementById('btn-chi-tiet');
        if (btnCt) btnCt.addEventListener('click', function () { moChiTiet(btnCt.getAttribute('data-ngay')); });
        // Bấm tiêu đề cột ngày -> mở Chi tiết đúng ngày đó (lưu lưới trước).
        document.querySelectorAll('th[data-mo-ngay]').forEach(function (th) {
            th.addEventListener('click', function () { moChiTiet(th.getAttribute('data-mo-ngay')); });
        });
    });
})();
