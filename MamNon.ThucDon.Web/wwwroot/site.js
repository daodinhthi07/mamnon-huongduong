// Dùng chung cho MỌI nơi xuất file Excel: hiện vòng quay chờ + báo lỗi cụ thể (VD nguyên liệu thiếu trong Bảng giá) thay vì chuyển sang trang lỗi.
//   - window.xuatExcelAjax(form)      : gửi form (POST/GET, kể cả multipart có file) rồi tải file xuống.
//   - window.xuatExcelUrl(url)        : tải file từ 1 đường dẫn GET.
//   - Gắn thuộc tính HTML (không cần viết JS):
//       <form data-xuat-excel ...>          -> submit = xuất bằng fetch (vòng quay chờ + hộp thông báo lỗi)
//       <a data-xuat-excel-url href="...">  -> bấm = tải file bằng fetch (href vẫn là đường dẫn dự phòng khi tắt JS)
//       <form data-hien-cho="Đang ...">     -> chỉ hiện vòng quay chờ khi gửi form (thao tác dài rồi chuyển trang, VD tải file mẫu lên)
(function () {
    var css = document.createElement('style');
    css.textContent =
        '.xl-overlay{position:fixed;inset:0;background:rgba(255,255,255,.72);z-index:9999;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;font-size:15px;font-weight:600}' +
        '.xl-spin{width:48px;height:48px;border:5px solid #d9d2c3;border-top-color:#8a5a2b;border-radius:50%;animation:xl-quay .8s linear infinite}' +
        '@keyframes xl-quay{to{transform:rotate(360deg)}}' +
        '.xl-modal{background:#fff;border:1px solid #c0392b;border-radius:14px;max-width:640px;width:calc(100% - 32px);max-height:80vh;overflow:auto;padding:18px 20px;box-shadow:0 10px 40px rgba(0,0,0,.2);font-weight:400}' +
        '.xl-modal h3{margin:0 0 10px;color:#c0392b}.xl-modal pre{white-space:pre-wrap;font:inherit;margin:0 0 14px}';
    document.head.appendChild(css);

    function hienCho(msg) {
        var o = document.createElement('div');
        o.className = 'xl-overlay';
        o.setAttribute('data-xl-cho', '1');
        o.innerHTML = '<div class="xl-spin"></div><div></div>';
        o.lastChild.textContent = msg || 'Đang xử lý...';
        document.body.appendChild(o);
        return function () { if (o.parentNode) o.parentNode.removeChild(o); };
    }

    function hienLoi(tieuDe, noiDung, dsDong) {
        var o = document.createElement('div');
        o.className = 'xl-overlay';
        o.setAttribute('role', 'alertdialog'); o.setAttribute('aria-modal', 'true');
        var m = document.createElement('div'); m.className = 'xl-modal';
        var h = document.createElement('h3'); h.textContent = tieuDe;
        m.appendChild(h);
        if (dsDong && dsDong.length) {
            var g = document.createElement('p'); g.style.margin = '0 0 8px';
            g.textContent = 'Các nguyên liệu sau chưa có trong Bảng giá (hoặc chưa có đơn giá), nên chưa xuất được file:';
            var ul = document.createElement('ul'); ul.style.cssText = 'margin:0 0 12px;padding-left:20px;line-height:1.55';
            dsDong.forEach(function (d) { var li = document.createElement('li'); li.textContent = d; ul.appendChild(li); });
            var c = document.createElement('p'); c.style.margin = '0 0 14px';
            c.textContent = 'Vui lòng vào Bảng giá để thêm / cập nhật giá cho các mặt hàng trên, rồi xuất lại.';
            m.appendChild(g); m.appendChild(ul); m.appendChild(c);
        } else {
            var p = document.createElement('pre'); p.textContent = noiDung; m.appendChild(p);
        }
        var a = document.createElement('a'); a.className = 'btn btn-primary'; a.href = '/bang-gia'; a.target = '_blank'; a.textContent = 'Mở Bảng giá';
        var b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-secondary'; b.style.marginLeft = '8px'; b.textContent = 'Đóng';
        function dong() { if (o.parentNode) o.parentNode.removeChild(o); document.removeEventListener('keydown', phim); }
        function phim(e) { if (e.key === 'Escape') dong(); }
        b.onclick = dong; document.addEventListener('keydown', phim);
        m.appendChild(a); m.appendChild(b); o.appendChild(m);
        document.body.appendChild(o); b.focus();
    }

    // Chạy 1 lần xuất: goiFetch() trả về Promise<Response>. Thành công -> tải file; lỗi -> hộp thông báo cụ thể.
    async function chayXuat(goiFetch, tenFileMacDinh) {
        var tat = hienCho('Đang xuất Excel, vui lòng chờ...');
        try {
            var res = await goiFetch();
            var loai = res.headers.get('Content-Type') || '';
            if (res.ok && loai.indexOf('json') < 0 && loai.indexOf('text/html') < 0) {
                var ten = tenFileMacDinh || 'bang-ke-cho.xlsx';
                var cd = res.headers.get('Content-Disposition') || '';
                var m = /filename\*=UTF-8''([^;]+)/i.exec(cd) || /filename="?([^";]+)"?/i.exec(cd);
                if (m) { try { ten = decodeURIComponent(m[1]); } catch (e) { ten = m[1]; } }
                var blob = await res.blob();
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a'); a.href = url; a.download = ten;
                document.body.appendChild(a); a.click(); document.body.removeChild(a);
                setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
                return true;
            }
            if (loai.indexOf('text/html') >= 0) {
                // Server chuyển hướng về trang kèm thông báo (TempData / ModelState) -> lấy nội dung thông báo từ trang đó để hiện trong hộp thoại.
                var html = await res.text();
                var doc = new DOMParser().parseFromString(html, 'text/html');
                // Lấy ô thông báo đầu tiên CÓ NỘI DUNG (khung cảnh báo rỗng của trang không tính).
                var thongBao = '';
                doc.querySelectorAll('.ls-loi, .alert-danger, .validation-summary-errors, .alert-thanh-cong').forEach(function (n) {
                    if (!thongBao && n.textContent.trim()) thongBao = n.textContent.trim();
                });
                hienLoi('Không xuất được Excel', thongBao || 'Có lỗi khi xuất Excel — vui lòng thử lại.');
                return false;
            }
            var j = null;
            try { j = await res.json(); } catch (e) { }
            hienLoi('Không xuất được Excel', (j && j.message) ? j.message : ('Lỗi ' + res.status + ' — vui lòng thử lại hoặc báo quản trị.'), j && j.items);
        } catch (e) {
            hienLoi('Không xuất được Excel', 'Mất kết nối tới máy chủ — vui lòng thử lại.');
        } finally { tat(); }
        return false;
    }

    // Gửi form bằng fetch: form có file (multipart) -> FormData; còn lại -> x-www-form-urlencoded.
    window.xuatExcelAjax = function (form, tenFileMacDinh) {
        return chayXuat(function () {
            var method = (form.getAttribute('method') || 'POST').toUpperCase();
            var action = form.getAttribute('action') || window.location.href;
            var opt = { method: method, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } };
            var data = new FormData(form);
            if (method === 'GET') {
                action += (action.indexOf('?') < 0 ? '?' : '&') + new URLSearchParams(data).toString();
            } else if ((form.getAttribute('enctype') || '').toLowerCase().indexOf('multipart') >= 0) {
                opt.body = data;   // để trình duyệt tự đặt Content-Type + boundary
            } else {
                opt.headers['Content-Type'] = 'application/x-www-form-urlencoded';
                opt.body = new URLSearchParams(data);
            }
            return fetch(action, opt);
        }, tenFileMacDinh);
    };

    // Tải file từ 1 đường dẫn GET.
    window.xuatExcelUrl = function (url, tenFileMacDinh) {
        return chayXuat(function () {
            return fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        }, tenFileMacDinh);
    };

    // Gắn bằng thuộc tính HTML (ủy quyền sự kiện nên cũng chạy với phần tử tạo động).
    document.addEventListener('click', function (e) {
        var el = e.target.closest ? e.target.closest('[data-xuat-excel-url]') : null;
        if (!el || e.defaultPrevented) return;
        e.preventDefault();
        window.xuatExcelUrl(el.getAttribute('data-xuat-excel-url') || el.getAttribute('href'), el.getAttribute('data-ten-file'));
    });
    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (!f || !f.matches || e.defaultPrevented) return;   // đã bị chặn (VD confirm() bấm Hủy) thì không làm gì
        if (f.matches('form[data-xuat-excel]')) { e.preventDefault(); window.xuatExcelAjax(f); }
        else if (f.matches('form[data-hien-cho]')) { hienCho(f.getAttribute('data-hien-cho') || 'Đang xử lý...'); }
    });
    // Quay lại bằng nút Back của trình duyệt (bfcache) thì gỡ vòng quay còn sót.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('.xl-overlay[data-xl-cho]').forEach(function (o) { if (o.parentNode) o.parentNode.removeChild(o); });
    });
})();
