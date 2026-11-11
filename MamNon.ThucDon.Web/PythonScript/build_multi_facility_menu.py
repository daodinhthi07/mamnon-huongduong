#!/usr/bin/env python3
"""
build_multi_facility_menu.py  (v4 — thêm số cháu tùy chỉnh theo từng ngày)
====================================================================
Từ 1 file đã điền thực đơn + số cháu cho TC, tự động:
  1. Cập nhật ngày tháng cho cả 6 khối ngày trong Chợ TC (chỉ cần cho
     biết ngày Thứ 2 đầu tuần, không cần tự gõ tay từng ngày).
  2. Nhân bản Chợ TC thành Chợ PH ok / Chợ ĐT ok theo đúng số cháu mới —
     CÓ THỂ khác nhau từng ngày trong tuần (mới thêm ở bản v4), vì học
     sinh đi học không đều nhau các ngày trong tuần.
  3. Chia tỷ lệ số lượng thực phẩm mục 3-4 theo số cháu (tô vàng, vẫn
     cần bếp rà lại) — nhưng ép TỔNG CỘNG mỗi ngày = đúng ngân sách được
     cấp (Chênh lệch cuối ngày = 0), tránh số liệu "ảo" gây hiểu lầm.
  4. Tự dò đúng vị trí "Tổng cộng" của từng khối ngày bằng cách QUÉT
     text (không dùng số dòng cố định) — vì số lượng nguyên liệu mỗi
     ngày KHÁC NHAU nên vị trí các dòng không cố định cách nhau 90 dòng.
  5. Tự dò và bọc IFERROR cho mọi lỗi #DIV/0! phát sinh.
  6. Đóng băng công thức liên kết workbook ngoài (Bảng giá cũ) để không
     mất dữ liệu khi lưu.

CÁCH DÙNG — số cháu GIỐNG NHAU cả tuần (như bản cũ, vẫn dùng được)
-------------------------------------------------------------------
python3 build_multi_facility_menu.py <file_vao.xlsx> <file_ra.xlsx> \
    --start-date 2026-09-14 \
    --ph-counts <an_sang> <nha_tre> <mau_giao> \
    --dt-counts <an_sang> <nha_tre> <mau_giao>

CÁCH DÙNG — số cháu KHÁC NHAU từng ngày (MỚI, --ph-counts vẫn phải điền
làm giá trị mặc định phòng khi thiếu ngày, nhưng --ph-counts-per-day sẽ
được ưu tiên dùng nếu có)
-------------------------------------------------------------------
python3 build_multi_facility_menu.py <file_vao.xlsx> <file_ra.xlsx> \
    --start-date 2026-09-14 \
    --ph-counts 220 70 290 --dt-counts 60 0 67 \
    --ph-counts-per-day '[[220,70,290],[220,70,290],[200,65,280],[210,68,285],[215,70,288],[150,50,200]]' \
    --dt-counts-per-day '[[60,0,67],[60,0,67],[55,0,60],[58,0,63],[60,0,65],[40,0,45]]'

--ph-counts-per-day / --dt-counts-per-day : chuỗi JSON, ĐÚNG 6 bộ 3 số
    [ăn_sáng, nhà_trẻ, mẫu_giáo] theo thứ tự Thứ 2 → Thứ 7. Bỏ qua tham
    số này nếu số cháu không đổi trong tuần (dùng --ph-counts/--dt-counts
    như cũ là đủ).
--start-date : ngày Thứ 2 đầu tuần (định dạng YYYY-MM-DD). Script tự tính
               5 ngày còn lại (Thứ 2 → Thứ 7) và tự ghi đúng tên Thứ.
               BỎ QUA cờ này nếu không muốn script đụng vào ngày tháng.

QUY TRÌNH HÀNG TUẦN (chỉ cần làm tay 1 việc)
-----------------------------------------------
1. Mở file tuần trước, giữ nguyên "Chợ TC" (KHÔNG xoá Chợ PH ok/Chợ ĐT ok
   cũ — script tự xoá và tạo lại).
2. Sửa thực đơn TC ở sheet "Thực đơn tuần ok" (dòng 1-40) + sửa số cháu
   thật ở "Chợ TC" (H7/I7/J7 mỗi khối ngày, tương ứng dòng
   7,97,187,277,367,457) — đây là 2 việc BẮT BUỘC làm tay vì phụ thuộc
   thực tế bếp/điểm danh, không tự động hoá được.
3. Chạy script này — mọi thứ còn lại (ngày tháng, Chợ PH ok/ĐT ok, Chênh
   lệch=0, kiểm lỗi công thức) tự động hoàn tất.

GIẢ ĐỊNH (chỉ đúng khi khung file KHÔNG đổi qua các tuần)
-----------------------------------------------------------
- Sheet "Chợ TC": có đúng 6 khối ngày, dòng "Xuất ăn trong ngày" (số cháu)
  luôn ở dòng 7, 97, 187, 277, 367, 457 — CÁCH NHAU ĐÚNG 90 DÒNG (đây là
  điểm neo duy nhất còn cố định; mọi vị trí khác trong mỗi khối được
  DÒ ĐỘNG bằng text, không giả định số dòng cố định).
- Mỗi khối ngày có đúng cấu trúc: '1. Thực đơn :' → '2. Theo dõi xuất
  kho gia vị' → '3. Chi tiền thực phẩm sáng' → '4. Chi tiền thực phẩm
  ăn chính + xế' → 'Nhà bếp' (kết thúc khối).
- Ô ngày tháng dạng chữ ("Thứ hai, ngày 07 tháng 9 năm 2026") nằm ở cột G,
  dòng đầu mỗi khối (headcount_row - 4).
Nếu khung file thay đổi cấu trúc, cần chỉnh lại DAY_HEADCOUNT_ROWS.

LỊCH SỬ SỬA ĐỔI
----------------
v4: + Số cháu tùy chỉnh theo từng ngày (--ph/dt-counts-per-day).
    + Sửa lỗi: thiếu `sys.stdout.reconfigure(encoding='utf-8')` khiến
      script có thể crash với UnicodeEncodeError khi in tiếng Việt trên
      Windows (bản PythonScript/ trước đó bị lệch với bản gốc).
    + Sửa lỗi: force_balance_totals/scale_highlight_and_balance từng có
      thể ghi công thức hỏng dạng "=IFERROR(H101-HNone,0)" nếu không dò
      được dòng "Tổng cộng" mục gia vị (giavi_total_row = None) — giờ
      bỏ qua an toàn + in cảnh báo thay vì ghi công thức sai.
"""

import sys
sys.stdout.reconfigure(encoding='utf-8')

import argparse
import datetime
import json
import re
import subprocess
from pathlib import Path

from openpyxl import load_workbook
from openpyxl.comments import Comment
from openpyxl.styles import PatternFill
from openpyxl.worksheet.copier import WorksheetCopy

# ==== Hằng số phụ thuộc khung file ====
CHO_TC_SHEET = "Chợ TC"
DAY_HEADCOUNT_ROWS = [7, 97, 187, 277, 367, 457]
DATE_COL = 7  # cột G

YELLOW = PatternFill(start_color="FFFF00", end_color="FFFF00", fill_type="solid")
ORANGE_FILL = PatternFill(start_color="FFC000", end_color="FFC000", fill_type="solid")
NOTE_TEXT = (
    "Ghi chú: số lượng thực phẩm mục 3-4 là ƯỚC TÍNH chia tỷ lệ theo số cháu (so với TC). "
    "Tổng tiền mỗi ngày đã được ép khớp đúng ngân sách được cấp (Chênh lệch = 0). Bếp/kế "
    "toán vẫn cần rà lại và sửa tay từng dòng cho khớp thực tế mua trước khi dùng để chi chợ."
)

THU_NAMES = ["Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy"]


# ------------------------------------------------------------------
# Số cháu theo từng ngày (MỚI ở v4)
# ------------------------------------------------------------------
def resolve_headcounts_by_day(uniform_counts, per_day_json, label):
    """Trả về dict {headcount_row: (sang, nha_tre, mau_giao)}.
    - Nếu per_day_json có giá trị: parse JSON, PHẢI đúng 6 bộ 3 số
      (1 bộ / ngày, theo thứ tự Thứ 2 -> Thứ 7).
    - Nếu không: dùng uniform_counts (1 bộ số) cho cả 6 ngày, như bản cũ.
    """
    n_days = len(DAY_HEADCOUNT_ROWS)

    if not per_day_json:
        return {row: tuple(uniform_counts) for row in DAY_HEADCOUNT_ROWS}

    try:
        arr = json.loads(per_day_json)
    except json.JSONDecodeError as e:
        raise ValueError(
            f"--{label}-counts-per-day không phải JSON hợp lệ: {e}. "
            f'Ví dụ đúng: [[220,70,290],[220,70,290],[200,65,280],[210,68,285],[215,70,288],[150,50,200]]'
        )

    if not isinstance(arr, list) or len(arr) != n_days:
        raise ValueError(
            f"--{label}-counts-per-day cần đúng {n_days} bộ số (mỗi ngày Thứ 2->Thứ 7 một bộ "
            f"[ăn_sáng, nhà_trẻ, mẫu_giáo]), nhận được {len(arr) if isinstance(arr, list) else 'không phải mảng'}."
        )

    for i, bo in enumerate(arr):
        if not isinstance(bo, list) or len(bo) != 3 or not all(isinstance(x, int) for x in bo):
            raise ValueError(
                f"--{label}-counts-per-day: bộ số thứ {i + 1} (thứ {THU_NAMES[i] if i < len(THU_NAMES) else i}) "
                f"phải là mảng đúng 3 số nguyên [ăn_sáng, nhà_trẻ, mẫu_giáo], nhận được {bo!r}."
            )

    return {row: tuple(arr[i]) for i, row in enumerate(DAY_HEADCOUNT_ROWS)}


# ------------------------------------------------------------------
# Dò động cấu trúc từng khối ngày (không dùng offset dòng cố định)
# ------------------------------------------------------------------
def scan_block(ws, headcount_row):
    """Trả về dict các dòng quan trọng trong 1 khối ngày, dò bằng text.
    Không giả định số dòng cố định giữa các khối (vì số nguyên liệu mỗi
    ngày khác nhau)."""
    block_start = headcount_row - 4
    info = {
        "block_start": block_start,
        "date_row": block_start,
        "budget_row": headcount_row + 1,
        "giavi_total_row": None,
        "muc3_header_row": None,
        "muc3_total_row": None,
        "muc4_header_row": None,
        "muc4_total_row": None,
        "block_end_row": None,
    }
    r = block_start
    state = None
    max_scan = block_start + 250
    while r <= max_scan:
        a = ws.cell(row=r, column=1).value
        b = ws.cell(row=r, column=2).value
        if isinstance(a, str):
            s = a.strip()
            if s.startswith("2. Theo dõi xuất kho"):
                state = "muc2"
            elif s.startswith("3. Chi tiền thực phẩm"):
                state = "muc3"
                info["muc3_header_row"] = r
            elif s.startswith("4. Chi tiền thực phẩm"):
                state = "muc4"
                info["muc4_header_row"] = r
            elif s.startswith("Nhà bếp"):
                info["block_end_row"] = r
                break
        if isinstance(b, str) and b.strip() == "Tổng cộng":
            if state == "muc2":
                info["giavi_total_row"] = r
            elif state == "muc3":
                info["muc3_total_row"] = r
            elif state == "muc4":
                info["muc4_total_row"] = r
        r += 1
    return info


# ------------------------------------------------------------------
# Cập nhật ngày tháng
# ------------------------------------------------------------------
def update_dates(ws, start_date: datetime.date):
    for i, headcount_row in enumerate(DAY_HEADCOUNT_ROWS):
        info = scan_block(ws, headcount_row)
        d = start_date + datetime.timedelta(days=i)
        thu = THU_NAMES[i] if i < len(THU_NAMES) else f"Thứ {i+2}"
        ws.cell(row=info["date_row"], column=DATE_COL).value = (
            f"{thu}, ngày {d.day:02d} tháng {d.month} năm {d.year}"
        )


# ------------------------------------------------------------------
# Đóng băng liên kết ngoài
# ------------------------------------------------------------------
def freeze_external_links(src_path: Path, dst_path: Path) -> None:
    wb_f = load_workbook(src_path, data_only=False)
    wb_v = load_workbook(src_path, data_only=True)
    ext_pattern = re.compile(r"\[\d+\]")
    for sheetname in wb_f.sheetnames:
        ws_f, ws_v = wb_f[sheetname], wb_v[sheetname]
        for row in ws_f.iter_rows():
            for cell in row:
                v = cell.value
                if isinstance(v, str) and v.startswith("=") and ext_pattern.search(v):
                    cell.value = ws_v[cell.coordinate].value
    wb_f.save(dst_path)


# ------------------------------------------------------------------
# Nhân bản Chợ TC -> Chợ PH ok / Chợ ĐT ok
# ------------------------------------------------------------------
def clone_cho_sheet(wb, src_ws, new_name, headcounts_by_day):
    """headcounts_by_day: dict {headcount_row: (sang, nha_tre, mau_giao)} —
    cho phép số cháu KHÁC NHAU từng ngày (mỗi dòng trong DAY_HEADCOUNT_ROWS
    lấy đúng bộ số của riêng nó, không còn ép cả tuần dùng chung 1 bộ)."""
    if new_name in wb.sheetnames:
        del wb[new_name]
    ws_new = wb.create_sheet(new_name)
    WorksheetCopy(src_ws, ws_new).copy_worksheet()
    write_headcounts_to_sheet(ws_new, headcounts_by_day)
    return ws_new


def write_headcounts_to_sheet(ws, headcounts_by_day):
    """Ghi số cháu (sáng, nhà trẻ, mẫu giáo) vào đúng 6 dòng headcount của 1
    sheet — dùng chung cho cả việc nhân bản sang PH/ĐT LẪN việc ghi đè trực
    tiếp lên chính Chợ TC (khi người dùng muốn nhập số cháu TC qua API thay
    vì gõ tay sẵn trong file trước khi upload)."""
    for r in DAY_HEADCOUNT_ROWS:
        hc = headcounts_by_day[r]
        ws.cell(row=r, column=8).value = hc[0]
        ws.cell(row=r, column=9).value = hc[1]
        ws.cell(row=r, column=10).value = hc[2]


def force_balance_totals(ws, orange=False):
    """Ép Tổng cộng mục 3/4 = Ngân sách - Gia vị (Chênh lệch cuối ngày = 0),
    KHÔNG đụng tới số lượng chi tiết từng dòng — dùng cho cả Chợ TC (giữ số
    liệu chi tiết nguyên vẹn) lẫn Chợ PH ok/ĐT ok."""
    FILL = ORANGE_FILL if orange else YELLOW
    note = (
        "CẢNH BÁO: Tổng cộng đã được ép khớp đúng ngân sách (Chênh lệch = 0) để tránh "
        "hiển thị số tiền sai lệch. Số lượng nguyên liệu chi tiết bên dưới vẫn là số liệu "
        "CŨ, CHƯA khớp với món mới — bếp/kế toán cần nhập lại số thật theo đúng thực đơn."
    ) if orange else NOTE_TEXT

    for headcount_row in DAY_HEADCOUNT_ROWS:
        info = scan_block(ws, headcount_row)

        if info["giavi_total_row"] is None:
            print(f"⚠ Dòng {headcount_row}: không dò được 'Tổng cộng' mục gia vị (mục 2) — "
                  "bỏ qua ép Chênh lệch=0 cho khối ngày này để tránh ghi công thức hỏng.")
            continue

        if info["muc3_header_row"] and info["muc3_total_row"]:
            ws.cell(row=info["muc3_total_row"], column=6).value = (
                f"=IFERROR(H{info['budget_row']}-H{info['giavi_total_row']},0)"
            )
            c = ws.cell(row=info["muc3_header_row"], column=1)
            c.comment = Comment(note, "AutoScript")
            if orange:
                c.fill = FILL
        if info["muc4_header_row"] and info["muc4_total_row"]:
            ws.cell(row=info["muc4_total_row"], column=7).value = (
                f"=IFERROR(I{info['budget_row']}-I{info['giavi_total_row']},0)"
            )
            ws.cell(row=info["muc4_total_row"], column=8).value = (
                f"=IFERROR(J{info['budget_row']}-J{info['giavi_total_row']},0)"
            )
            c = ws.cell(row=info["muc4_header_row"], column=1)
            c.comment = Comment(note, "AutoScript")
            if orange:
                c.fill = FILL


def scale_highlight_and_balance(ws, source_headcounts, target_headcounts_by_day):
    """Với mỗi khối ngày (dò động):
    - Chia tỷ lệ + tô vàng số lượng ở mục 3/4 (ước tính, cần rà tay), theo
      ĐÚNG số cháu của NGÀY ĐÓ (mỗi ngày có thể có số cháu khác nhau).
    - Ép Tổng cộng mục 3/4 = Ngân sách - Gia vị đã tính (mục 2), đảm bảo
      Chênh lệch cuối ngày = 0 (không hiển thị số tiền "ảo")."""
    highlighted = 0
    for headcount_row in DAY_HEADCOUNT_ROWS:
        info = scan_block(ws, headcount_row)
        src_hc = source_headcounts.get(headcount_row, (0, 0, 0))
        target_headcounts = target_headcounts_by_day[headcount_row]

        if info["giavi_total_row"] is None:
            print(f"⚠ Dòng {headcount_row}: không dò được 'Tổng cộng' mục gia vị (mục 2) — "
                  "bỏ qua ép Chênh lệch=0 cho khối ngày này để tránh ghi công thức hỏng.")
            continue

        def ratio(idx):
            s = src_hc[idx]
            return (target_headcounts[idx] / s) if s else None

        # --- mục 3: chia tỷ lệ số lượng (cột D) theo "ăn sáng" ---
        if info["muc3_header_row"] and info["muc3_total_row"]:
            rt = ratio(0)
            rr = info["muc3_header_row"] + 2
            while rr < info["muc3_total_row"]:
                d_cell = ws.cell(row=rr, column=4)
                if d_cell.value is not None and not (isinstance(d_cell.value, str) and d_cell.value.startswith("=")):
                    if rt is not None and isinstance(d_cell.value, (int, float)):
                        d_cell.value = round(d_cell.value * rt, 2)
                    d_cell.fill = YELLOW
                    highlighted += 1
                rr += 1
            ws.cell(row=info["muc3_header_row"], column=1).comment = Comment(NOTE_TEXT, "AutoScript")
            # ép Tổng cộng = Ngân sách sáng - Gia vị sáng
            ws.cell(row=info["muc3_total_row"], column=6).value = (
                f"=IFERROR(H{info['budget_row']}-H{info['giavi_total_row']},0)"
            )

        # --- mục 4: chia tỷ lệ số lượng (cột D=Nhà trẻ, E=Mẫu giáo) ---
        if info["muc4_header_row"] and info["muc4_total_row"]:
            rt_nt, rt_mg = ratio(1), ratio(2)
            rr = info["muc4_header_row"] + 3
            while rr < info["muc4_total_row"]:
                for col, rt in ((4, rt_nt), (5, rt_mg)):
                    cell = ws.cell(row=rr, column=col)
                    if cell.value is not None and not (isinstance(cell.value, str) and cell.value.startswith("=")):
                        if rt is not None and isinstance(cell.value, (int, float)):
                            cell.value = round(cell.value * rt, 2)
                        cell.fill = YELLOW
                        highlighted += 1
                rr += 1
            ws.cell(row=info["muc4_header_row"], column=1).comment = Comment(NOTE_TEXT, "AutoScript")
            # ép Tổng cộng Nhà trẻ/Mẫu giáo = Ngân sách - Gia vị
            ws.cell(row=info["muc4_total_row"], column=7).value = (
                f"=IFERROR(I{info['budget_row']}-I{info['giavi_total_row']},0)"
            )
            ws.cell(row=info["muc4_total_row"], column=8).value = (
                f"=IFERROR(J{info['budget_row']}-J{info['giavi_total_row']},0)"
            )

    return highlighted


def read_source_headcounts(ws):
    result = {}
    for r in DAY_HEADCOUNT_ROWS:
        result[r] = (
            ws.cell(row=r, column=8).value or 0,
            ws.cell(row=r, column=9).value or 0,
            ws.cell(row=r, column=10).value or 0,
        )
    return result


# ------------------------------------------------------------------
# Recalc / auto-fix #DIV/0!
# ------------------------------------------------------------------
def _soffice_path():
    import shutil as _shutil
    for name in ("soffice", "soffice.exe"):
        p = _shutil.which(name)
        if p:
            return p
    for c in (r"C:\Program Files\LibreOffice\program\soffice.exe",
              r"C:\Program Files (x86)\LibreOffice\program\soffice.exe"):
        if Path(c).exists():
            return c
    return None


def _recalc_with_libreoffice(path: Path, timeout: int) -> dict:
    import tempfile as _tempfile
    soffice = _soffice_path()
    if not soffice:
        raise FileNotFoundError("soffice-not-found")
    profile_dir = Path(_tempfile.mkdtemp(prefix="lo_profile_"))
    out_dir = Path(_tempfile.mkdtemp(prefix="lo_out_"))
    subprocess.run(
        [soffice, "--headless", "--norestore", "--nologo",
         f"-env:UserInstallation={profile_dir.as_uri()}",
         "--convert-to", "xlsx:Calc MS Excel 2007 XML",
         "--outdir", str(out_dir), str(path)],
        timeout=timeout, check=True, capture_output=True,
    )
    converted = out_dir / path.name
    if converted.exists():
        converted.replace(path)
    wb = load_workbook(path, data_only=True)
    error_summary, total_errors = {}, 0
    ERR_STRINGS = {"#DIV/0!", "#N/A", "#VALUE!", "#REF!", "#NAME?", "#NULL!", "#NUM!"}
    for ws in wb.worksheets:
        for row in ws.iter_rows():
            for cell in row:
                if isinstance(cell.value, str) and cell.value in ERR_STRINGS:
                    error_summary.setdefault(cell.value, {"count": 0, "locations": []})
                    error_summary[cell.value]["count"] += 1
                    error_summary[cell.value]["locations"].append(f"{ws.title}!{cell.coordinate}")
                    total_errors += 1
    return {"total_errors": total_errors, "error_summary": error_summary}


def autofix_div0_errors(path: Path, max_rounds: int = 4, timeout: int = 180) -> int:
    if not _soffice_path():
        print("\n⚠ Không tìm thấy LibreOffice — bỏ qua bước tự dò lỗi #DIV/0!. "
              "Cài LibreOffice (libreoffice.org) nếu muốn script tự làm bước này.\n")
        return 0
    total_fixed = 0
    for _ in range(max_rounds):
        try:
            data = _recalc_with_libreoffice(path, timeout)
        except Exception as e:
            print(f"⚠ Lỗi khi chạy LibreOffice recalc: {e}. Bỏ qua bước auto-fix.")
            return total_fixed
        if data.get("total_errors", 0) == 0:
            break
        wb = load_workbook(path, data_only=False)
        fixed_this_round = 0
        for _err_type, info in data.get("error_summary", {}).items():
            for loc in info.get("locations", []):
                sheet_name, coord = loc.split("!")
                cell = wb[sheet_name][coord]
                v = cell.value
                if isinstance(v, str) and v.startswith("=") and not v.upper().startswith("=IFERROR("):
                    cell.value = "=IFERROR(" + v[1:] + ",0)"
                    fixed_this_round += 1
        if fixed_this_round == 0:
            break
        wb.save(path)
        total_fixed += fixed_this_round
    return total_fixed


def main():
    ap = argparse.ArgumentParser(description="Dựng Chợ PH ok/ĐT ok từ Chợ TC — tự dò cấu trúc, tự cập nhật ngày, tự cân Chênh lệch=0")
    ap.add_argument("input", type=Path)
    ap.add_argument("output", type=Path)
    ap.add_argument("--ph-counts", type=int, nargs=3, required=True, metavar=("SANG", "NHATRE", "MAUGIAO"),
                     help="Số cháu PH áp dụng cho CẢ TUẦN — dùng làm mặc định khi không có --ph-counts-per-day.")
    ap.add_argument("--dt-counts", type=int, nargs=3, required=True, metavar=("SANG", "NHATRE", "MAUGIAO"),
                     help="Số cháu ĐT áp dụng cho CẢ TUẦN — dùng làm mặc định khi không có --dt-counts-per-day.")
    ap.add_argument("--ph-counts-per-day", type=str, default=None,
                     help="JSON 6 bộ [sáng,nhà_trẻ,mẫu_giáo] cho PH, theo thứ tự Thứ 2->Thứ 7. "
                          "Có thì DÙNG THAY --ph-counts (số cháu khác nhau từng ngày).")
    ap.add_argument("--dt-counts-per-day", type=str, default=None,
                     help="JSON 6 bộ [sáng,nhà_trẻ,mẫu_giáo] cho ĐT, theo thứ tự Thứ 2->Thứ 7. "
                          "Có thì DÙNG THAY --dt-counts (số cháu khác nhau từng ngày).")
    ap.add_argument("--tc-counts", type=int, nargs=3, default=None, metavar=("SANG", "NHATRE", "MAUGIAO"),
                     help="TÙY CHỌN: ghi đè số cháu TC (cả tuần 1 bộ số). Bỏ qua cờ này để GIỮ NGUYÊN "
                          "số cháu TC đã có sẵn trong file (cách dùng mặc định từ trước tới nay).")
    ap.add_argument("--tc-counts-per-day", type=str, default=None,
                     help="TÙY CHỌN: ghi đè số cháu TC khác nhau từng ngày, JSON 6 bộ giống --ph-counts-per-day. "
                          "Có thì DÙNG THAY --tc-counts. Bỏ cả 2 cờ này để giữ nguyên số cháu TC có sẵn trong file.")
    ap.add_argument("--start-date", type=str, default=None, help="Ngày Thứ 2 đầu tuần, định dạng YYYY-MM-DD. Bỏ qua nếu không muốn tự đổi ngày.")
    ap.add_argument("--ph-name", default="Chợ PH ok")
    ap.add_argument("--dt-name", default="Chợ ĐT ok")
    ap.add_argument("--no-force-balance-tc", action="store_true",
                     help="KHÔNG ép Chênh lệch=0 cho chính Chợ TC (mặc định CÓ ép, vì dữ liệu mục 3-4 thường bị lệch khi đổi món mới mà chưa cập nhật số liệu thật).")
    args = ap.parse_args()

    try:
        ph_by_day = resolve_headcounts_by_day(args.ph_counts, args.ph_counts_per_day, "ph")
        dt_by_day = resolve_headcounts_by_day(args.dt_counts, args.dt_counts_per_day, "dt")
        tc_by_day = None
        if args.tc_counts or args.tc_counts_per_day:
            # uniform_counts có thể None ở đây nếu chỉ truyền --tc-counts-per-day — vẫn an
            # toàn vì resolve_headcounts_by_day chỉ dùng uniform_counts khi per_day_json rỗng.
            tc_by_day = resolve_headcounts_by_day(args.tc_counts, args.tc_counts_per_day, "tc")
    except ValueError as e:
        print(f"❌ Lỗi tham số: {e}")
        raise SystemExit(2)

    tmp_frozen = args.output.with_suffix(".frozen.tmp.xlsx")
    freeze_external_links(args.input, tmp_frozen)

    wb = load_workbook(tmp_frozen, data_only=False)
    src_cho = wb[CHO_TC_SHEET]

    if args.start_date:
        start_date = datetime.datetime.strptime(args.start_date, "%Y-%m-%d").date()
        update_dates(src_cho, start_date)
        print(f"Đã cập nhật ngày tháng cho Chợ TC, bắt đầu {start_date.strftime('%d/%m/%Y')} (Thứ hai)")

    if tc_by_day is not None:
        write_headcounts_to_sheet(src_cho, tc_by_day)
        print("Đã ghi đè số cháu TC theo tham số --tc-counts/--tc-counts-per-day (thay cho số cũ có sẵn trong file)")

    source_headcounts = read_source_headcounts(src_cho)

    if not args.no_force_balance_tc:
        force_balance_totals(src_cho, orange=True)
        print("Chợ TC: đã ép Chênh lệch cuối ngày = 0 (giữ nguyên số lượng chi tiết, chỉ ép dòng Tổng cộng)")

    ws_ph = clone_cho_sheet(wb, src_cho, args.ph_name, ph_by_day)
    ws_dt = clone_cho_sheet(wb, src_cho, args.dt_name, dt_by_day)

    for ws, target_by_day in ((ws_ph, ph_by_day), (ws_dt, dt_by_day)):
        n_hl = scale_highlight_and_balance(ws, source_headcounts, target_by_day)
        print(f"{ws.title}: chia tỷ lệ + tô vàng {n_hl} ô | Chênh lệch cuối ngày đã ép = 0")

    wb.save(args.output)
    tmp_frozen.unlink(missing_ok=True)

    n_fixed = autofix_div0_errors(args.output)
    print(f"Đã tự động bọc IFERROR cho {n_fixed} ô bị lỗi phép chia")
    print(f"\nHoàn tất: {args.output}")


if __name__ == "__main__":
    main()
