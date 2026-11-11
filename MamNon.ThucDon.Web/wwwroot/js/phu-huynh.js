(function () {
    'use strict';

    var app = document.getElementById('ph-app');
    var api = app.getAttribute('data-api');
    var xemTruoc = app.getAttribute('data-xem-truoc') || '';
    var data = null;
    var ngayChon = 0;
    var khoiChon = 'mauGiao';

    function el(id) { return document.getElementById(id); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function boDau(s) {
        return String(s || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd');
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

    function homNayIso() {
        var d = new Date();
        var m = String(d.getMonth() + 1).padStart(2, '0');
        var n = String(d.getDate()).padStart(2, '0');
        return d.getFullYear() + '-' + m + '-' + n;
    }

    function dongMon(m, nho) {
        return '<div class="ph-mon"><span class="e">' + bieuTuong(m.ten) + '</span><span>' +
            (m.loai ? '<span class="l">' + esc(m.loai) + '</span>' : '') + esc(m.ten) + '</span></div>';
    }

    // ---- bảng nguyên tuần (máy tính): CHỈ 1 khối đang chọn, mỗi ngày 1 cột ----
    function veBangTuan() {
        var hn = homNayIso();
        var kh = khoiChon === 'nhaTre' ? 'nhaTre' : 'mauGiao';   // chế độ 'chitiet' ẩn bảng tuần (CSS .ph-ct)
        // luôn hiện đủ 5 dòng như file Excel của trường; ô chưa có món thì để TRỐNG
        var cacBua = [['sang', 'BUỔI SÁNG'], ['phusang', 'PHỤ SÁNG'], ['trua', 'BUỔI TRƯA'], ['chieu', 'CHÍNH CHIỀU'], ['phuchieu', 'PHỤ CHIỀU']];
        var h = '<table class="ph-bang"><thead><tr><th class="ph-nhan"></th>';
        data.ngay.forEach(function (d, i) {
            h += '<th class="ph-ngay-dau d' + i + (d.ngay === hn ? ' homnay' : '') + '">' + esc(d.thu) +
                '<small>' + esc(d.ngayNgan) + (d.ngay === hn ? ' · Hôm nay' : '') + '</small></th>';
        });
        h += '</tr></thead><tbody>';
        if (cacBua.length === 0) h += '<tr><td colspan="7" class="ph-trong">Chưa có món ăn trong tuần này.</td></tr>';
        cacBua.forEach(function (b, r) {
            h += '<tr><th class="ph-nhan">' + EMOJI_BUA[b[0]] + '<span>' + esc(b[1]) + '</span></th>';
            data.ngay.forEach(function (d, i) {
                var td = ' d' + i + (d.ngay === hn ? ' homnay' : '');
                if (d.nghi) { if (r === 0) h += '<td class="ph-nghi' + td + '" rowspan="' + cacBua.length + '">Nghỉ</td>'; return; }
                var bua = d[kh].filter(function (x) { return x.ma === b[0]; })[0];
                if (!bua) h += '<td class="ph-trong' + td + '"></td>';
                else h += '<td class="' + td.trim() + '">' + bua.mon.map(function (m) { return dongMon(m); }).join('') + '</td>';
            });
            h += '</tr>';
        });
        h += '</tbody></table>';
        el('ph-may-tinh').innerHTML = h;
    }

    // ---- từng ngày (điện thoại) ----
    function nhanThu(t) {
        var so = { 'hai': '2', 'ba': '3', 'tư': '4', 'năm': '5', 'sáu': '6', 'bảy': '7' };
        var w = String(t || '').toLowerCase().split(' ').pop();
        return so[w] ? 'T' + so[w] : t;
    }

    function veChonNgay() {
        var hn = homNayIso();
        el('ph-chon-ngay').innerHTML = data.ngay.map(function (d, i) {
            return '<button type="button" class="ph-chip d' + i + (i === ngayChon ? ' chon' : '') + (d.ngay === hn ? ' homnay' : '') +
                '" data-i="' + i + '"><b>' + esc(nhanThu(d.thu))
                + '</b><small>' + esc(d.ngayNgan) + '</small></button>';
        }).join('');
        el('ph-chon-khoi').querySelectorAll('button').forEach(function (b) {
            b.classList.toggle('chon', b.getAttribute('data-khoi') === khoiChon);
        });
    }

    function khoiHtml(d, kh) {
        var ds = d[kh];
        if (d.nghi) return '<div class="ph-the-trong">Ngày nghỉ</div>';
        if (!ds.length) return '';   // chưa có dữ liệu -> để trống
        return ds.map(function (b) {
            return '<div class="ph-the-bua"><h3>' + EMOJI_BUA[b.ma] + ' ' + esc(b.ten) + '</h3>' +
                b.mon.map(function (m) { return dongMon(m); }).join('') + '</div>';
        }).join('');
    }

    function veTheNgay() {
        var d = data.ngay[ngayChon];
        var box = el('ph-the-ngay');
        box.className = 'ph-the-ngay d' + ngayChon;
        if (khoiChon === 'chitiet') {
            var ct = '<div class="ph-the-tieu-de">' + esc(d.thu) + '<small>' + esc(d.ngayNgan) + ' · Chi tiết nguyên liệu</small></div>';
            if (d.nghi) ct += '<div class="ph-the-trong">Ngày nghỉ</div>';
            else if (d.chiTiet && window.TongHopNgay) ct += '<section class="th-wrap" id="ph-ct"></section>';   // không có file chi tiết -> để trống
            box.innerHTML = ct;
            if (!d.nghi && d.chiTiet && window.TongHopNgay) {
                window.TongHopNgay.render(el('ph-ct'), d.ngay, {
                    data: d.chiTiet,
                    trong: true,   // ô/bảng không có số liệu thì để trống, không hiện chữ "Chưa có..."
                    ghiChu: 'Nguyên liệu, đơn giá và thành tiền trong ngày, theo từng cơ sở (' +
                        (d.chiTiet.coSo || []).map(function (c) { return esc(c.ma || c.ten); }).join(', ') + ').'
                });
            }
            return;
        }
        var ten = { nhaTre: 'Nhà trẻ', mauGiao: 'Mẫu giáo' }[khoiChon];
        var h = '<div class="ph-the-tieu-de">' + esc(d.thu) + '<small>' + esc(d.ngayNgan) + ' · ' + ten + '</small></div>';
        h += khoiHtml(d, khoiChon);
        box.innerHTML = h;
    }

    // Chế độ "Chi tiết": ẩn hẳn bảng tuần và hiện khu từng ngày (trên máy tính CSS đang ẩn khu này) — làm bằng style trực tiếp nên không phụ thuộc CSS cũ còn trong cache.
    function capNhatCheDo() {
        var ct = khoiChon === 'chitiet';
        app.classList.toggle('ph-ct', ct);
        el('ph-may-tinh').style.display = ct ? 'none' : '';
        el('ph-dien-thoai').style.display = ct ? 'block' : '';
    }

    function ve() {
        if (!data || !data.coDuLieu) {
            el('ph-thong-bao').hidden = false;
            el('ph-thong-bao').textContent = 'Nhà trường chưa công khai thực đơn. Phụ huynh vui lòng quay lại sau.';
            el('ph-may-tinh').innerHTML = ''; el('ph-may-tinh').style.display = 'none'; el('ph-dien-thoai').style.display = 'none'; el('ph-chon-khoi').hidden = true;
            el('ph-dieu-huong').hidden = true;
            return;
        }
        el('ph-thong-bao').hidden = true; el('ph-chon-khoi').hidden = false; el('ph-dieu-huong').hidden = false;
        // Nhà trường tắt tab "Chi tiết" (CongKhai:HienChiTiet = false) -> ẩn nút và quay về Mẫu giáo
        var nutCt = el('ph-chon-khoi').querySelector('[data-khoi="chitiet"]');
        if (nutCt) nutCt.hidden = data.hienChiTiet === false;
        if (data.hienChiTiet === false && khoiChon === 'chitiet') khoiChon = 'mauGiao';
        el('ph-tuan').textContent = data.tieuDe;
        el('ph-khoang').textContent = 'Từ ngày ' + data.tuNgay + ' đến ngày ' + data.denNgay;
        el('ph-cap-nhat').textContent = data.capNhat ? 'Cập nhật lúc ' + data.capNhat : '';
        el('ph-truoc').disabled = !data.tuanTruoc;
        el('ph-sau').disabled = !data.tuanSau;
        if (xemTruoc) { el('ph-banner').hidden = false; el('ph-truoc').hidden = true; el('ph-sau').hidden = true; }
        capNhatCheDo(); veBangTuan(); veChonNgay(); veTheNgay();
    }

    function chonNgayMacDinh() {
        var hn = homNayIso();
        var i = data.ngay.findIndex(function (d) { return d.ngay === hn; });
        ngayChon = i >= 0 ? i : 0;
        if (data.ngay[ngayChon] && data.ngay[ngayChon].nghi) ngayChon = 0;
    }

    function tai(tuan) {
        var url = api + (xemTruoc ? '?tuan=' + encodeURIComponent(xemTruoc) : (tuan ? '?tuan=' + encodeURIComponent(tuan) : ''));
        fetch(url, { credentials: 'same-origin', cache: 'no-store' })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (kq) { data = kq; if (data.coDuLieu) chonNgayMacDinh(); ve(); })
            .catch(function () {
                el('ph-thong-bao').hidden = false;
                el('ph-thong-bao').textContent = 'Không tải được thực đơn. Vui lòng thử lại.';
            });
    }

    // ---- sự kiện ----
    el('ph-chon-ngay').addEventListener('click', function (e) {
        var b = e.target.closest('.ph-chip'); if (!b) return;
        ngayChon = parseInt(b.getAttribute('data-i'), 10); veChonNgay(); veTheNgay();
    });
    el('ph-chon-khoi').addEventListener('click', function (e) {
        var b = e.target.closest('button'); if (!b) return;
        khoiChon = b.getAttribute('data-khoi');
        capNhatCheDo();
        try { localStorage.setItem('ph-khoi', khoiChon); } catch (x) { }
        veChonNgay(); veTheNgay(); veBangTuan();
    });
    el('ph-truoc').addEventListener('click', function () { if (data && data.tuanTruoc) tai(data.tuanTruoc); });
    el('ph-sau').addEventListener('click', function () { if (data && data.tuanSau) tai(data.tuanSau); });
    el('ph-chia-se').addEventListener('click', function () {
        var link = location.origin + '/thuc-don-phu-huynh';
        var tieuDe = 'Thực đơn cho trẻ — ' + app.getAttribute('data-ten-truong');
        if (navigator.share) { navigator.share({ title: tieuDe, url: link }).catch(function () { }); return; }
        var xong = function () { el('ph-chia-se').textContent = 'Đã chép link ✓'; setTimeout(function () { el('ph-chia-se').textContent = 'Chia sẻ link'; }, 2500); };
        if (navigator.clipboard) navigator.clipboard.writeText(link).then(xong, function () { window.prompt('Chép link này:', link); });
        else window.prompt('Chép link này:', link);
    });

    // giá trị cũ 'ca' (tab Cả hai đã bỏ) không còn hợp lệ -> giữ mặc định
    try { var k = localStorage.getItem('ph-khoi'); if (k === 'nhaTre' || k === 'mauGiao' || k === 'chitiet') khoiChon = k; } catch (x) { }
    tai('');
})();
