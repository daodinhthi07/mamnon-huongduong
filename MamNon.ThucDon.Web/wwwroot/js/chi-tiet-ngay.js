(function () {
    'use strict';

    function dinhDangTien(so) {
        return Math.round(so || 0).toLocaleString('vi-VN');
    }

    function layMonTheoBua(selector) {
        var ketQua = {};
        document.querySelectorAll(selector).forEach(function (sel) {
            ketQua[sel.getAttribute('data-bua-id')] = sel.value ? parseInt(sel.value, 10) : null;
        });
        return ketQua;
    }

    function xemLai() {
        var coSoId = document.getElementById('coSoIdHienTai').value;
        var ngay = document.getElementById('ngay').value;
        if (!coSoId) {
            alert('Chọn cơ sở ở hàng pill phía trên trước đã.');
            return;
        }
        if (!ngay) {
            alert('Chọn ngày trước đã.');
            return;
        }
        window.location.href = '/chi-tiet-ngay?coSoId=' + coSoId + '&ngay=' + ngay;
    }

    // ---- Lưu tạm (draft) lựa chọn chưa bấm Lưu vào localStorage, theo TỪNG cặp
    // (Cơ sở, Ngày) riêng — để khi bấm đổi tab Cơ sở/Ngày (trang tải lại từ server)
    // rồi quay lại đúng cặp đó, các ô chọn món/sĩ số vẫn hiện đúng như lúc rời đi,
    // thay vì bị nạp lại y hệt dữ liệu đã lưu chính thức trong CSDL. ----
    var DRAFT_HET_HAN_MS = 24 * 60 * 60 * 1000; // 24 giờ — tránh draft cũ ở lại vĩnh viễn

    function khoaDraftCho(coSoId, ngay) {
        return 'chiTietNgayDraft:' + coSoId + ':' + ngay;
    }

    function khoaDraftHienTai() {
        return khoaDraftCho(document.getElementById('coSoIdHienTai').value, document.getElementById('ngay').value);
    }

    function layDanhSachCoSoId() {
        var el = document.getElementById('dsCoSoIds');
        if (!el || !el.value) return [];
        return el.value.split(',').map(function (s) { return s.trim(); }).filter(Boolean);
    }

    function ghiDraft(khoa, draft) {
        try { localStorage.setItem(khoa, JSON.stringify(draft)); } catch (e) {
            // localStorage đầy hoặc bị chặn (chế độ ẩn danh) — bỏ qua, không chặn thao tác chính.
        }
    }

    function docDraft(khoa) {
        var raw;
        try { raw = localStorage.getItem(khoa); } catch (e) { return null; }
        if (!raw) return null;
        try { return JSON.parse(raw); } catch (e) { return null; }
    }

    function luuDraftHienTai() {
        if (!document.getElementById('khoiIdMauGiao')) return;
        var ngay = document.getElementById('ngay').value;
        var coSoIdHienTai = document.getElementById('coSoIdHienTai').value;
        var monMauGiao = layMonTheoBua('.mon-mau-giao');
        var monNhaTre = layMonTheoBua('.mon-nha-tre');

        ghiDraft(khoaDraftCho(coSoIdHienTai, ngay), {
            monMauGiao: monMauGiao,
            monNhaTre: monNhaTre,
            soSuatAnSang: document.getElementById('soSuatAnSang').value,
            soSuatMauGiao: document.getElementById('soSuatMauGiao').value,
            soSuatNhaTre: document.getElementById('soSuatNhaTre').value,
            tienAnSang: document.getElementById('tienAnSang').value,
            tienMauGiao: document.getElementById('tienMauGiao').value,
            tienNhaTre: document.getElementById('tienNhaTre').value,
            luuLuc: Date.now(),
        });

        // Món ăn dùng CHUNG cho mọi cơ sở (đúng như khi bấm "Lưu nháp" ở server —
        // xem ChiTietNgayController.Luu) — nên đồng bộ NGAY phần món sang draft của
        // các cơ sở còn lại, để đổi tab qua là thấy đúng món luôn, không cần đợi
        // bấm Lưu trước. Sĩ số/tiền của các cơ sở khác giữ nguyên, không bị ghi đè,
        // vì mỗi cơ sở có số cháu khác nhau.
        layDanhSachCoSoId().forEach(function (coSoIdKhac) {
            if (coSoIdKhac === coSoIdHienTai) return;
            var draftKhac = docDraft(khoaDraftCho(coSoIdKhac, ngay)) || {};
            draftKhac.monMauGiao = monMauGiao;
            draftKhac.monNhaTre = monNhaTre;
            draftKhac.luuLuc = Date.now();
            ghiDraft(khoaDraftCho(coSoIdKhac, ngay), draftKhac);
        });
    }

    function xoaDraftHienTai() {
        try { localStorage.removeItem(khoaDraftHienTai()); } catch (e) { /* bỏ qua */ }
    }

    function apDungGiaTri(id, giaTri) {
        if (giaTri === undefined || giaTri === null || giaTri === '') return;
        var el = document.getElementById(id);
        if (el) el.value = giaTri;
    }

    function taiDraftNeuCo() {
        var draft = docDraft(khoaDraftHienTai());
        if (!draft || (draft.luuLuc && Date.now() - draft.luuLuc > DRAFT_HET_HAN_MS)) {
            if (draft) xoaDraftHienTai();
            return;
        }

        document.querySelectorAll('.mon-mau-giao').forEach(function (sel) {
            var buaId = sel.getAttribute('data-bua-id');
            if (draft.monMauGiao && Object.prototype.hasOwnProperty.call(draft.monMauGiao, buaId)) {
                sel.value = draft.monMauGiao[buaId] || '';
            }
        });
        document.querySelectorAll('.mon-nha-tre').forEach(function (sel) {
            var buaId = sel.getAttribute('data-bua-id');
            if (draft.monNhaTre && Object.prototype.hasOwnProperty.call(draft.monNhaTre, buaId)) {
                sel.value = draft.monNhaTre[buaId] || '';
            }
        });
        apDungGiaTri('soSuatAnSang', draft.soSuatAnSang);
        apDungGiaTri('soSuatMauGiao', draft.soSuatMauGiao);
        apDungGiaTri('soSuatNhaTre', draft.soSuatNhaTre);
        apDungGiaTri('tienAnSang', draft.tienAnSang);
        apDungGiaTri('tienMauGiao', draft.tienMauGiao);
        apDungGiaTri('tienNhaTre', draft.tienNhaTre);
    }

    // ---- Mục 3 / 4: Sửa & Xóa từng dòng (giống mục 2) ----
    // Dữ liệu đang hiện của 2 bảng — dùng làm "ảnh chụp" gửi lên khi sửa/xóa LẦN ĐẦU trong
    // ngày, để server ghi cố định đủ các dòng đang thấy (không mất dòng nào).
    var duLieuSang = [];
    var duLieuChinh = [];

    function esc(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function layThongTinNgayCoSo() {
        var form = document.getElementById('form-chi-tiet-ngay-luu');
        return {
            coSoId: form.querySelector('[name=coSoId]').value,
            ngay: form.querySelector('[name=ngay]').value,
            token: form.querySelector('[name=__RequestVerificationToken]').value
        };
    }

    // Tên món dùng nguyên liệu này (nhỏ, dưới tên hàng) — bảng đã xếp theo thứ tự món trong thực đơn.
    function tagMon(r) {
        return r.tenMon ? '<div class="mon-tag">' + esc(r.tenMon) + '</div>' : '';
    }

    function oAn(name, value) {
        return '<input type="hidden" name="' + name + '" value="' + esc(value) + '" />';
    }

    function truongChung(loai, hangHoaId, donGia) {
        var tt = layThongTinNgayCoSo();
        var snapshot = loai === 'sang' ? duLieuSang : duLieuChinh;
        return oAn('__RequestVerificationToken', tt.token) + oAn('coSoId', tt.coSoId) + oAn('ngay', tt.ngay) +
            oAn('loai', loai) + oAn('hangHoaId', hangHoaId) + oAn('donGia', donGia) +
            oAn('snapshotJson', JSON.stringify(snapshot.map(function (r) {
                return {
                    hangHoaId: r.hangHoaId,
                    soLuong: r.soLuong || 0,
                    slNhaTre: r.slNhaTre || 0,
                    slMauGiao: r.slMauGiao || 0,
                    donGia: r.donGia || 0
                };
            })));
    }

    function nutHanhDong(loai, rid, r) {
        return '<td class="gia-vi-actions">' +
            '<button type="button" class="btn-link" onclick="chiTietNgayToggleSuaGiaVi(\'' + rid + '\')">Sửa</button>' +
            '<form method="post" action="/chi-tiet-ngay/xoa-mot-thuc-pham" class="inline-form" ' +
            'onsubmit="return confirm(\'Xóa số lượng của &quot;' + esc(r.tenHang).replace(/'/g, '') + '&quot; cho riêng ngày này (về 0)?\');">' +
            truongChung(loai, r.hangHoaId, r.donGia) +
            '<button type="submit" class="btn-link btn-link-danger">Xóa</button></form></td>';
    }

    function slInput(name, label, v) {
        return '<label>' + label + ' <input type="number" name="' + name + '" min="0" step="0.000001" value="' + esc(v) + '" /></label>';
    }

    function ghiChuMuc(idEl, loai, coPhieu) {
        var el = document.getElementById(idEl);
        if (!el) return;
        el.textContent = 'Chỉ để xem: số tính theo thực đơn × sĩ số theo quy tắc từng chợ. Không sửa/xóa ở trang này.';
    }

    function renderSang(ds, coPhieu) {
        duLieuSang = ds || [];
        var tbody = document.querySelector('#bang-sang tbody');
        tbody.innerHTML = '';
        duLieuSang.filter(conSoLuong).forEach(function (r) {
            var rid = 'sang-' + r.hangHoaId;
            tbody.insertAdjacentHTML('beforeend',
                '<tr id="' + rid + '-xem"><td>' + esc(r.tenHang) + tagMon(r) + '</td><td>' + esc(r.dvt || '') + '</td><td>' + r.soLuong + '</td>' +
                '<td>' + dinhDangTien(r.donGia) + '</td><td>' + dinhDangTien(r.thanhTien) + '</td></tr>');

        });
        ghiChuMuc('ghi-chu-sang', 'sang', coPhieu);
    }

    // Dòng đã bấm "Xóa" được lưu = 0 (để không tự hiện lại) -> KHÔNG hiển thị trên giao diện (Excel cũng ẩn dòng toàn 0).
    function conSoLuong(r) { return (r.slNhaTre || 0) > 0 || (r.slMauGiao || 0) > 0 || (r.soLuong || 0) > 0; }

    function renderChinh(ds, coPhieu) {
        duLieuChinh = ds || [];
        var tbody = document.querySelector('#bang-chinh-xe tbody');
        tbody.innerHTML = '';
        duLieuChinh.filter(conSoLuong).forEach(function (r) {
            var rid = 'chinh-' + r.hangHoaId;
            tbody.insertAdjacentHTML('beforeend',
                '<tr id="' + rid + '-xem"><td>' + esc(r.tenHang) + tagMon(r) + '</td><td>' + esc(r.dvt || '') + '</td>' +
                '<td>' + r.slNhaTre + '</td><td>' + r.slMauGiao + '</td>' +
                '<td>' + dinhDangTien(r.donGia) + '</td><td>' + dinhDangTien(r.ttNhaTre) + '</td><td>' + dinhDangTien(r.ttMauGiao) + '</td>' +
                '</tr>');

        });
        ghiChuMuc('ghi-chu-chinh-xe', 'chinh', coPhieu);
    }

    function guiTinhToan() {
        var khoiIdMauGiao = document.getElementById('khoiIdMauGiao').value;
        var khoiIdNhaTre = document.getElementById('khoiIdNhaTre').value;

        var payload = {
            khoiIdMauGiao: parseInt(khoiIdMauGiao, 10),
            khoiIdNhaTre: parseInt(khoiIdNhaTre, 10),
            coSoId: parseInt(layThongTinNgayCoSo().coSoId, 10),
            ngay: layThongTinNgayCoSo().ngay,
            monMauGiao: layMonTheoBua('.mon-mau-giao'),
            monNhaTre: layMonTheoBua('.mon-nha-tre'),
            soSuatAnSang: parseInt(document.getElementById('soSuatAnSang').value || '0', 10),
            soSuatMauGiao: parseInt(document.getElementById('soSuatMauGiao').value || '0', 10),
            soSuatNhaTre: parseInt(document.getElementById('soSuatNhaTre').value || '0', 10),
        };

        fetch('/chi-tiet-ngay/tinh-toan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
            .then(function (r) { return r.json(); })
            .then(function (kq) {
                renderSang(kq.sang, kq.coPhieuSang);
                renderChinh(kq.chinh, kq.coPhieuChinhXe);
                if (kq.coPhieuChinhXe && kq.ghiChuLech) {
                    var elLech = document.getElementById('ghi-chu-chinh-xe');
                    if (elLech) {
                        var canhBao = document.createElement('div');
                        canhBao.style.color = '#b45309';
                        canhBao.style.fontWeight = '600';
                        canhBao.textContent = '⚠ ' + kq.ghiChuLech;
                        elLech.appendChild(canhBao);
                    }
                }

                // Mục 2 (Gia vị) không nằm trong kết quả AJAX nữa — đó là số liệu THẬT
                // lấy từ phiếu xuất kho, hiển thị tĩnh sẵn từ server (không đổi theo món
                // chọn), nên chỉ cần LẤY LẠI 3 số tổng đã render sẵn để cộng vào tổng
                // tiền cả ngày bên dưới.
                var giaViAnSang = parseFloat(document.getElementById('tongTienGiaViAnSang').value || '0');
                var giaViNhaTre = parseFloat(document.getElementById('tongTienGiaViNhaTre').value || '0');
                var giaViMauGiao = parseFloat(document.getElementById('tongTienGiaViMauGiao').value || '0');

                var tongAnSang = giaViAnSang + kq.tongTienSang;
                var tongNhaTre = giaViNhaTre + kq.tongTienChinhNhaTre;
                var tongMauGiao = giaViMauGiao + kq.tongTienChinhMauGiao;

                document.getElementById('tong-an-sang').textContent = dinhDangTien(tongAnSang);
                document.getElementById('tong-nha-tre').textContent = dinhDangTien(tongNhaTre);
                document.getElementById('tong-mau-giao').textContent = dinhDangTien(tongMauGiao);
                document.getElementById('tong-ca-ngay').textContent = dinhDangTien(tongAnSang + tongNhaTre + tongMauGiao) + 'đ';
            })
            .catch(function (err) { console.error('Lỗi tính toán:', err); });
    }

    function chiTietNgayXuatExcel() {
        var ngay = document.getElementById('ngay').value;
        if (!ngay) {
            alert('Chọn ngày trước đã.');
            return;
        }

        // Lưu nháp NGAY những gì đang hiện trên màn hình trước (không cần bấm "Lưu nháp"
        // tay trước đó), rồi mới chuyển sang tải file — để Excel luôn khớp đúng những gì
        // đang chọn trên trang, kể cả chưa bấm Lưu.
        ganGiaTriAnVaoForm();
        document.getElementById('hiddenGuiDuyet').value = 'false';
        document.getElementById('hiddenXuatExcelSauKhiLuu').value = 'true';

        // Gửi kèm số liệu ĐANG HIỆN TRÊN TRANG (mục 2/3/4, kể cả số vừa gõ trong ô Sửa mà chưa
        // bấm Lưu) để Excel ra y hệt màn hình.
        // Mục 2 (gia vị) KHÔNG gửi từ trang: bảng gia vị trên trang chỉ tính lúc tải trang theo sĩ số ĐÃ LƯU,
        // nên nếu vừa gõ sĩ số mới rồi bấm Xuất Excel thì số trên trang đang cũ (VD = 0) và sẽ ghi đè sai.
        // Server tự tính gia vị theo sĩ số vừa lưu (Luu chạy trước khi xuất) + các dòng đã Sửa/Xóa.
        document.getElementById('hiddenExcelGiaViJson').value = '';
        // Trang chỉ để XEM: Excel luôn xuất từ hệ thống (giống nút Áp dụng ở trang Lịch sử), không gửi số trên trang.
        document.getElementById('hiddenExcelSangJson').value = '';
        document.getElementById('hiddenExcelChinhJson').value = '';
        document.getElementById('hiddenLuuCaTuan').value = 'false';
        // + bản nháp chưa lưu của TẤT CẢ ngày (Thứ 2 → Thứ 7) và cơ sở — phải đọc TRƯỚC khi xóa draft.
        document.getElementById('hiddenDraftTuanJson').value = JSON.stringify(layDraftCaTuan());
        var chkCanDoi = document.getElementById('chkCanDoiChenhLech');
        document.getElementById('hiddenCanDoiChenhLech').value = (!chkCanDoi || chkCanDoi.checked) ? 'true' : 'false';

        // Chỉ xóa bản nháp khi xuất thành công — nếu báo thiếu Bảng giá thì giữ nguyên để không mất số đã gõ.
        window.xuatExcelAjax(document.getElementById('form-chi-tiet-ngay-luu')).then(function (ok) { if (ok) xoaDraftHienTai(); });
    }
    window.chiTietNgayXuatExcel = chiTietNgayXuatExcel;

    // ---- Gom bản nháp (localStorage) của mọi cơ sở × Thứ 2 → Thứ 7 trong tuần đang xem ----
    function soHoacNull(v) {
        if (v === undefined || v === null || v === '') return null;
        var n = Number(v);
        return isNaN(n) ? null : n;
    }

    // Duyệt mọi (cơ sở × Thứ 2 → Thứ 7) của tuần chứa ngày đang xem.
    function duyetCaTuan(callback) {
        var tt = layThongTinNgayCoSo();
        var p = tt.ngay.split('-');
        if (p.length !== 3) return;
        var goc = new Date(Date.UTC(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10)));
        var lechVeThuHai = (goc.getUTCDay() + 6) % 7; // Thứ 2 = 0
        layDanhSachCoSoId().forEach(function (coSoId) {
            for (var i = 0; i < 6; i++) {
                var d = new Date(goc.getTime() + (i - lechVeThuHai) * 86400000);
                callback(coSoId, d.toISOString().slice(0, 10));
            }
        });
    }

    // Đã lưu cả tuần xuống CSDL -> xóa bản nháp tạm của TẤT CẢ ngày/cơ sở để lần sau mở lại thấy đúng dữ liệu đã lưu.
    function xoaDraftCaTuan() {
        duyetCaTuan(function (coSoId, ngay) {
            try { localStorage.removeItem(khoaDraftCho(coSoId, ngay)); } catch (e) { /* bỏ qua */ }
        });
    }

    function layDraftCaTuan() {
        var kq = [];
        duyetCaTuan(function (coSoId, ngay) {
            {
                var dr = docDraft(khoaDraftCho(coSoId, ngay));
                if (!dr || (dr.luuLuc && Date.now() - dr.luuLuc > DRAFT_HET_HAN_MS)) return;
                kq.push({
                    coSoId: parseInt(coSoId, 10),
                    ngay: ngay,
                    monMauGiao: dr.monMauGiao || null,
                    monNhaTre: dr.monNhaTre || null,
                    soSuatAnSang: soHoacNull(dr.soSuatAnSang),
                    soSuatMauGiao: soHoacNull(dr.soSuatMauGiao),
                    soSuatNhaTre: soHoacNull(dr.soSuatNhaTre),
                    tienAnSang: soHoacNull(dr.tienAnSang),
                    tienMauGiao: soHoacNull(dr.tienMauGiao),
                    tienNhaTre: soHoacNull(dr.tienNhaTre)
                });
            }
        });
        return kq;
    }

    // ---- Đọc số liệu đang hiện trên trang (ưu tiên số trong ô Sửa nếu có) ----
    function soTuO(form, ten, macDinh) {
        var el = form ? form.querySelector('[name=' + ten + ']') : null;
        if (!el || el.value === '') return macDinh || 0;
        var v = parseFloat(el.value);
        return isNaN(v) ? (macDinh || 0) : v;
    }

    function layGiaViTrenTrang() {
        var kq = [];
        document.querySelectorAll('#bang-gia-vi tbody tr[id$="-sua"]').forEach(function (dongSua) {
            var rid = dongSua.id.replace(/-sua$/, '');
            var dongXem = document.getElementById(rid + '-xem');
            var form = dongSua.querySelector('form');
            if (!dongXem || !form) return;
            kq.push({
                hangHoaId: parseInt(form.querySelector('[name=hangHoaId]').value, 10),
                tenHang: dongXem.cells[0].textContent.trim(),
                dvt: dongXem.cells[1].textContent.trim(),
                donGia: soTuO(form, 'donGia'),
                slAnSang: soTuO(form, 'slAnSang'),
                slNhaTre: soTuO(form, 'slNhaTre'),
                slMauGiao: soTuO(form, 'slMauGiao')
            });
        });
        return kq;
    }

    function laySangTrenTrang() {
        return duLieuSang.map(function (r) {
            var form = document.querySelector('#sang-' + r.hangHoaId + '-sua form');
            return {
                hangHoaId: r.hangHoaId, tenHang: r.tenHang, dvt: r.dvt || '', tenMon: r.tenMon || '',
                donGia: r.donGia || 0, soLuong: soTuO(form, 'soLuong', r.soLuong)
            };
        });
    }

    function layChinhTrenTrang() {
        return duLieuChinh.map(function (r) {
            var form = document.querySelector('#chinh-' + r.hangHoaId + '-sua form');
            return {
                hangHoaId: r.hangHoaId, tenHang: r.tenHang, dvt: r.dvt || '', tenMon: r.tenMon || '',
                donGia: r.donGia || 0,
                slNhaTre: soTuO(form, 'slNhaTre', r.slNhaTre),
                slMauGiao: soTuO(form, 'slMauGiao', r.slMauGiao)
            };
        });
    }

    function ganGiaTriAnVaoForm() {
        document.getElementById('hiddenMonMauGiaoJson').value = JSON.stringify(layMonTheoBua('.mon-mau-giao'));
        document.getElementById('hiddenMonNhaTreJson').value = JSON.stringify(layMonTheoBua('.mon-nha-tre'));
        document.getElementById('hiddenSoSuatAnSang').value = document.getElementById('soSuatAnSang').value || '0';
        document.getElementById('hiddenSoSuatMauGiao').value = document.getElementById('soSuatMauGiao').value || '0';
        document.getElementById('hiddenSoSuatNhaTre').value = document.getElementById('soSuatNhaTre').value || '0';
        document.getElementById('hiddenTienAnSang').value = document.getElementById('tienAnSang').value || '0';
        document.getElementById('hiddenTienMauGiao').value = document.getElementById('tienMauGiao').value || '0';
        document.getElementById('hiddenTienNhaTre').value = document.getElementById('tienNhaTre').value || '0';
    }

    function chiTietNgayLuu(guiDuyet) {
        ganGiaTriAnVaoForm();
        document.getElementById('hiddenGuiDuyet').value = guiDuyet ? 'true' : 'false';
        document.getElementById('hiddenXuatExcelSauKhiLuu').value = 'false';
        document.getElementById('hiddenExcelGiaViJson').value = '';
        document.getElementById('hiddenExcelSangJson').value = '';
        document.getElementById('hiddenExcelChinhJson').value = '';
        // "Lưu nháp" = lưu CẢ TUẦN (Thứ 2 → Thứ 7) của cả 3 cơ sở: ngày đang xem lấy theo màn hình,
        // các ngày/cơ sở khác lấy theo bản nháp trình duyệt đang giữ.
        document.getElementById('hiddenDraftTuanJson').value = JSON.stringify(layDraftCaTuan());
        document.getElementById('hiddenLuuCaTuan').value = 'true';
        // Đã lưu chính thức xuống CSDL — xóa draft tạm để lần sau mở lại thấy đúng dữ liệu đã lưu.
        xoaDraftHienTai();
        xoaDraftCaTuan();
        document.getElementById('form-chi-tiet-ngay-luu').submit();
    }
    window.chiTietNgayLuu = chiTietNgayLuu;

    // Mục 2 (Gia vị hằng ngày): bấm "Sửa" ở 1 dòng -> ẩn dòng hiển thị (chữ), hiện dòng
    // sửa (ô nhập) ngay bên dưới đúng hàng hoá đó; bấm "Hủy" ở dòng sửa thì đảo ngược lại.
    // Chỉ đổi trên màn hình (chưa lưu gì) — lưu thật sự là bấm "Lưu" trong dòng sửa, submit
    // y hệt 1 form thường (không AJAX), để không phải tự xử lý CSRF token cho JS.
    function chiTietNgayToggleSuaGiaVi(rid) {
        var dongXem = document.getElementById(rid + '-xem');
        var dongSua = document.getElementById(rid + '-sua');
        if (!dongXem || !dongSua) return;
        var dangSua = dongSua.style.display !== 'none';
        dongSua.style.display = dangSua ? 'none' : '';
        dongXem.style.display = dangSua ? '' : 'none';
    }
    window.chiTietNgayToggleSuaGiaVi = chiTietNgayToggleSuaGiaVi;

    // ---- Áp món Mẫu giáo sang Nhà trẻ: chọn 1 món ở bữa nào bên Mẫu giáo thì bữa CÙNG TÊN bên
    // Nhà trẻ tự hiện đúng món đó (Nhà trẻ vẫn đổi riêng được sau đó — trừ bữa SÁNG luôn giống
    // Mẫu giáo vì dùng chung 1 món/1 sĩ số). Bữa Nhà trẻ không có (VD XÀO) hoặc không có món đó
    // trong danh sách của bữa Nhà trẻ thì bỏ qua. ----
    function chepMonMauGiaoSangNhaTre(mgSel) {
        var ten = mgSel.getAttribute('data-ten-bua');
        // Thực đơn gốc: món XẾ của Mẫu giáo = món CHÍNH CHIỀU của Nhà trẻ (ô XẾ Nhà trẻ chỉ có sữa — tính tự động).
        var tenNt = (ten === 'XẾ') ? 'CHÍNH CHIỀU' : ten;
        var nt = Array.prototype.filter.call(document.querySelectorAll('.mon-nha-tre'), function (s) {
            return s.getAttribute('data-ten-bua') === tenNt;
        })[0];
        if (!nt) return false;
        var coMon = mgSel.value === '' || Array.prototype.some.call(nt.options, function (o) { return o.value === mgSel.value; });
        if (!coMon) return false;
        nt.value = mgSel.value;
        nt.dispatchEvent(new Event('mon-dong-bo'));
        return true;
    }

    // Món chiều: XẾ của Mẫu giáo = CHÍNH CHIỀU của Nhà trẻ. Bên nào đang TRỐNG thì lấy theo bên kia
    // (tránh chỉ chọn 1 bên rồi bên còn lại thiếu món -> "Chưa cân đối" khi xuất Excel).
    function boSungMonChieu() {
        var mg = document.querySelector('.mon-mau-giao[data-ten-bua="XẾ"]');
        var nt = document.querySelector('.mon-nha-tre[data-ten-bua="CHÍNH CHIỀU"]');
        if (!mg || !nt) return;
        function coOption(sel, v) { return Array.prototype.some.call(sel.options, function (o) { return o.value === v; }); }
        if (mg.value === '' && nt.value !== '' && coOption(mg, nt.value)) {
            mg.value = nt.value; mg.dispatchEvent(new Event('mon-dong-bo'));
        } else if (nt.value === '' && mg.value !== '' && coOption(nt, mg.value)) {
            nt.value = mg.value; nt.dispatchEvent(new Event('mon-dong-bo'));
        }
    }

    function dongBoSangNhaTre() {
        var mg = document.querySelector('.mon-mau-giao[data-ten-bua="SÁNG"]');
        if (mg) chepMonMauGiaoSangNhaTre(mg);
    }

    // ---- Ô chọn món có thể gõ tìm kiếm — bọc quanh mỗi <select class="mon-mau-giao"/
    // "mon-nha-tre"> 1 ô input lọc theo tên món, ẩn <select> gốc đi (giữ nguyên trong
    // DOM để mọi chỗ khác đọc .value bình thường, không phải sửa gì thêm). Chọn 1 món
    // trong danh sách lọc sẽ set lại select.value rồi tự bắn sự kiện 'change' — coi như
    // người dùng chọn thẳng trên <select>, nên draft/tính toán vẫn chạy như cũ. ----
    function enhanceSearchableSelect(select) {
        if (select.dataset.enhanced === '1') return;
        select.dataset.enhanced = '1';

        var wrap = document.createElement('div');
        wrap.className = 'mon-search-wrap';
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('mon-search-native');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'mon-search-input';
        input.setAttribute('autocomplete', 'off');
        input.placeholder = '-- chưa chọn --';
        wrap.appendChild(input);

        var list = document.createElement('div');
        list.className = 'mon-search-list';
        wrap.appendChild(list);

        function dsTuyChon() {
            return Array.prototype.slice.call(select.options);
        }

        function capNhatInputTheoChon() {
            var op = select.options[select.selectedIndex];
            input.value = (op && op.value) ? op.textContent : '';
        }

        // Cho phép đồng bộ giá trị từ ngoài (bữa SÁNG Nhà trẻ theo Mẫu giáo) mà vẫn cập nhật ô hiển thị.
        select.addEventListener('mon-dong-bo', capNhatInputTheoChon);
        if (select.dataset.dongBo === '1') {
            input.readOnly = true;
            input.title = 'Tự lấy theo món sáng của Mẫu giáo';
            input.style.cursor = 'default';
            capNhatInputTheoChon();
            return; // ô chỉ để xem — không gắn danh sách chọn
        }

        function dongDanhSach() {
            list.style.display = 'none';
            list.innerHTML = '';
        }

        // Chuẩn hóa Unicode về NFC trước khi so sánh — gõ tiếng Việt có dấu trên một số
        // trình duyệt/hệ điều hành (đặc biệt Mac + bộ gõ Unikey) tạo ra dấu ở DẠNG TỔ HỢP
        // (chữ cái + dấu rời, Unicode NFD), trong khi chữ lấy từ CSDL thường ở DẠNG DỰNG
        // SẴN (NFC) — 2 chuỗi trông giống hệt nhau nhưng so sánh bằng máy lại khác nhau,
        // khiến gõ "Bánh"/"Bún" không lọc ra kết quả dù gõ "B" một chữ vẫn ra (vì chữ cái
        // không dấu thì NFC/NFD giống nhau). Gọi .normalize('NFC') cho cả từ khóa lẫn tên
        // món để luôn so sánh cùng 1 dạng.
        // Tìm KHÔNG PHÂN BIỆT DẤU và khớp từng từ bất kể thứ tự: gõ "pho ga", "Phở gà", "ga pho" đều ra "Phở gà".
        function chuanHoa(s) {
            return (s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
                .replace(/đ/g, 'd').replace(/Đ/g, 'd').toLowerCase();
        }

        function moDanhSach(tuKhoaGoc) {
            var tuKhoa = chuanHoa(tuKhoaGoc).trim();
            var cacTu = tuKhoa ? tuKhoa.split(/\s+/) : [];
            var ds = dsTuyChon().filter(function (op) {
                if (!op.value && tuKhoa) return false; // gõ tìm thì ẩn dòng "-- chưa chọn --"
                var ten = chuanHoa(op.textContent);
                return cacTu.every(function (t) { return ten.indexOf(t) !== -1; });
            });

            list.innerHTML = '';
            if (ds.length === 0) {
                var trong = document.createElement('div');
                trong.className = 'mon-search-empty';
                trong.textContent = 'Không có món phù hợp';
                list.appendChild(trong);
            } else {
                ds.forEach(function (op) {
                    var dong = document.createElement('div');
                    dong.className = 'mon-search-item' + (op.value === select.value ? ' active' : '');
                    dong.textContent = op.value ? op.textContent : '-- chưa chọn --';
                    dong.addEventListener('mousedown', function (e) {
                        e.preventDefault(); // giữ focus input để blur không chạy trước khi click kịp bắt
                        select.value = op.value;
                        capNhatInputTheoChon();
                        dongDanhSach();
                        select.dispatchEvent(new Event('change', { bubbles: true }));
                    });
                    list.appendChild(dong);
                });
            }
            list.style.display = 'block';
        }

        input.addEventListener('focus', function () { moDanhSach(''); input.select(); });
        input.addEventListener('input', function () { moDanhSach(input.value); });
        input.addEventListener('blur', function () {
            // Trễ 1 nhịp để 'mousedown' ở trên kịp chạy trước khi danh sách bị đóng.
            setTimeout(function () {
                dongDanhSach();
                capNhatInputTheoChon(); // gõ dở không chọn gì thì quay lại đúng món đang chọn
            }, 150);
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { dongDanhSach(); capNhatInputTheoChon(); input.blur(); }
        });

        capNhatInputTheoChon();
    }

    function enhanceAllSearchableSelects() {
        document.querySelectorAll('.mon-mau-giao, .mon-nha-tre').forEach(enhanceSearchableSelect);
    }

    // ---- Nút ngẫu nhiên / xóa món ----
    function datMonLenManHinh(monMg, monNt) {
        function dat(selector, map) {
            document.querySelectorAll(selector).forEach(function (sel) {
                var v = map[sel.getAttribute('data-bua-id')];
                sel.value = (v == null) ? '' : String(v);
                sel.dispatchEvent(new Event('mon-dong-bo'));
            });
        }
        dat('.mon-mau-giao', monMg || {});
        dat('.mon-nha-tre', monNt || {});
        luuDraftHienTai();
        guiTinhToan();
    }

    // Ghi bản nháp (chỉ phần món) cho 1 ngày của MỌI cơ sở (món dùng chung các cơ sở), giữ nguyên sĩ số/tiền đã có trong nháp.
    function ghiDraftMonChoNgay(ngay, monMg, monNt) {
        layDanhSachCoSoId().forEach(function (coSoId) {
            var dr = docDraft(khoaDraftCho(coSoId, ngay)) || {};
            dr.monMauGiao = monMg;
            dr.monNhaTre = monNt;
            dr.luuLuc = Date.now();
            ghiDraft(khoaDraftCho(coSoId, ngay), dr);
        });
    }

    function layTatCaBuaId(selector) {
        var kq = [];
        document.querySelectorAll(selector).forEach(function (sel) { kq.push(sel.getAttribute('data-bua-id')); });
        return kq;
    }

    function chiTietNgayNgauNhien() {
        var tt = layThongTinNgayCoSo();
        fetch('/chi-tiet-ngay/ngau-nhien?coSoId=' + encodeURIComponent(tt.coSoId) + '&ngay=' + encodeURIComponent(tt.ngay))
            .then(function (r) { return r.json(); })
            .then(function (kq) {
                if (!kq.ok) { alert(kq.loi || 'Không tạo được.'); return; }
                datMonLenManHinh(kq.monMauGiao, kq.monNhaTre);
            })
            .catch(function (err) { console.error(err); alert('Lỗi khi tạo ngẫu nhiên.'); });
    }
    window.chiTietNgayNgauNhien = chiTietNgayNgauNhien;

    function chiTietNgayNgauNhienCaTuan() {
        if (!confirm('Tạo ngẫu nhiên thực đơn cả tuần (Thứ 2 → Thứ 7) từ tuần gần nhất trong lịch sử? Món đang chọn của cả tuần sẽ bị thay (chưa ghi vào hệ thống đến khi bấm Lưu nháp).')) return;
        var tt = layThongTinNgayCoSo();
        fetch('/chi-tiet-ngay/ngau-nhien-ca-tuan?coSoId=' + encodeURIComponent(tt.coSoId) + '&ngay=' + encodeURIComponent(tt.ngay))
            .then(function (r) { return r.json(); })
            .then(function (kq) {
                if (!kq.ok) { alert(kq.loi || 'Không tạo được.'); return; }
                var homNay = null;
                kq.cacNgay.forEach(function (d) {
                    if (d.ngay === tt.ngay) homNay = d; else ghiDraftMonChoNgay(d.ngay, d.monMauGiao, d.monNhaTre);
                });
                if (homNay) datMonLenManHinh(homNay.monMauGiao, homNay.monNhaTre);
                alert('Đã tạo ngẫu nhiên cả tuần (lấy từ tuần ' + kq.tuan + '). Bấm các tab Thứ 2 → Thứ 7 để xem, rồi bấm "Lưu nháp cả tuần" để ghi vào hệ thống.');
            })
            .catch(function (err) { console.error(err); alert('Lỗi khi tạo ngẫu nhiên cả tuần.'); });
    }
    window.chiTietNgayNgauNhienCaTuan = chiTietNgayNgauNhienCaTuan;

    function chiTietNgayXoaMonNgay() {
        if (!confirm('Xóa món đã chọn của ngày này (chưa ghi vào hệ thống đến khi bấm Lưu nháp)?')) return;
        datMonLenManHinh({}, {});
    }
    window.chiTietNgayXoaMonNgay = chiTietNgayXoaMonNgay;

    function chiTietNgayXoaMonCaTuan() {
        if (!confirm('Xóa món đã chọn của CẢ TUẦN (Thứ 2 → Thứ 7, mọi cơ sở)? Chưa ghi vào hệ thống đến khi bấm Lưu nháp.')) return;
        var tt = layThongTinNgayCoSo();
        var rongMg = {}, rongNt = {};
        layTatCaBuaId('.mon-mau-giao').forEach(function (id) { rongMg[id] = null; });
        layTatCaBuaId('.mon-nha-tre').forEach(function (id) { rongNt[id] = null; });
        duyetCaTuan(function (coSoId, ngay) {
            if (ngay === tt.ngay) return;
            var dr = docDraft(khoaDraftCho(coSoId, ngay)) || {};
            dr.monMauGiao = rongMg; dr.monNhaTre = rongNt; dr.luuLuc = Date.now();
            ghiDraft(khoaDraftCho(coSoId, ngay), dr);
        });
        datMonLenManHinh({}, {});
    }
    window.chiTietNgayXoaMonCaTuan = chiTietNgayXoaMonCaTuan;

    document.addEventListener('DOMContentLoaded', function () {
        var btnXem = document.getElementById('btn-xem');
        if (btnXem) btnXem.addEventListener('click', xemLai);

        if (document.getElementById('khoiIdMauGiao')) {
            // Áp draft (nếu có) TRƯỚC khi tính toán lần đầu, để bảng nguyên liệu hiện
            // đúng theo lựa chọn dang dở, không phải theo dữ liệu gốc từ server.
            taiDraftNeuCo();
            dongBoSangNhaTre(); // món sáng Nhà trẻ = món sáng Mẫu giáo
            boSungMonChieu();   // XẾ Mẫu giáo <-> CHÍNH CHIỀU Nhà trẻ: bên trống lấy theo bên kia

            // Biến các <select> chọn món thành ô gõ tìm kiếm được — làm SAU taiDraftNeuCo()
            // để ô input hiển thị đúng ngay từ đầu theo giá trị đã khôi phục (nếu có).
            enhanceAllSearchableSelects();

            document.querySelectorAll('.mon-mau-giao').forEach(function (sel) {
                sel.addEventListener('change', function () {
                    chepMonMauGiaoSangNhaTre(sel); // chọn món Mẫu giáo -> Nhà trẻ hiện giống
                    luuDraftHienTai();
                    guiTinhToan();
                });
            });
            document.querySelectorAll('.mon-nha-tre').forEach(function (sel) {
                sel.addEventListener('change', function () {
                    boSungMonChieu();
                    luuDraftHienTai();
                    guiTinhToan();
                });
            });
            var btnApDung = document.getElementById('btn-ap-dung-mg-nt');
            if (btnApDung) {
                btnApDung.addEventListener('click', function () {
                    document.querySelectorAll('.mon-mau-giao').forEach(chepMonMauGiaoSangNhaTre);
                    luuDraftHienTai();
                    guiTinhToan();
                });
            }
            ['soSuatAnSang', 'soSuatMauGiao', 'soSuatNhaTre'].forEach(function (id) {
                document.getElementById(id).addEventListener('change', function () {
                    luuDraftHienTai();
                    guiTinhToan();
                });
            });
            // Tiền/cháu không ảnh hưởng tới bảng nguyên liệu (chỉ dùng khi Lưu), nên chỉ
            // cần lưu draft, không cần gọi lại /tinh-toan.
            ['tienAnSang', 'tienMauGiao', 'tienNhaTre'].forEach(function (id) {
                var el = document.getElementById(id);
                if (el) el.addEventListener('change', luuDraftHienTai);
            });

            guiTinhToan(); // tính ngay khi mở trang (đã tính theo draft vừa áp ở trên, nếu có)
        }
    });
})();
