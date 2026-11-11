#!/usr/bin/env python3
"""
build_recipe_database.py
==========================
Trích xuất "cơ sở dữ liệu công thức món ăn" từ nhiều file tuần THẬT (đã có
sẵn thực đơn khớp đúng nguyên liệu trong Chợ TC), gộp thành 1 file JSON +
1 file Excel tổng hợp để tra cứu khi soạn thực đơn tuần mới.

CÁCH DÙNG
---------
python3 build_recipe_database.py <file1.xlsx> <file2.xlsx> ... --out-json recipes.json --out-xlsx recipes.xlsx

NHỮNG GÌ SCRIPT TRÍCH XUẤT
----------------------------
1. MỤC 2 (Gia vị hằng ngày): danh sách nguyên liệu CỐ ĐỊNH luôn xuất hiện
   mỗi ngày (không phụ thuộc món), kèm định lượng/cháu trung bình + khoảng
   dao động thực tế (min-max) quan sát được qua các tuần.

2. MỤC 3 (Chi tiền thực phẩm sáng): map trực tiếp MÓN SÁNG -> nguyên liệu
   + định lượng/cháu — rõ ràng vì mỗi ngày chỉ có đúng 1 món sáng.

3. MỤC 4 (Ăn chính + xế): vì dữ liệu gốc GỘP CHUNG nguyên liệu của 5 món
   (Canh/Mặn/Xào/Tráng miệng/Xế) trong 1 ngày mà KHÔNG tách riêng từng
   món, script làm 2 việc:
   a. Phân tích TẦN SUẤT xuất hiện của từng nguyên liệu qua tất cả các
      ngày đã thu thập -> nguyên liệu xuất hiện ở phần lớn số ngày
      (>= NGUONG_CODINH, mặc định 70%) được xếp là "NỀN CỐ ĐỊNH" (ít phụ
      thuộc món cụ thể chọn hôm đó — ví dụ hành, tỏi, dầu ăn cơ bản).
      Nguyên liệu xuất hiện ít hơn được xếp "GẮN VỚI TỔ HỢP MÓN CỤ THỂ".
   b. Vẫn lưu đầy đủ "tổ hợp món -> nguyên liệu" theo từng ngày gốc, để
      khi tổ hợp giống hệt lặp lại, có ngay dữ liệu dùng thẳng.

GIỚI HẠN CẦN BIẾT
-------------------
- Với món MỚI hoàn toàn chưa từng xuất hiện trong dữ liệu nguồn, script
  KHÔNG tự bịa định lượng — chỉ có thể gợi ý dùng "nền cố định" + để trống
  phần nguyên liệu riêng của món đó, chờ bếp bổ sung tay.
- "Tổ hợp món -> nguyên liệu" (mục 4) chỉ tái sử dụng chính xác khi lặp
  đúng NGUYÊN CẢ 5 món trong tổ hợp, không suy ra được cho từng món lẻ.
"""

import argparse
import json
import re
from collections import defaultdict
from pathlib import Path

from openpyxl import Workbook, load_workbook
from openpyxl.styles import Font, PatternFill

DAY_HEADCOUNT_ROWS = [7, 97, 187, 277, 367, 457]
DAYS_COLS = ["B", "C", "D", "E", "F", "G"]
DAYS_NAMES = ["Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7"]
NGUONG_CODINH = 0.7  # >=70% số ngày xuất hiện -> coi là nguyên liệu nền cố định
DATE_RE = re.compile(r"ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})")


def find_sheet(wb, candidates):
    for c in candidates:
        for name in wb.sheetnames:
            if name.strip() == c.strip():
                return wb[name]
    return None


def scan_block(ws, headcount_row):
    block_start = headcount_row - 4
    info = {"muc2_header": None, "muc2_total": None,
            "muc3_header": None, "muc3_total": None,
            "muc4_header": None, "muc4_total": None}
    r = block_start
    state = None
    while r <= block_start + 250:
        a = ws.cell(row=r, column=1).value
        b = ws.cell(row=r, column=2).value
        if isinstance(a, str):
            s = a.strip()
            if s.startswith("2. Theo dõi xuất kho"):
                state = "muc2"; info["muc2_header"] = r
            elif s.startswith("3. Chi tiền thực phẩm"):
                state = "muc3"; info["muc3_header"] = r
            elif s.startswith("4. Chi tiền thực phẩm"):
                state = "muc4"; info["muc4_header"] = r
            elif s.startswith("Nhà bếp"):
                break
        if isinstance(b, str) and b.strip() == "Tổng cộng":
            if state == "muc2": info["muc2_total"] = r
            elif state == "muc3": info["muc3_total"] = r
            elif state == "muc4": info["muc4_total"] = r
        r += 1
    return info


def find_headcount_rows(ws):
    """Tự dò các dòng có 'Xuất ăn trong ngày' — không giả định cách nhau
    cố định 90 dòng, vì số dòng mỗi khối có thể khác nhau giữa các file.
    Dò trên NHIỀU CỘT (1-10), không chỉ cột D — vì có file (VD "TUẦN 1
    THÁNG 9 A") bị lệch cột nhãn giữa chừng (nửa đầu tuần nhãn ở cột D,
    nửa sau lại ở cột F, dù số liệu thật vẫn luôn nằm ở cột H/I/J cố
    định — chỉ MIÊU TẢ nhãn bị dịch cột do lỗi soạn thảo)."""
    rows = []
    for r in range(1, ws.max_row + 1):
        for c in range(1, 11):
            d = ws.cell(row=r, column=c).value
            if isinstance(d, str) and "Xuất ăn trong ngày" in d:
                rows.append(r)
                break
    return rows


def extract_one_file(path: Path, expected_month: int | None = None):
    """Trả về list các ngày đã trích xuất từ 1 file tuần.

    expected_month: nếu có (VD 9 cho tháng 9), BỎ QUA các khối ngày mà nhãn
    ngày tháng thật (dò trong ô, KHÔNG suy từ vị trí) không khớp tháng này —
    vì có file tái sử dụng từ tuần trước, chỉ cập nhật 1 phần, còn lại vẫn
    là dữ liệu tháng cũ (VD "TUẦN 1 THÁNG 9 A" có 3/6 khối là dữ liệu
    tháng 7 sót lại). Nếu không lọc, dữ liệu cũ sẽ bị ghép NHẦM với tên
    món của tuần đang xử lý, làm hỏng cả cơ sở dữ liệu công thức.
    """
    wb = load_workbook(path, data_only=True)
    ws_menu = wb["Thực đơn tuần ok"]
    ws_cho = find_sheet(wb, ["Chợ TC"])
    if ws_cho is None:
        print(f"  [BỎ QUA] {path.name}: không tìm thấy sheet Chợ TC")
        return []

    headcount_rows = find_headcount_rows(ws_cho)
    if len(headcount_rows) != 6:
        print(f"  [CẢNH BÁO] {path.name}: tìm thấy {len(headcount_rows)} khối ngày (kỳ vọng 6) — {headcount_rows}")

    days_out = []
    for i, hc_row in enumerate(headcount_rows[:6]):
        col = DAYS_COLS[i]

        if expected_month is not None:
            date_text = ws_cho.cell(row=hc_row - 4, column=7).value
            m = DATE_RE.search(date_text) if isinstance(date_text, str) else None
            if not m or int(m.group(2)) != expected_month:
                thang_thuc_te = m.group(2) if m else "?"
                print(f"  [BỎ QUA khối ngày {DAYS_NAMES[i]}] {path.name}: nhãn ghi tháng {thang_thuc_te}, "
                      f"khác tháng {expected_month} đang xử lý — có thể là dữ liệu cũ còn sót lại, không dùng.")
                continue

        info = scan_block(ws_cho, hc_row)
        sang_hc = ws_cho.cell(row=hc_row, column=8).value or 0
        nt_hc = ws_cho.cell(row=hc_row, column=9).value or 0
        mg_hc = ws_cho.cell(row=hc_row, column=10).value or 0
        if not isinstance(sang_hc, (int, float)): sang_hc = 0
        if not isinstance(nt_hc, (int, float)): nt_hc = 0
        if not isinstance(mg_hc, (int, float)): mg_hc = 0

        dish = {
            "sang": ws_menu[f"{col}7"].value,
            "canh": ws_menu[f"{col}8"].value,
            "man": ws_menu[f"{col}9"].value,
            "xao": ws_menu[f"{col}10"].value,
            "trang_mieng": ws_menu[f"{col}11"].value,
            "xe": ws_menu[f"{col}12"].value,
        }

        # mục 2 (gia vị hằng ngày)
        gia_vi = []
        if info["muc2_header"] and info["muc2_total"]:
            for r in range(info["muc2_header"] + 2, info["muc2_total"]):
                name = ws_cho.cell(row=r, column=2).value
                q_sang = ws_cho.cell(row=r, column=4).value
                q_nt = ws_cho.cell(row=r, column=5).value
                q_mg = ws_cho.cell(row=r, column=6).value
                if name:
                    gia_vi.append({
                        "ten": name.strip() if isinstance(name, str) else name,
                        "rate_sang": round(q_sang / sang_hc, 6) if isinstance(q_sang, (int, float)) and sang_hc else None,
                        "rate_nt": round(q_nt / nt_hc, 6) if isinstance(q_nt, (int, float)) and nt_hc else None,
                        "rate_mg": round(q_mg / mg_hc, 6) if isinstance(q_mg, (int, float)) and mg_hc else None,
                    })

        # mục 3 (món sáng)
        ingredients_sang = []
        if info["muc3_header"] and info["muc3_total"] and sang_hc:
            for r in range(info["muc3_header"] + 2, info["muc3_total"]):
                name = ws_cho.cell(row=r, column=2).value
                qty = ws_cho.cell(row=r, column=4).value
                if name and isinstance(qty, (int, float)) and qty != 0:
                    ingredients_sang.append({"ten": name.strip() if isinstance(name, str) else name,
                                              "rate": round(qty / sang_hc, 6)})

        # mục 4 (tổ hợp ăn chính + xế)
        ingredients_combo = []
        if info["muc4_header"] and info["muc4_total"]:
            for r in range(info["muc4_header"] + 3, info["muc4_total"]):
                name = ws_cho.cell(row=r, column=2).value
                q_nt = ws_cho.cell(row=r, column=4).value
                q_mg = ws_cho.cell(row=r, column=5).value
                rate_nt = round(q_nt / nt_hc, 6) if isinstance(q_nt, (int, float)) and q_nt != 0 and nt_hc else None
                rate_mg = round(q_mg / mg_hc, 6) if isinstance(q_mg, (int, float)) and q_mg != 0 and mg_hc else None
                if name and (rate_nt or rate_mg):
                    ingredients_combo.append({"ten": name.strip() if isinstance(name, str) else name,
                                               "rate_nt": rate_nt, "rate_mg": rate_mg})

        days_out.append({
            "file": path.name,
            "ngay": DAYS_NAMES[i],
            "mon": dish,
            "gia_vi": gia_vi,
            "nguyen_lieu_sang": ingredients_sang,
            "nguyen_lieu_combo": ingredients_combo,
        })

    return days_out


def build_database(all_days):
    n_days = len(all_days)

    # --- mục 2: gộp theo tên nguyên liệu, tính trung bình + khoảng dao động ---
    gia_vi_agg = defaultdict(lambda: {"rate_sang": [], "rate_nt": [], "rate_mg": []})
    for d in all_days:
        for item in d["gia_vi"]:
            key = item["ten"]
            for f in ("rate_sang", "rate_nt", "rate_mg"):
                if item[f] is not None:
                    gia_vi_agg[key][f].append(item[f])

    gia_vi_co_dinh = {}
    for name, rates in gia_vi_agg.items():
        entry = {}
        for f in ("rate_sang", "rate_nt", "rate_mg"):
            vals = rates[f]
            if vals:
                entry[f] = {
                    "trung_binh": round(sum(vals) / len(vals), 6),
                    "min": round(min(vals), 6),
                    "max": round(max(vals), 6),
                    "so_lan_quan_sat": len(vals),
                }
        gia_vi_co_dinh[name] = entry

    # --- mục 3: gộp theo tên món sáng ---
    mon_sang_agg = defaultdict(lambda: defaultdict(list))
    mon_sang_count = defaultdict(int)
    for d in all_days:
        dish = d["mon"]["sang"]
        if not dish:
            continue
        mon_sang_count[dish] += 1
        for ing in d["nguyen_lieu_sang"]:
            mon_sang_agg[dish][ing["ten"]].append(ing["rate"])

    mon_sang_db = {}
    for dish, ingredients in mon_sang_agg.items():
        mon_sang_db[dish] = {
            "so_lan_xuat_hien": mon_sang_count[dish],
            "nguyen_lieu": {
                name: {"trung_binh": round(sum(v) / len(v), 6), "min": round(min(v), 6), "max": round(max(v), 6)}
                for name, v in ingredients.items()
            },
        }

    # --- mục 4: phân tích tần suất nền cố định + lưu tổ hợp theo ngày ---
    ingredient_day_count = defaultdict(int)
    ingredient_rates = defaultdict(lambda: {"nt": [], "mg": []})
    combos = []
    for d in all_days:
        if not d["nguyen_lieu_combo"]:
            continue
        names_today = set()
        for ing in d["nguyen_lieu_combo"]:
            names_today.add(ing["ten"])
            if ing["rate_nt"] is not None:
                ingredient_rates[ing["ten"]]["nt"].append(ing["rate_nt"])
            if ing["rate_mg"] is not None:
                ingredient_rates[ing["ten"]]["mg"].append(ing["rate_mg"])
        for name in names_today:
            ingredient_day_count[name] += 1
        combos.append({
            "file": d["file"], "ngay": d["ngay"],
            "canh": d["mon"]["canh"], "man": d["mon"]["man"], "xao": d["mon"]["xao"],
            "trang_mieng": d["mon"]["trang_mieng"], "xe": d["mon"]["xe"],
            "nguyen_lieu": d["nguyen_lieu_combo"],
        })

    n_combo_days = len([d for d in all_days if d["nguyen_lieu_combo"]])
    nen_co_dinh = {}
    gan_theo_mon = {}
    for name, count in ingredient_day_count.items():
        freq = count / n_combo_days if n_combo_days else 0
        rates = ingredient_rates[name]
        entry = {
            "so_ngay_xuat_hien": count,
            "tong_so_ngay": n_combo_days,
            "tan_suat": round(freq, 3),
            "rate_nt_trung_binh": round(sum(rates["nt"]) / len(rates["nt"]), 6) if rates["nt"] else None,
            "rate_mg_trung_binh": round(sum(rates["mg"]) / len(rates["mg"]), 6) if rates["mg"] else None,
        }
        if freq >= NGUONG_CODINH:
            nen_co_dinh[name] = entry
        else:
            gan_theo_mon[name] = entry

    return {
        "so_ngay_du_lieu": n_days,
        "muc2_gia_vi_hang_ngay": gia_vi_co_dinh,
        "muc3_mon_sang": mon_sang_db,
        "muc4_nen_co_dinh": nen_co_dinh,
        "muc4_nguyen_lieu_gan_theo_mon": gan_theo_mon,
        "muc4_to_hop_theo_ngay_goc": combos,
    }


def write_excel_report(db, out_path: Path):
    wb = Workbook()
    wb.remove(wb.active)
    HEADER_FILL = PatternFill(start_color="1F3864", end_color="1F3864", fill_type="solid")
    HEADER_FONT = Font(bold=True, color="FFFFFF")

    def header_row(ws, headers):
        for c, h in enumerate(headers, start=1):
            cell = ws.cell(row=1, column=c, value=h)
            cell.font = HEADER_FONT
            cell.fill = HEADER_FILL

    # Sheet 1: Gia vị hằng ngày
    ws1 = wb.create_sheet("Gia vị hằng ngày (mục 2)")
    header_row(ws1, ["Tên nguyên liệu", "Rate/cháu Sáng (TB)", "Rate/cháu Nhà trẻ (TB)", "Rate/cháu Mẫu giáo (TB)", "Số lần quan sát"])
    r = 2
    for name, entry in db["muc2_gia_vi_hang_ngay"].items():
        ws1.cell(row=r, column=1, value=name)
        ws1.cell(row=r, column=2, value=entry.get("rate_sang", {}).get("trung_binh"))
        ws1.cell(row=r, column=3, value=entry.get("rate_nt", {}).get("trung_binh"))
        ws1.cell(row=r, column=4, value=entry.get("rate_mg", {}).get("trung_binh"))
        obs = max((entry.get(f, {}).get("so_lan_quan_sat", 0) for f in ("rate_sang", "rate_nt", "rate_mg")), default=0)
        ws1.cell(row=r, column=5, value=obs)
        r += 1
    for col, w in zip("ABCDE", [30, 20, 20, 20, 16]):
        ws1.column_dimensions[col].width = w

    # Sheet 2: Món sáng
    ws2 = wb.create_sheet("Món Sáng (mục 3)")
    header_row(ws2, ["Món", "Nguyên liệu", "Rate/cháu (TB)", "Min", "Max", "Số lần món xuất hiện"])
    r = 2
    for dish, info in db["muc3_mon_sang"].items():
        start_r = r
        for ing, vals in info["nguyen_lieu"].items():
            ws2.cell(row=r, column=1, value=dish)
            ws2.cell(row=r, column=2, value=ing)
            ws2.cell(row=r, column=3, value=vals["trung_binh"])
            ws2.cell(row=r, column=4, value=vals["min"])
            ws2.cell(row=r, column=5, value=vals["max"])
            ws2.cell(row=r, column=6, value=info["so_lan_xuat_hien"])
            r += 1
        if r > start_r + 1:
            ws2.merge_cells(start_row=start_r, start_column=1, end_row=r - 1, end_column=1)
            ws2.merge_cells(start_row=start_r, start_column=6, end_row=r - 1, end_column=6)
    for col, w in zip("ABCDEF", [26, 30, 16, 12, 12, 18]):
        ws2.column_dimensions[col].width = w

    # Sheet 3: Nguyên liệu nền cố định (mục 4)
    ws3 = wb.create_sheet("Nền cố định (mục 4)")
    header_row(ws3, ["Nguyên liệu", "Tần suất xuất hiện", "Số ngày / Tổng ngày", "Rate/cháu Nhà trẻ (TB)", "Rate/cháu Mẫu giáo (TB)"])
    r = 2
    for name, e in sorted(db["muc4_nen_co_dinh"].items(), key=lambda x: -x[1]["tan_suat"]):
        ws3.cell(row=r, column=1, value=name)
        ws3.cell(row=r, column=2, value=f"{e['tan_suat']*100:.0f}%")
        ws3.cell(row=r, column=3, value=f"{e['so_ngay_xuat_hien']}/{e['tong_so_ngay']}")
        ws3.cell(row=r, column=4, value=e["rate_nt_trung_binh"])
        ws3.cell(row=r, column=5, value=e["rate_mg_trung_binh"])
        r += 1
    for col, w in zip("ABCDE", [30, 16, 16, 20, 20]):
        ws3.column_dimensions[col].width = w

    # Sheet 4: Nguyên liệu gắn theo món cụ thể
    ws4 = wb.create_sheet("Gắn theo món (mục 4)")
    header_row(ws4, ["Nguyên liệu", "Tần suất xuất hiện", "Số ngày / Tổng ngày", "Rate/cháu Nhà trẻ (TB)", "Rate/cháu Mẫu giáo (TB)"])
    r = 2
    for name, e in sorted(db["muc4_nguyen_lieu_gan_theo_mon"].items(), key=lambda x: -x[1]["tan_suat"]):
        ws4.cell(row=r, column=1, value=name)
        ws4.cell(row=r, column=2, value=f"{e['tan_suat']*100:.0f}%")
        ws4.cell(row=r, column=3, value=f"{e['so_ngay_xuat_hien']}/{e['tong_so_ngay']}")
        ws4.cell(row=r, column=4, value=e["rate_nt_trung_binh"])
        ws4.cell(row=r, column=5, value=e["rate_mg_trung_binh"])
        r += 1
    for col, w in zip("ABCDE", [30, 16, 16, 20, 20]):
        ws4.column_dimensions[col].width = w

    # Sheet 5: Tổ hợp theo ngày gốc
    ws5 = wb.create_sheet("Tổ hợp ngày gốc (mục 4)")
    header_row(ws5, ["File", "Ngày", "Canh", "Mặn", "Xào", "Tráng miệng", "Xế", "Nguyên liệu", "Rate NT", "Rate MG"])
    r = 2
    for combo in db["muc4_to_hop_theo_ngay_goc"]:
        start_r = r
        for ing in combo["nguyen_lieu"]:
            ws5.cell(row=r, column=1, value=combo["file"])
            ws5.cell(row=r, column=2, value=combo["ngay"])
            ws5.cell(row=r, column=3, value=combo["canh"])
            ws5.cell(row=r, column=4, value=combo["man"])
            ws5.cell(row=r, column=5, value=combo["xao"])
            ws5.cell(row=r, column=6, value=combo["trang_mieng"])
            ws5.cell(row=r, column=7, value=combo["xe"])
            ws5.cell(row=r, column=8, value=ing["ten"])
            ws5.cell(row=r, column=9, value=ing["rate_nt"])
            ws5.cell(row=r, column=10, value=ing["rate_mg"])
            r += 1
        if r > start_r + 1:
            for col in range(1, 8):
                ws5.merge_cells(start_row=start_r, start_column=col, end_row=r - 1, end_column=col)
    for col, w in zip("ABCDEFGHIJ", [22, 10, 20, 20, 20, 16, 20, 28, 12, 12]):
        ws5.column_dimensions[col].width = w

    wb.save(out_path)


def main():
    ap = argparse.ArgumentParser(description="Trích xuất cơ sở dữ liệu công thức món ăn từ nhiều file tuần")
    ap.add_argument("files", type=Path, nargs="+", help="Các file tuần thật (Chợ TC khớp đúng nguyên liệu-món)")
    ap.add_argument("--out-json", type=Path, default=Path("recipes.json"))
    ap.add_argument("--out-xlsx", type=Path, default=Path("recipes.xlsx"))
    ap.add_argument("--thang", type=int, default=None,
                     help="Chỉ lấy dữ liệu đúng tháng này (VD 9 cho tháng 9) — BỎ QUA các khối ngày ghi "
                          "tháng khác (thường là dữ liệu cũ còn sót lại trong file tái sử dụng từ tuần trước). "
                          "Khuyến nghị LUÔN truyền cờ này khi các file đưa vào cùng thuộc 1 tháng.")
    args = ap.parse_args()

    all_days = []
    for f in args.files:
        print(f"Đang xử lý: {f.name}")
        days = extract_one_file(f, expected_month=args.thang)
        all_days.extend(days)
        print(f"  -> trích được {len(days)} ngày")

    db = build_database(all_days)

    with open(args.out_json, "w", encoding="utf-8") as fp:
        json.dump(db, fp, ensure_ascii=False, indent=2)

    write_excel_report(db, args.out_xlsx)

    print()
    print(f"Tổng số ngày dữ liệu: {db['so_ngay_du_lieu']}")
    print(f"Số nguyên liệu gia vị cố định (mục 2): {len(db['muc2_gia_vi_hang_ngay'])}")
    print(f"Số món sáng khác nhau (mục 3): {len(db['muc3_mon_sang'])}")
    print(f"Số nguyên liệu NỀN CỐ ĐỊNH (mục 4, xuất hiện >= {int(NGUONG_CODINH*100)}% số ngày): {len(db['muc4_nen_co_dinh'])}")
    print(f"Số nguyên liệu GẮN THEO MÓN CỤ THỂ (mục 4): {len(db['muc4_nguyen_lieu_gan_theo_mon'])}")
    print(f"\nĐã lưu: {args.out_json}, {args.out_xlsx}")


if __name__ == "__main__":
    main()
