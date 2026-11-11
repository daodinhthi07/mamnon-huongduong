namespace MamNon.ThucDon.Web.ViewModels;

public sealed class ApDungNgayVm
{
    /// <summary>0 = Thứ 2 ... 5 = Thứ 7.</summary>
    public int Ngay { get; set; }
    public double? Sang { get; set; }
    public double? NhaTre { get; set; }
    public double? MauGiao { get; set; }

    /// <summary>Tiền ăn 1 cháu: ăn sáng / nhà trẻ / mẫu giáo.</summary>
    public double? TienSang { get; set; }
    public double? TienNhaTre { get; set; }
    public double? TienMauGiao { get; set; }
    public string TenThu => LichSuThucDonViewModel.TenThu[Ngay];

    /// <summary>false = ngày này KHÔNG có trong file mẫu (VD Thứ 7): để trống cho người dùng tự nhập nếu tuần mới có học.</summary>
    public bool TrongMau { get; set; } = true;
}

public sealed class ApDungCoSoVm
{
    /// <summary>Khóa cơ sở trong file mẫu: TC, PH, ĐT.</summary>
    public string Khoa { get; set; } = string.Empty;
    public List<ApDungNgayVm> Ngay { get; set; } = new();
}

/// <summary>Món có thể chọn để đổi (đã có định lượng nguyên liệu).</summary>
public sealed class ApDungMonOption
{
    public int Id { get; set; }
    public string Ten { get; set; } = string.Empty;
    public int? NhomId { get; set; }
}

/// <summary>Một ô món của file mẫu (ngày, khối, bữa) mà người dùng có thể đổi sang món khác.</summary>
public sealed class ApDungDoiMonVm
{
    public int Ngay { get; set; }
    public int Khoi { get; set; }
    public int LoaiBuaId { get; set; }
    public int? NhomId { get; set; }
    public byte ThuTuBua { get; set; }
    public string TenBua { get; set; } = string.Empty;
    public string MonMau { get; set; } = string.Empty;
    /// <summary>true = đổi ô này thì Nhà trẻ đổi theo (Sáng, Xế MG = Chính chiều NT).</summary>
    public bool DongBoNhaTre { get; set; }
    public string TenThu => LichSuThucDonViewModel.TenThu[Ngay];
    public string TenKhoi => Khoi == 1 ? "Mẫu giáo" : "Nhà trẻ";

    /// <summary>false = ngày không có trong file mẫu: người dùng CHỌN món cho ngày này (thêm ngày mới vào tuần).</summary>
    public bool TrongMau { get; set; } = true;
}

/// <summary>Trang "Áp dụng tuần": chọn tuần mới + nhập số suất từng ngày, rồi tải Excel y như file mẫu.</summary>
public sealed class ApDungTuanViewModel
{
    public DateOnly Nguon { get; set; }
    public string? Thang { get; set; }
    public string TenMau { get; set; } = string.Empty;
    public DateOnly TuanMoi { get; set; }
    public List<int> NgayCoThucDon { get; set; } = new();
    public List<ApDungCoSoVm> CoSo { get; set; } = new();
    public List<ApDungDoiMonVm> DoiMon { get; set; } = new();
    public List<ApDungMonOption> DsMon { get; set; } = new();
}
