(function () {
    'use strict';
    // Bảng thực đơn tuần ở TRANG CHỦ — giao diện giống trang thực đơn cho phụ huynh, CHỈ ĐỂ XEM (không chọn món trên bảng).
    // Nguồn dữ liệu = các ô ẩn .dashboard-chon-mon (giữ nguyên cho Lưu nháp / Ngẫu nhiên / Xóa / Xuất Excel); bảng tự vẽ lại mỗi khi chúng đổi.
    // Bấm tiêu đề ngày để mở Chi tiết đúng ngày đó (như cũ).

    var goc = document.getElementById('bt-goc');
    if (!goc) return;
    var ngayChon = 0;
    var khoiChon = 'nhaTre';
    var data = null;

    function el(id) { return document.getElementById(id); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function boDau(s) {
        return String(s || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd');
    }

    // Hình minh họa TỰ ĐỘNG theo tên món: thư viện biểu tượng dùng chung (không tạo ảnh mới mỗi lần). Khớp từ trên xuống, không khớp thì dùng 🍽️.
    var BIEU_TUONG = [
        ['sua chua', '🥛'], ['yakult', '🥛'], ['yakul', '🥛'], ['sua', '🥛'],
        ['flan', '🍮'], ['kem', '🍨'], ['banh su', '🧁'], ['banh mi', '🥖'], ['banh canh', '🍜'], ['banh', '🥮'],
        ['chao', '🥣'], ['sup', '🥣'],
        ['bun', '🍜'], ['pho', '🍜'], ['mien', '🍜'], ['nui', '🍜'], ['my ', '🍜'], ['mi ', '🍜'], ['hu tieu', '🍜'],
        ['com', '🍚'], ['xoi', '🍚'],
        ['canh', '🍲'], ['lau', '🍲'],
        ['ca ', '🐟'], ['tom', '🦐'], ['muc', '🦑'], ['cua', '🦀'], ['ngheu', '🐚'], ['so ', '🐚'], ['luon', '🐍'],
        ['ga ', '🍗'], ['vit', '🦆'], ['bo ', '🥩'], ['heo', '🍖'], ['thit', '🍖'], ['suon', '🍖'], ['cha ', '🍢'], ['trung', '🥚'],
        ['dau hu', '🫘'], ['dau ', '🫘'],
        ['nam', '🍄'], ['ca rot', '🥕'], ['khoai tay', '🥔'], ['khoai', '🍠'], ['bap', '🌽'], ['ca chua', '🍅'],
        ['bi do', '🎃'], ['bi dao', '🥒'], ['dua leo', '🥒'], ['bau', '🥒'], ['muop', '🥒'], ['lo xanh', '🥦'], ['su lo', '🥦'],
        ['rau', '🥬'], ['cai', '🥬'], ['xa lach', '🥬'], ['bong cai', '🥦'],
        ['chuoi', '🍌'], ['dua hau', '🍉'], ['thanh long', '🐉'], ['dua luoi', '🍈'], ['nho', '🍇'], ['cam', '🍊'], ['quyt', '🍊'],
        ['tao', '🍎'], ['le ', '🍐'], ['xoai', '🥭'], ['thom', '🍍'], ['du du', '🧡'], ['oi', '🍐'], ['mang cut', '🍑'], ['buoi', '🍊'], ['dua', '🥥'],
        ['nuoc', '🧃']
    ];
    function bieuTuong(ten) {
        var s = ' ' + boDau(ten) + ' ';
        for (var i = 0; i < BIEU_TUONG.length; i++) {
            var k = BIEU_TUONG[i][0];
            if (s.indexOf(k.charAt(k.length - 1) === ' ' ? ' ' + k : k) >= 0) return BIEU_TUONG[i][1];
        }
        return '🍽️';
    }
    var EMOJI_BUA = { sang: '🌅', phusang: '🥤', trua: '🍚', chieu: '🍲', phuchieu: '🥛' };
    var TEN_NHOM = { sang: 'BUỔI SÁNG', phusang: 'PHỤ SÁNG', trua: 'BUỔI TRƯA', chieu: 'CHÍNH CHIỀU', phuchieu: 'PHỤ CHIỀU' };
    var THU = ['Thứ Hai', 'Thứ Ba', 'Thứ Tư', 'Thứ Năm', 'Thứ Sáu', 'Thứ Bảy'];

    // Tên bữa trong hệ thống (loai_bua_an) -> nhóm hiển thị + nhãn loại món (giống phần công khai cho phụ huynh).
    var NHOM_THEO_BUA = {
        'SÁNG': ['sang', ''], 'PHỤ SÁNG': ['phusang', ''],
        'CANH': ['trua', 'Canh'], 'MẶN': ['trua', 'Mặn'], 'XÀO': ['trua', 'Xào'], 'TRÁNG MIỆNG': ['trua', 'Tráng miệng'], 'CẢI THIỆN': ['trua', 'Cải thiện'],
        'CHÍNH CHIỀU': ['chieu', ''], 'PHỤ CHIỀU': ['phuchieu', ''], 'SỮA': ['phuchieu', '']
    };
    function nhomCuaBua(tenBua, khoi) {
        var k = String(tenBua || '').trim().toUpperCase();
        if (k === 'XẾ') return khoi === 'nhaTre' ? ['phuchieu', ''] : ['chieu', ''];   // Nhà trẻ: xế = sữa (phụ chiều); Mẫu giáo: xế = món chiều
        return NHOM_THEO_BUA[k] || ['trua', String(tenBua || '')];
    }

    function homNayIso() {
        var d = new Date();
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }
    function congNgay(iso, n) {
        var p = iso.split('-');
        var d = new Date(Date.UTC(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10) + n));
        return { iso: d.getUTCFullYear() + '-' + String(d.getUTCMonth() + 1).padStart(2, '0') + '-' + String(d.getUTCDate()).padStart(2, '0'),
                 ngan: String(d.getUTCDate()).padStart(2, '0') + '/' + String(d.getUTCMonth() + 1).padStart(2, '0') };
    }

    // ---- gom dữ liệu: danh sách ô { khoi, bua, ngay(0-5), ten } -> mô hình giống JSON trang phụ huynh ----
    function xayDuLieu(cacO, tuanIso, thu7Nghi, phu, phuChieuMacDinh) {
        var ngay = [];
        for (var i = 0; i < 6; i++) {
            var c = congNgay(tuanIso, i);
            ngay.push({ thu: THU[i], ngan: c.ngan, iso: c.iso, nghi: i === 5 && !!thu7Nghi, nhaTre: [], mauGiao: [] });
        }
        cacO.forEach(function (o) {
            if (!o.ten || o.ngay < 0 || o.ngay > 5) return;
            var d = ngay[o.ngay];
            var ds = o.khoi === 'nhaTre' ? d.nhaTre : d.mauGiao;
            var nh = nhomCuaBua(o.bua, o.khoi);
            var bua = ds.filter(function (b) { return b.ma === nh[0]; })[0];
            if (!bua) { bua = { ma: nh[0], ten: TEN_NHOM[nh[0]], mon: [] }; ds.push(bua); }
            bua.mon.push({ ten: o.ten, loai: nh[1] });
        });
        // Phụ sáng / Phụ chiều (lưu riêng, dùng chung 2 khối) — cùng quy tắc ghép với trang phụ huynh:
        //  · phụ sáng: có chữ thì hiện;  · phụ chiều: ngày đã lưu -> dùng bản lưu (thay "Xế" sữa của Nhà trẻ); chưa lưu -> mặc định, không đè Xế đã soạn.
        ngay.forEach(function (d, i) {
            if (d.nghi) return;
            var p = (phu && phu[i]) || { sang: '', chieu: '', daLuu: false };
            [d.nhaTre, d.mauGiao].forEach(function (ds) {
                if (p.sang) ds.push({ ma: 'phusang', ten: TEN_NHOM.phusang, mon: [{ ten: p.sang, loai: '' }] });
                var chieu = p.daLuu ? p.chieu : phuChieuMacDinh;
                if (!chieu) return;
                var vt = -1;
                ds.forEach(function (b, k) { if (b.ma === 'phuchieu') vt = k; });
                if (vt >= 0) { if (!p.daLuu) return; ds.splice(vt, 1); }
                ds.push({ ma: 'phuchieu', ten: TEN_NHOM.phuchieu, mon: [{ ten: chieu, loai: '' }] });
            });
        });

        var thuTu = ['sang', 'phusang', 'trua', 'chieu', 'phuchieu'];
        ngay.forEach(function (d) {
            [d.nhaTre, d.mauGiao].forEach(function (ds) {
                ds.sort(function (a, b) { return thuTu.indexOf(a.ma) - thuTu.indexOf(b.ma); });
            });
        });
        return ngay;
    }

    function docCacO() {
        var kq = [];
        document.querySelectorAll('.dashboard-chon-mon').forEach(function (s) {
            kq.push({
                khoi: s.getAttribute('data-khoi'),
                bua: s.getAttribute('data-bua-ten'),
                ngay: parseInt(s.getAttribute('data-ngay'), 10),
                ten: s.value ? (s.getAttribute('data-ten') || '') : ''
            });
        });
        return kq;
    }

    function dongMon(m) {
        return '<div class="ph-mon"><span class="e">' + bieuTuong(m.ten) + '</span><span>' +
            (m.loai ? '<span class="l">' + esc(m.loai) + '</span>' : '') + esc(m.ten) + '</span></div>';
    }

    // ---- bảng nguyên tuần (máy tính): 1 khối đang chọn, mỗi ngày 1 cột ----
    function veBangTuan() {
        var hn = homNayIso();
        var kh = khoiChon === 'nhaTre' ? 'nhaTre' : 'mauGiao';
        var cacBua = ['sang', 'phusang', 'trua', 'chieu', 'phuchieu'];
        var h = '<table class="ph-bang"><thead><tr><th class="ph-nhan"></th>';
        data.forEach(function (d, i) {
            h += '<th class="ph-ngay-dau mo-chi-tiet d' + i + (d.iso === hn ? ' homnay' : '') + '" data-mo-ngay-bt="' + d.iso + '" title="Bấm để xem chi tiết ngày này">' + esc(d.thu) +
                '<small>' + esc(d.ngan) + (d.iso === hn ? ' · Hôm nay' : '') + '</small></th>';
        });
        h += '</tr></thead><tbody>';
        cacBua.forEach(function (ma, r) {
            h += '<tr><th class="ph-nhan">' + EMOJI_BUA[ma] + '<span>' + esc(TEN_NHOM[ma]) + '</span></th>';
            data.forEach(function (d, i) {
                var td = ' d' + i + (d.iso === hn ? ' homnay' : '');
                if (d.nghi) { if (r === 0) h += '<td class="ph-nghi' + td + '" rowspan="' + cacBua.length + '">Nghỉ</td>'; return; }
                var bua = d[kh].filter(function (x) { return x.ma === ma; })[0];
                if (!bua) h += '<td class="ph-trong' + td + '">–</td>';
                else h += '<td class="' + td.trim() + '">' + bua.mon.map(dongMon).join('') + '</td>';
            });
            h += '</tr>';
        });
        h += '</tbody></table>';
        el('ph-may-tinh').innerHTML = h;
    }

    // ---- từng ngày (điện thoại / chế độ "Cả hai") ----
    function veChonNgay() {
        var hn = homNayIso();
        el('ph-chon-ngay').innerHTML = data.map(function (d, i) {
            return '<button type="button" class="ph-chip d' + i + (i === ngayChon ? ' chon' : '') + (d.iso === hn ? ' homnay' : '') +
                '" data-i="' + i + '"><b>T' + (i + 2) + '</b><small>' + esc(d.ngan) + '</small></button>';
        }).join('');
        el('ph-chon-khoi').querySelectorAll('button').forEach(function (b) {
            b.classList.toggle('chon', b.getAttribute('data-khoi') === khoiChon);
        });
    }

    function khoiHtml(d, kh) {
        var ds = d[kh];
        if (d.nghi) return '<div class="ph-the-trong">Ngày nghỉ</div>';
        if (!ds.length) return '<div class="ph-the-trong">Chưa có thực đơn.</div>';
        return ds.map(function (b) {
            return '<div class="ph-the-bua"><h3>' + EMOJI_BUA[b.ma] + ' ' + esc(b.ten) + '</h3>' + b.mon.map(dongMon).join('') + '</div>';
        }).join('');
    }

    function veTheNgay() {
        var d = data[ngayChon];
        var box = el('ph-the-ngay');
        box.className = 'ph-the-ngay d' + ngayChon;
        var ten = { nhaTre: 'Nhà trẻ', mauGiao: 'Mẫu giáo', ca: 'Nhà trẻ & Mẫu giáo' }[khoiChon];
        var h = '<div class="ph-the-tieu-de">' + esc(d.thu) + '<small>' + esc(d.ngan) + ' · ' + ten + '</small></div>';
        h += '<button type="button" class="ph-the-ct" data-mo-ngay-bt="' + d.iso + '">Xem chi tiết ngày này →</button>';
        if (khoiChon === 'ca') {
            h += '<div class="ph-2khoi">' +
                '<section class="ph-khoi-nt"><h2>🍼 Nhà trẻ</h2>' + khoiHtml(d, 'nhaTre') + '</section>' +
                '<section class="ph-khoi-mg"><h2>🎒 Mẫu giáo</h2>' + khoiHtml(d, 'mauGiao') + '</section></div>';
        } else {
            h += khoiHtml(d, khoiChon);
        }
        box.innerHTML = h;
    }

    function docPhu() {
        var kq = [];
        document.querySelectorAll('.dashboard-phu').forEach(function (s) {
            kq[parseInt(s.getAttribute('data-ngay'), 10)] = {
                sang: (s.getAttribute('data-sang') || '').trim(),
                chieu: (s.getAttribute('data-chieu') || '').trim(),
                daLuu: s.getAttribute('data-da-luu') === 'true'
            };
        });
        return kq;
    }

    function ve() {
        var tuan = document.getElementById('hiddenTuanBatDau').value;
        var chk = document.getElementById('chk-thu7-nghi');
        var kho = document.getElementById('dashboard-du-lieu');
        var macDinh = kho ? (kho.getAttribute('data-phu-chieu-mac-dinh') || '').trim() : '';
        data = xayDuLieu(docCacO(), tuan, chk && chk.checked, docPhu(), macDinh);
        goc.classList.toggle('ph-ca', khoiChon === 'ca');
        veBangTuan(); veChonNgay(); veTheNgay();
    }
    window.dashboardLamMoiBang = ve;

    function chonNgayMacDinh() {
        var hn = homNayIso();
        var i = data.findIndex(function (d) { return d.iso === hn; });
        ngayChon = i >= 0 ? i : 0;
        if (data[ngayChon] && data[ngayChon].nghi) ngayChon = 0;
    }

    // ---- sự kiện ----
    el('ph-chon-ngay').addEventListener('click', function (e) {
        var b = e.target.closest('.ph-chip'); if (!b) return;
        ngayChon = parseInt(b.getAttribute('data-i'), 10); veChonNgay(); veTheNgay();
    });
    el('ph-chon-khoi').addEventListener('click', function (e) {
        var b = e.target.closest('button'); if (!b) return;
        khoiChon = b.getAttribute('data-khoi');
        try { localStorage.setItem('bt-khoi', khoiChon); } catch (x) { }
        ve();
    });
    goc.addEventListener('click', function (e) {
        var t = e.target.closest('[data-mo-ngay-bt]'); if (!t) return;
        if (typeof window.dashboardMoChiTiet === 'function') window.dashboardMoChiTiet(t.getAttribute('data-mo-ngay-bt'));
    });

    try { var k = localStorage.getItem('bt-khoi'); if (k === 'nhaTre' || k === 'mauGiao' || k === 'ca') khoiChon = k; } catch (x) { }
    ve();
    chonNgayMacDinh();
    veChonNgay(); veTheNgay();
})();
