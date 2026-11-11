(function () {
    'use strict';

    function formatTien(so) {
        return Math.round(so).toLocaleString('vi-VN') + 'đ';
    }

    function layMonDaChon() {
        var ketQua = {};
        document.querySelectorAll('.chon-mon').forEach(function (sel) {
            var buaId = sel.getAttribute('data-bua-id');
            ketQua[buaId] = sel.value ? parseInt(sel.value, 10) : null;
        });
        return ketQua;
    }

    function layTrangThaiHienTai() {
        return {
            khoiId: document.getElementById('khoiId').value ? parseInt(document.getElementById('khoiId').value, 10) : null,
            nhomChiPhiId: document.getElementById('nhomChiPhiId').value ? parseInt(document.getElementById('nhomChiPhiId').value, 10) : null,
            ngay: document.getElementById('ngay').value,
            soSuatAn: parseInt(document.getElementById('soSuatAn').value || '0', 10),
            tienAn1Chau: parseFloat(document.getElementById('tienAn1Chau').value || '0'),
            monDaChonTheoBua: layMonDaChon()
        };
    }

    function veBangNguyenLieu(ds) {
        var tbody = document.getElementById('bang-nguyen-lieu-body');
        tbody.innerHTML = '';
        var tong = 0;
        ds.forEach(function (kq) {
            tong += kq.thanhTien;
            var tr = document.createElement('tr');
            tr.innerHTML = '<td>' + kq.tenHang + '</td><td>' + (kq.dvt || '') + '</td>' +
                '<td>' + kq.soLuong.toFixed(3).replace(/\.?0+$/, '') + '</td><td>' + formatTien(kq.thanhTien) + '</td>';
            tbody.appendChild(tr);
        });
        document.getElementById('tong-tien-nguyen-lieu').innerHTML = '<strong>' + formatTien(tong) + '</strong>';
    }

    function veBangHangMuc(ds) {
        var tbody = document.getElementById('bang-hang-muc-body');
        tbody.innerHTML = '';
        ds.forEach(function (kq) {
            var tr = document.createElement('tr');
            if (kq.vuotDinhMuc === true) tr.className = 'row-vuot-dinh-muc';
            var dinhMucText = kq.dinhMuc === null ? 'Chưa cấu hình' : formatTien(kq.dinhMuc);
            var chenhLechHtml = '—';
            if (kq.chenhLech !== null) {
                var cls = kq.vuotDinhMuc === true ? 'text-danger' : 'text-success';
                var dau = kq.chenhLech > 0 ? '+' : '';
                chenhLechHtml = '<span class="' + cls + '">' + dau + formatTien(kq.chenhLech) + '</span>';
            }
            tr.innerHTML = '<td>' + kq.tenHangMuc + '</td><td>' + formatTien(kq.chiPhiThucTe) + '</td>' +
                '<td>' + dinhMucText + '</td><td>' + chenhLechHtml + '</td>';
            tbody.appendChild(tr);
        });
    }

    function veTongQuanNganSach(nganSach) {
        var el = document.getElementById('tong-quan-ngan-sach');
        if (!nganSach) {
            el.innerHTML = '<em>Nhập đủ Nhóm chi phí + Tiền ăn 1 cháu để xem đối chiếu.</em>';
            return;
        }
        var cls = nganSach.chenhLech < 0 ? 'text-danger' : 'text-success';
        el.innerHTML =
            '<p>Số tiền được phép chi: <strong>' + formatTien(nganSach.soTienDuocPhepChi) + '</strong></p>' +
            '<p>Tổng tiền nguyên liệu dự kiến: <strong>' + formatTien(nganSach.tongChiThucTe) + '</strong></p>' +
            '<p>Chênh lệch: <span class="' + cls + '">' + formatTien(nganSach.chenhLech) + '</span></p>';
    }

    function capNhatLinkXuatExcel() {
        var coSoId = document.getElementById('coSoId').value;
        var khoiId = document.getElementById('khoiId').value;
        var ngay = document.getElementById('ngay').value;
        document.getElementById('link-xuat-excel').href =
            '/xuat-excel?coSoId=' + coSoId + '&khoiId=' + khoiId + '&ngay=' + ngay;
    }

    function tinhToan() {
        var payload = layTrangThaiHienTai();
        if (!payload.khoiId) {
            veBangNguyenLieu([]);
            veBangHangMuc([]);
            veTongQuanNganSach(null);
            return;
        }

        fetch('/soan-thuc-don/tinh-toan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                veBangNguyenLieu(data.nguyenLieu || []);
                veBangHangMuc(data.hangMuc || []);
                veTongQuanNganSach(data.nganSach || null);
            });

        capNhatLinkXuatExcel();
    }

    // Chuẩn hoá để so sánh: undefined/null/'' đều coi là "chưa gán nhóm" (khớp
    // hành vi null == null của C#/EF cho các bữa/món chưa gán nhóm bữa).
    function chuanHoaNhom(x) {
        return (x === undefined || x === null || x === '') ? null : String(x);
    }

    // Chỉ đưa vào dropdown những món CÙNG NHÓM BỮA (Sáng/Canh/Mặn/Xào/Tráng
    // miệng/Xế/...) với dòng bữa đang soạn — tránh chọn nhầm món trái bữa.
    function xayDungTuyChonMon(select, monDaChon, nhomBuaAnId) {
        select.innerHTML = '<option value="">-- Chưa chọn --</option>';
        var nhomCanLoc = chuanHoaNhom(nhomBuaAnId);
        (window.__monAnList || [])
            .filter(function (mon) { return chuanHoaNhom(mon.nhomBuaAnId) === nhomCanLoc; })
            .forEach(function (mon) {
                var opt = document.createElement('option');
                opt.value = mon.id;
                opt.textContent = mon.tenMon;
                if (monDaChon === mon.id) opt.selected = true;
                select.appendChild(opt);
            });
    }

    function taiBuaTheoKhoi() {
        var khoiId = document.getElementById('khoiId').value;
        var tbody = document.getElementById('bang-bua-body');
        tbody.innerHTML = '';

        if (!khoiId) {
            tinhToan();
            return;
        }

        fetch('/soan-thuc-don/bua-theo-khoi?khoiId=' + khoiId)
            .then(function (res) { return res.json(); })
            .then(function (dsBua) {
                dsBua.forEach(function (bua) {
                    var tr = document.createElement('tr');
                    tr.setAttribute('data-bua-id', bua.id);
                    tr.setAttribute('data-nhom-bua-an-id', bua.nhomBuaAnId == null ? '' : bua.nhomBuaAnId);

                    var tdTen = document.createElement('td');
                    tdTen.textContent = bua.tenBua;

                    var tdChon = document.createElement('td');
                    var select = document.createElement('select');
                    select.className = 'form-control chon-mon';
                    select.setAttribute('data-bua-id', bua.id);
                    select.setAttribute('data-nhom-bua-an-id', bua.nhomBuaAnId == null ? '' : bua.nhomBuaAnId);
                    var monDaChon = (window.__monDaChonBanDau || {})[bua.id] || null;
                    xayDungTuyChonMon(select, monDaChon, bua.nhomBuaAnId);
                    select.addEventListener('change', tinhToan);

                    tdChon.appendChild(select);
                    tr.appendChild(tdTen);
                    tr.appendChild(tdChon);
                    tbody.appendChild(tr);
                });

                tinhToan();
            });
    }

    function submitForm(guiDuyet) {
        document.getElementById('MonDaChonJson').value = JSON.stringify(layMonDaChon());
        document.getElementById('GuiDuyet').value = guiDuyet ? 'true' : 'false';
        document.getElementById('form-soan').submit();
    }
    window.submitForm = submitForm;

    /// Gọi API chọn ngẫu nhiên 1 món/bữa, điền vào các ô chọn rồi tính lại
    /// như bình thường — người dùng vẫn sửa tay từng ô sau đó nếu không ưng.
    function chonMonNgauNhien() {
        var khoiId = document.getElementById('khoiId').value;
        if (!khoiId) {
            alert('Chọn khối trước đã.');
            return;
        }

        fetch('/soan-thuc-don/mon-ngau-nhien?khoiId=' + khoiId)
            .then(function (res) { return res.json(); })
            .then(function (monTheoBua) {
                document.querySelectorAll('.chon-mon').forEach(function (sel) {
                    var buaId = sel.getAttribute('data-bua-id');
                    var monId = monTheoBua[buaId];
                    if (monId) sel.value = monId;
                });
                tinhToan();
            });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.getElementById('khoiId').addEventListener('change', taiBuaTheoKhoi);
        ['coSoId', 'nhomChiPhiId', 'ngay', 'soSuatAn', 'tienAn1Chau'].forEach(function (id) {
            document.getElementById(id).addEventListener('input', tinhToan);
            document.getElementById(id).addEventListener('change', tinhToan);
        });
        document.querySelectorAll('.chon-mon').forEach(function (sel) {
            sel.addEventListener('change', tinhToan);
        });
        document.getElementById('btn-ngau-nhien').addEventListener('click', chonMonNgauNhien);

        capNhatLinkXuatExcel();
        if (document.getElementById('khoiId').value) {
            tinhToan();
        }
    });
})();
