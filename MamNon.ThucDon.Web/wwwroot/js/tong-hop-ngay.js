/* Trang chủ → tab "Cả hai" → "Chi tiết nguyên liệu & giá" của từng ngày.
   Bố cục giống file mẫu "Bảng theo dõi và đánh giá chất lượng thực phẩm": 2 tab Ăn sáng / Ăn trưa;
   mỗi dòng nguyên liệu có Tổng, số lượng từng chợ (TC / PH / ĐT), đơn giá, thành tiền từng chợ, tổng cộng.
   Dữ liệu: GET /chi-tiet-ngay/tong-hop?ngay=yyyy-MM-dd (thực đơn + sĩ số ĐÃ LƯU của 3 cơ sở). */
(function () {
    'use strict';
    var CACHE = {};
    var TAB = 'sang';   // nhớ tab đang xem khi đổi ngày
    try { var t0 = localStorage.getItem('th-tab'); if (t0 === 'sang' || t0 === 'trua') TAB = t0; } catch (x) { }
    var KHOI = 'ca';   // tab con của "Ăn trưa": 'nt' = Nhà trẻ, 'mg' = Mẫu giáo, 'ca' = Cả hai
    try { var k0 = localStorage.getItem('th-khoi-trua'); if (k0 === 'nt' || k0 === 'mg' || k0 === 'ca') KHOI = k0; } catch (x) { }

    var css = document.createElement('style');
    css.textContent =
        '.th-tabs-ngay{margin:8px 16px 4px}.th-tabs-ngay button{padding:8px 20px;font-size:14px}' +
        '.th-wrap{margin:14px 12px 18px;border:1px solid #e3e3e3;border-radius:14px;background:#fff;overflow:hidden}' +
        '.th-dau{display:flex;flex-wrap:wrap;align-items:center;gap:10px;padding:10px 14px;border-bottom:1px solid #eee;background:#fafafa}' +
        '.th-dau h3{margin:0;font-size:15px;font-weight:800;flex:1 1 220px}' +
        '.th-tabs{display:inline-flex;background:#f0f0f0;border-radius:999px;padding:3px}' +
        '.th-tabs button{border:0;background:transparent;font:inherit;font-size:13.5px;font-weight:700;padding:7px 16px;border-radius:999px;cursor:pointer;color:#7a7a7a}' +
        '.th-tabs button.chon{background:#fff;color:#1a8a50;box-shadow:0 1px 3px rgba(0,0,0,.14)}' +
        '.th-tabs-khoi{display:flex;padding:10px 14px 0}.th-tabs-khoi .th-tabs button{padding:6px 16px}' +
        '.th-ghichu{padding:8px 14px;font-size:12.5px;color:#7a7a7a;border-bottom:1px solid #f1f1f1}' +
        '.th-cuon{overflow-x:auto}' +
        '.th-bang{border-collapse:collapse;width:100%;min-width:760px;font-size:13px}' +
        '.th-bang th,.th-bang td{border:1px solid #e6e6e6;padding:6px 8px;text-align:right;vertical-align:middle;white-space:nowrap}' +
        '.th-bang th{background:#f5f8f6;font-size:11.5px;font-weight:800;text-align:center;white-space:normal;line-height:1.25}' +
        '.th-bang td.t{text-align:left;white-space:normal;min-width:150px}' +
        '.th-bang td.c{text-align:center}' +
        '.th-bang tr.th-khoi td{background:#1a8a50;color:#fff;font-weight:800;text-align:left;font-size:13.5px}' +
        '.th-bang tr.th-khoi.mg td{background:#2f9e5d}' +
        '.th-bang tr.th-suat td{background:#fff8e6;font-weight:700}' +
        '.th-bang tr.th-tong td{background:#e9f6ee;font-weight:800}' +
        '.th-bang tr.th-tong-lon td{background:#d6efe0;font-weight:800;font-size:14px}' +
        '.th-bang td.zero{color:#c9c9c9}' +
        '.th-bang td.khacgia{background:#fff3cd}' +
        '.th-trong{padding:20px;text-align:center;color:#7a7a7a;font-weight:600}';
    document.head.appendChild(css);

    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
    function sl(n) { n = Number(n) || 0; if (!n) return '–'; return (Math.round(n * 1000) / 1000).toLocaleString('vi-VN', { maximumFractionDigits: 3 }); }
    function tien(n) { n = Math.round(Number(n) || 0); return n ? n.toLocaleString('vi-VN') : '–'; }
    var THU = ['Chủ nhật', 'Thứ hai', 'Thứ ba', 'Thứ tư', 'Thứ năm', 'Thứ sáu', 'Thứ bảy'];

    // Gộp các dòng cùng nguyên liệu (kể cả khi bị tách theo món) thành 1 dòng cho từng chợ.
    function gop(dsCoSo, layDong) {
        var map = {}, thuTu = [];
        dsCoSo.forEach(function (cs, ci) {
            layDong(cs).forEach(function (r) {
                // Dữ liệu từ file Excel có STT: mỗi dòng của file là 1 dòng hiển thị (không gộp theo tên, vì file có thể có 2 dòng cùng nguyên liệu).
                var k = r.stt > 0 ? 'f' + r.stt : (r.id > 0 ? 'h' + r.id : 'n' + String(r.ten || '').trim().toLowerCase());
                var o = map[k];
                if (!o) { o = map[k] = { stt: r.stt > 0 ? r.stt : 0, ten: r.ten, dvt: r.dvt, gia: 0, giaKhac: false, sl: dsCoSo.map(function () { return 0; }), tt: dsCoSo.map(function () { return 0; }) }; thuTu.push(k); }
                o.sl[ci] += Number(r.sl) || 0;
                o.tt[ci] += Number(r.tt) || 0;
                var g = Number(r.gia) || 0;
                if (g > 0) { if (o.gia > 0 && Math.abs(o.gia - g) > 0.5) o.giaKhac = true; if (!(o.gia > 0)) o.gia = g; }
            });
        });
        return thuTu.map(function (k) { return map[k]; }).filter(function (o) {
            return o.stt > 0 || o.sl.some(function (v) { return v > 0; }) || o.tt.some(function (v) { return v > 0; });
        });
    }

    function tieuDeCot(ds, nhan) {   // nhan: 'Nhà trẻ' | 'Mẫu giáo' | 'Sáng'
        var h = '<thead><tr><th rowspan="2">STT</th><th rowspan="2">Tên thực phẩm</th><th rowspan="2">ĐVT</th>' +
            '<th colspan="' + (ds.length + 1) + '">Số lượng ' + esc(nhan) + '</th><th rowspan="2">Đơn giá<br>(đồng)</th>' +
            '<th colspan="' + (ds.length + 1) + '">Thành tiền</th></tr><tr>';
        h += '<th>Tổng</th>' + ds.map(function (c) { return '<th>' + esc(c.ma || c.ten) + '</th>'; }).join('');
        h += '<th>Tổng cộng</th>' + ds.map(function (c) { return '<th>' + esc(c.ma || c.ten) + '</th>'; }).join('');
        return h + '</tr></thead>';
    }

    function dongSuat(ds, nhan, lay) {
        var tong = 0, o = '<tr class="th-suat"><td colspan="3" style="text-align:left">Số suất ăn ' + esc(nhan) + '</td><td>';
        var cells = ds.map(function (c) { var v = Number(lay(c)) || 0; tong += v; return '<td>' + (v || '–') + '</td>'; }).join('');
        return o + (tong || '–') + '</td>' + cells + '<td></td><td></td>' + ds.map(function () { return '<td></td>'; }).join('') + '</tr>';
    }

    function thanBang(ds, rows, tenTong) {
        var h = '', tongTc = ds.map(function () { return 0; });
        rows.forEach(function (r, i) {
            var tongSl = r.sl.reduce(function (a, b) { return a + b; }, 0), tongTt = r.tt.reduce(function (a, b) { return a + b; }, 0);
            r.tt.forEach(function (v, k) { tongTc[k] += v; });
            h += '<tr><td class="c">' + (r.stt || i + 1) + '</td><td class="t">' + esc(r.ten) + '</td><td class="c">' + esc(r.dvt || '') + '</td><td><b>' + sl(tongSl) + '</b></td>' +
                r.sl.map(function (v) { return '<td class="' + (v ? '' : 'zero') + '">' + sl(v) + '</td>'; }).join('') +
                '<td class="' + (r.giaKhac ? 'khacgia' : '') + '"' + (r.giaKhac ? ' title="Đơn giá khác nhau giữa các chợ — thành tiền tính theo giá từng chợ"' : '') + '>' + tien(r.gia) + '</td>' +
                '<td><b>' + tien(tongTt) + '</b></td>' +
                r.tt.map(function (v) { return '<td class="' + (v ? '' : 'zero') + '">' + tien(v) + '</td>'; }).join('') + '</tr>';
        });
        var tong = tongTc.reduce(function (a, b) { return a + b; }, 0);
        h += '<tr class="th-tong"><td colspan="3" style="text-align:left">' + esc(tenTong) + '</td><td></td>' + ds.map(function () { return '<td></td>'; }).join('') +
            '<td></td><td>' + tien(tong) + '</td>' + tongTc.map(function (v) { return '<td>' + tien(v) + '</td>'; }).join('') + '</tr>';
        return { html: h, tong: tong, tongTc: tongTc };
    }

    function bangSang(ds, trong) {
        var rows = gop(ds, function (c) {
            var a = (c.sang || []).map(function (r) { return { stt: r.stt, id: r.hangHoaId, ten: r.tenHang, dvt: r.dvt, sl: r.soLuong, gia: r.donGia, tt: r.thanhTien }; });
            var b = (c.giaVi || []).filter(function (r) { return r.slAnSang > 0 || r.ttAnSang > 0; })
                .map(function (r) { return { id: r.hangHoaId, ten: r.tenHang, dvt: r.dvt, sl: r.slAnSang, gia: r.donGia, tt: r.ttAnSang }; });
            return a.concat(b);
        });
        if (!rows.length) return trong ? '' : '<div class="th-trong">Chưa có nguyên liệu bữa sáng cho ngày này.</div>';
        var t = thanBang(ds, rows, 'TỔNG CỘNG ĂN SÁNG');
        return '<div class="th-cuon"><table class="th-bang">' + tieuDeCot(ds, 'ăn sáng') + '<tbody>' +
            dongSuat(ds, 'sáng', function (c) { return c.soSuatAnSang; }) + t.html + '</tbody></table></div>';
    }

    function bangTrua(ds, trong) {
        function khoi(nhan, kSl, kTt, kGv, kGvTt, cls, tenTong) {
            var maKhoi = kSl === 'slNhaTre' ? 'nt' : 'mg';
            var rows = gop(ds, function (c) {
                var a = (c.chinh || []).filter(function (r) { return !r.khoi || r.khoi === maKhoi; })
                    .map(function (r) { return { stt: r.stt, id: r.hangHoaId, ten: r.tenHang, dvt: r.dvt, sl: r[kSl], gia: r.donGia, tt: r[kTt] }; });
                var b = (c.giaVi || []).filter(function (r) { return r[kGv] > 0 || r[kGvTt] > 0; })
                    .map(function (r) { return { id: r.hangHoaId, ten: r.tenHang, dvt: r.dvt, sl: r[kGv], gia: r.donGia, tt: r[kGvTt] }; });
                return a.concat(b);
            });
            var soSuat = kSl === 'slNhaTre' ? function (c) { return c.soSuatNhaTre; } : function (c) { return c.soSuatMauGiao; };
            if (!rows.length && trong) return { html: '', tong: 0, tongTc: ds.map(function () { return 0; }) };
            if (!rows.length) return { html: '<tr class="th-khoi ' + cls + '"><td colspan="' + (2 * ds.length + 6) + '">' + esc(nhan.toUpperCase()) + '</td></tr><tr><td colspan="' + (2 * ds.length + 6) + '" class="th-trong">Chưa có nguyên liệu.</td></tr>', tong: 0, tongTc: ds.map(function () { return 0; }) };
            var t = thanBang(ds, rows, tenTong);
            return { html: '<tr class="th-khoi ' + cls + '"><td colspan="' + (2 * ds.length + 6) + '">' + esc(nhan.toUpperCase()) + '</td></tr>' + dongSuat(ds, 'trưa ' + nhan, soSuat) + t.html, tong: t.tong, tongTc: t.tongTc };
        }
        var nt = khoi('Nhà trẻ', 'slNhaTre', 'ttNhaTre', 'slNhaTre', 'ttNhaTre', 'nt', 'TỔNG CỘNG NHÀ TRẺ');
        var mg = khoi('Mẫu giáo', 'slMauGiao', 'ttMauGiao', 'slMauGiao', 'ttMauGiao', 'mg', 'TỔNG CỘNG MẪU GIÁO');
        var tongDong = '<tr class="th-tong-lon"><td colspan="3" style="text-align:left">TỔNG CỘNG NHÀ TRẺ + MẪU GIÁO</td><td></td>' + ds.map(function () { return '<td></td>'; }).join('') +
            '<td></td><td>' + tien(nt.tong + mg.tong) + '</td>' + ds.map(function (c, k) { return '<td>' + tien(nt.tongTc[k] + mg.tongTc[k]) + '</td>'; }).join('') + '</tr>';
        if (trong && !nt.html && !mg.html) return '';
        var tabKhoi = '<div class="th-tabs-khoi"><div class="th-tabs">' +
            [['nt', 'Nhà trẻ'], ['mg', 'Mẫu giáo'], ['ca', 'Cả hai']].map(function (k) {
                return '<button type="button" data-th-khoi="' + k[0] + '" class="' + (KHOI === k[0] ? 'chon' : '') + '">' + k[1] + '</button>';
            }).join('') + '</div></div>';
        var nhan = KHOI === 'nt' ? 'ăn trưa Nhà trẻ' : KHOI === 'mg' ? 'ăn trưa Mẫu giáo' : 'trưa (chính + xế)';
        var thanh = KHOI === 'nt' ? nt.html : KHOI === 'mg' ? mg.html : nt.html + mg.html + tongDong;
        if (KHOI !== 'ca' && !thanh) thanh = '<tr><td colspan="' + (2 * ds.length + 6) + '" class="th-trong">Chưa có nguyên liệu.</td></tr>';
        return tabKhoi + '<div class="th-cuon"><table class="th-bang">' + tieuDeCot(ds, nhan) + '<tbody>' + thanh + '</tbody></table></div>';
    }

    function veNoiDung(box, kq, opts) {
        var ds = kq.coSo || [];
        var body = box.querySelector('.th-than');
        if (!ds.length) { body.innerHTML = (opts && opts.trong) ? '' : '<div class="th-trong">Chưa có cơ sở nào.</div>'; return; }
        var coDon = ds.some(function (c) { return c.coThucDon; });
        var ghi = (opts && opts.ghiChu != null)
            ? '<div class="th-ghichu">' + opts.ghiChu + '</div>'
            : '<div class="th-ghichu">Số liệu theo thực đơn và sĩ số <b>đã lưu</b> của từng chợ (' + ds.map(function (c) { return esc(c.ma || c.ten); }).join(', ') +
            '). Món vừa sửa mà chưa lưu sẽ chưa tính vào bảng này.' + (coDon ? '' : ' <b>Ngày này chưa lưu thực đơn nên chưa có nguyên liệu.</b>') + '</div>';
        var trong = !!(opts && opts.trong);
        body.innerHTML = ghi + (TAB === 'sang' ? bangSang(ds, trong) : bangTrua(ds, trong));
    }

    // opts (tuỳ chọn): { data: kết quả đã có sẵn (trang phụ huynh — không gọi API nội bộ), ghiChu: HTML ghi chú thay cho ghi chú mặc định }
    function render(box, iso, opts) {
        if (!box) return;
        opts = opts || {};
        var d = new Date(iso + 'T00:00:00');
        var tieuDe = 'Chi tiết nguyên liệu &amp; giá — ' + THU[d.getDay()] + ', ngày ' + d.getDate() + ' tháng ' + (d.getMonth() + 1) + ' năm ' + d.getFullYear();
        box.innerHTML = '<div class="th-dau"><h3>' + tieuDe + '</h3><div class="th-tabs">' +
            '<button type="button" data-th-tab="sang" class="' + (TAB === 'sang' ? 'chon' : '') + '">Ăn sáng</button>' +
            '<button type="button" data-th-tab="trua" class="' + (TAB === 'trua' ? 'chon' : '') + '">Ăn trưa</button></div></div>' +
            '<div class="th-than"><div class="th-trong">Đang tải…</div></div>';
        function ve(kq) {
            if (!kq || kq.ok === false) { box.querySelector('.th-than').innerHTML = '<div class="th-trong">' + esc((kq && kq.loi) || 'Không tải được dữ liệu.') + '</div>'; return; }
            veNoiDung(box, kq, opts);
        }
        box.onclick = function (e) {
            var kb = e.target.closest('[data-th-khoi]');
            if (kb) {
                KHOI = kb.getAttribute('data-th-khoi');
                try { localStorage.setItem('th-khoi-trua', KHOI); } catch (x) { }
                var cs = opts.data || CACHE[iso];
                if (cs) ve(cs);
                return;
            }
            var b = e.target.closest('[data-th-tab]'); if (!b) return;
            TAB = b.getAttribute('data-th-tab');
            try { localStorage.setItem('th-tab', TAB); } catch (x) { }
            box.querySelectorAll('[data-th-tab]').forEach(function (x) { x.classList.toggle('chon', x === b); });
            var coSan = opts.data || CACHE[iso];
            if (coSan) ve(coSan);
        };
        if (opts.data) { ve(opts.data); return; }
        if (CACHE[iso]) { ve(CACHE[iso]); return; }
        fetch('/chi-tiet-ngay/tong-hop?ngay=' + encodeURIComponent(iso), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (kq) { CACHE[iso] = kq; if (box.isConnected) ve(kq); })
            .catch(function () { if (box.isConnected) ve({ ok: false, loi: 'Không tải được chi tiết nguyên liệu (kiểm tra đăng nhập/quyền hoặc tải lại trang).' }); });
    }

    window.TongHopNgay = { render: render, xoaCache: function () { CACHE = {}; } };
})();
