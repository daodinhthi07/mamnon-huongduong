/*
 * Ô chọn món gõ tìm kiếm được — dùng chung cho mọi trang.
 *
 * Cách dùng: thêm thuộc tính  data-tim-mon  vào thẻ <select> (không phải sửa gì khác):
 *     <select class="form-control" name="..." data-tim-mon> ... </select>
 *
 * - <select> gốc được ẩn đi nhưng VẪN nằm trong form/DOM, nên mọi chỗ đọc select.value, gửi form, bắt sự kiện 'change' cũ
 *   đều chạy như trước. Chọn 1 món trong danh sách lọc = set select.value + bắn 'change'.
 * - Tìm KHÔNG phân biệt dấu và khớp từng từ bất kể thứ tự: gõ "pho ga", "Phở gà", "ga pho" đều ra "Phở gà".
 * - Phím: ↑ ↓ chọn dòng, Enter chọn, Esc đóng. Danh sách hiện bằng position:fixed nên không bị cắt trong bảng cuộn.
 * - Tự theo dõi: select.disabled (bật/tắt từ ngoài), đổi giá trị bằng code (bắn sự kiện 'mon-dong-bo' hoặc 'change'),
 *   và danh sách option bị thay đổi.
 * API: window.MamNonSearch.enhance(select) / enhanceAll(selector) / autoEnhance(selector)
 */
(function () {
    'use strict';

    function chuanHoa(s) {
        // NFD rồi bỏ dấu: dùng được cho cả chữ dạng dựng sẵn (NFC) lẫn dạng tổ hợp (Unikey/Mac).
        return (s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
            .replace(/đ/g, 'd').replace(/Đ/g, 'd').toLowerCase();
    }

    function enhance(select) {
        if (!select || select.dataset.timMonDone === '1') return;
        select.dataset.timMonDone = '1';

        var wrap = document.createElement('div');
        wrap.className = 'mon-search-wrap tim-mon-wrap';
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('mon-search-native');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'mon-search-input';
        input.setAttribute('autocomplete', 'off');
        input.setAttribute('spellcheck', 'false');
        wrap.appendChild(input);

        var list = document.createElement('div');
        list.className = 'mon-search-list mon-search-fixed';
        wrap.appendChild(list);

        var dangMo = false;
        var chiSo = -1;       // dòng đang tô sáng bằng bàn phím
        var dongHienTai = []; // các option đang hiện trong danh sách

        function nhanRong() {
            var op0 = select.options[0];
            return (op0 && !op0.value) ? op0.textContent : '-- chưa chọn --';
        }

        function capNhatInput() {
            var op = select.options[select.selectedIndex];
            input.value = (op && op.value) ? op.textContent : '';
            input.placeholder = nhanRong();
            input.title = input.value;
        }

        function dongBoTrangThai() {
            input.disabled = select.disabled;
            wrap.classList.toggle('tim-mon-tat', select.disabled);
            if (select.disabled) dongDanhSach();
        }

        function datViTri() {
            var r = input.getBoundingClientRect();
            var duoi = window.innerHeight - r.bottom - 8;
            var tren = r.top - 8;
            var cao = 260;
            list.style.left = r.left + 'px';
            list.style.width = Math.max(r.width, 260) + 'px';
            if (duoi < 170 && tren > duoi) {          // ít chỗ bên dưới -> mở lên trên
                list.style.top = 'auto';                  // phải là 'auto' (không phải ''), nếu không CSS top:calc(100% + 4px) kéo danh sách ra ngoài màn hình => trống
                list.style.bottom = (window.innerHeight - r.top + 2) + 'px';
                list.style.maxHeight = Math.min(cao, tren) + 'px';
            } else {
                list.style.bottom = 'auto';
                list.style.top = (r.bottom + 2) + 'px';
                list.style.maxHeight = Math.min(cao, duoi) + 'px';
            }
        }

        function khiCuon(e) {
            if (e && e.target === list) return;       // cuộn chính danh sách thì đừng đóng
            datViTri();
        }

        function dongDanhSach() {
            if (!dangMo) return;
            dangMo = false;
            list.style.display = 'none';
            list.innerHTML = '';
            window.removeEventListener('scroll', khiCuon, true);
            window.removeEventListener('resize', datViTri);
        }

        function chon(op) {
            select.value = op.value;
            capNhatInput();
            dongDanhSach();
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        function toSang(i) {
            var items = list.querySelectorAll('.mon-search-item');
            if (!items.length) return;
            chiSo = Math.max(0, Math.min(items.length - 1, i));
            Array.prototype.forEach.call(items, function (it, k) { it.classList.toggle('kb', k === chiSo); });
            items[chiSo].scrollIntoView({ block: 'nearest' });
        }

        function moDanhSach(tuKhoaGoc) {
            var tuKhoa = chuanHoa(tuKhoaGoc).trim();
            var cacTu = tuKhoa ? tuKhoa.split(/\s+/) : [];
            dongHienTai = Array.prototype.slice.call(select.options).filter(function (op) {
                if (!op.value) return !tuKhoa;        // gõ tìm thì ẩn dòng "-- chưa chọn --"
                var ten = chuanHoa(op.textContent);
                return cacTu.every(function (t) { return ten.indexOf(t) !== -1; });
            });

            list.innerHTML = '';
            if (dongHienTai.length === 0) {
                var trong = document.createElement('div');
                trong.className = 'mon-search-empty';
                trong.textContent = 'Không có món phù hợp';
                list.appendChild(trong);
            } else {
                dongHienTai.forEach(function (op) {
                    var dong = document.createElement('div');
                    dong.className = 'mon-search-item' + (op.value === select.value ? ' active' : '');
                    dong.textContent = op.textContent;
                    dong.addEventListener('mousedown', function (e) {
                        e.preventDefault();           // giữ focus ở ô nhập để blur không chạy trước khi bắt được click
                        chon(op);
                    });
                    list.appendChild(dong);
                });
            }
            list.style.display = 'block';
            if (!dangMo) {
                dangMo = true;
                window.addEventListener('scroll', khiCuon, true);
                window.addEventListener('resize', datViTri);
            }
            datViTri();
            // dòng đầu tiên (khi đang gõ) hoặc dòng đang chọn (khi mới mở) được tô sáng
            var act = list.querySelector('.mon-search-item.active');
            if (tuKhoa) { toSang(0); }
            else if (act) { act.scrollIntoView({ block: 'nearest' }); chiSo = -1; }
            else { chiSo = -1; }
        }

        input.addEventListener('focus', function () {
            if (input.disabled) return;
            moDanhSach('');
            input.select();
        });
        input.addEventListener('click', function () {
            if (!dangMo && !input.disabled) moDanhSach('');
        });
        input.addEventListener('input', function () { moDanhSach(input.value); });
        input.addEventListener('blur', function () {
            setTimeout(function () {                  // trễ 1 nhịp để 'mousedown' kịp chạy trước khi đóng
                dongDanhSach();
                capNhatInput();                       // gõ dở không chọn thì quay lại đúng món đang chọn
            }, 150);
        });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (!dangMo) moDanhSach(input.value === (select.options[select.selectedIndex] || {}).textContent ? '' : input.value);
                toSang(chiSo + 1);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (dangMo) toSang(chiSo - 1);
            } else if (e.key === 'Enter') {
                if (dangMo) {
                    e.preventDefault();               // không submit form
                    var i = chiSo >= 0 ? chiSo : (input.value.trim() ? 0 : -1);
                    if (i >= 0 && dongHienTai[i]) chon(dongHienTai[i]);
                    else dongDanhSach();
                }
            } else if (e.key === 'Escape') {
                dongDanhSach(); capNhatInput(); input.blur();
            }
        });

        // Đồng bộ khi nơi khác đổi giá trị / trạng thái của <select> gốc.
        select.addEventListener('change', capNhatInput);
        select.addEventListener('mon-dong-bo', capNhatInput);
        if (window.MutationObserver) {
            new MutationObserver(dongBoTrangThai).observe(select, { attributes: true, attributeFilter: ['disabled'] });
            new MutationObserver(capNhatInput).observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['selected'] });
        }

        capNhatInput();
        dongBoTrangThai();
    }

    function enhanceAll(selector, root) {
        Array.prototype.forEach.call((root || document).querySelectorAll(selector || 'select[data-tim-mon]'), enhance);
    }

    // Tự bọc cả những <select> được thêm vào trang sau này (tạo bằng JS).
    function autoEnhance(selector) {
        selector = selector || 'select[data-tim-mon]';
        enhanceAll(selector);
        if (!window.MutationObserver) return;
        new MutationObserver(function () { enhanceAll(selector); })
            .observe(document.body, { childList: true, subtree: true });
    }

    window.MamNonSearch = { enhance: enhance, enhanceAll: enhanceAll, autoEnhance: autoEnhance };

    function khoiTao() { autoEnhance('select[data-tim-mon]'); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', khoiTao);
    else khoiTao();
})();
