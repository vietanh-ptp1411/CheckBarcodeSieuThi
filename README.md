# Check Barcode Siêu Thị (phần mềm bán hàng / thanh toán)

Phần mềm thu ngân siêu thị viết bằng WPF (.NET 10). Nhận mã vạch từ đầu đọc **ICW VNG**
(Shijie Intelligent Tech) qua **Ethernet TCP/IP**, tạo và in hóa đơn, quản lý danh mục sản phẩm.

## Chức năng

### Bán hàng
- Quét mã bằng đầu đọc (hoặc gõ mã rồi Enter) để thêm vào đơn. Quét lại mã đã có thì tăng số lượng.
- Chỉnh số lượng bằng nút − / +, gõ trực tiếp, hoặc phím `+` / `−` trên dòng đang chọn. `Del` xóa dòng.
- Giảm giá theo số tiền, chọn phương thức: tiền mặt, chuyển khoản hoặc thẻ.
- Tiền mặt: nhập tiền khách đưa (hoặc bấm nút gợi ý mệnh giá), phần mềm tính tiền thừa và chặn thanh toán nếu khách đưa chưa đủ.
- Quét phải mã chưa có trong danh mục: phần mềm báo lỗi (có tiếng bíp) và hiện nút **Thêm vào danh mục**.
  Nhập tên và giá xong sẽ tự quay lại màn hình bán hàng với sản phẩm đã nằm trong đơn.

| Phím | Tác dụng |
|---|---|
| `F2` | Về ô quét mã |
| `F4` | Tới ô khách đưa |
| `Enter` (ở ô khách đưa) hoặc `F9` | Thanh toán |
| `F8` | Hủy đơn |
| `Enter` / `Esc` (ở cửa sổ hóa đơn) | Đóng, bán đơn tiếp theo |
| `Ctrl+P` (ở cửa sổ hóa đơn) | In hóa đơn |

### Hóa đơn
- Mỗi lần thanh toán lưu một hóa đơn, số hóa đơn dạng `HD260925-0001` (đánh số lại mỗi ngày).
- Hóa đơn lưu lại tên và giá lúc bán, nên sửa hoặc xóa sản phẩm sau đó không làm thay đổi hóa đơn cũ.
- Sau khi thanh toán hiện cửa sổ hóa đơn (bill khổ 80mm) kèm số tiền thừa, có nút in.
  Có thể bật in tự động ra máy in mặc định trong **Cài đặt**.
- Trang **Hóa đơn**: lọc theo ngày (Hôm nay / 7 ngày / Tháng này) hoặc số hóa đơn, xem tổng số hóa đơn,
  doanh thu và số sản phẩm đã bán, xem chi tiết và in lại.

### Sản phẩm
Thêm, sửa, xóa, tìm kiếm sản phẩm (mã vạch, tên, giá). Khi đang ở trang này, quét mã sẽ điền vào form
thay vì thêm vào đơn hàng.

### Cài đặt
- Kết nối đầu đọc: chế độ TCP Client / TCP Server, IP, cổng, lệnh trigger mềm, chống đọc lặp, tự kết nối.
- Thông tin cửa hàng in trên hóa đơn: tên, địa chỉ, điện thoại, lời chào cuối hóa đơn.

## Chạy phần mềm

Yêu cầu Windows và .NET 10 SDK.

```powershell
dotnet run --project CheckBarcodeSieuThi.csproj
```

Hoặc mở `CheckBarcodeSieuThi.slnx` bằng Visual Studio.

Dữ liệu (`products.db`: sản phẩm và hóa đơn) và cấu hình (`settings.json`) lưu tại
`%LocalAppData%\CheckBarcodeSieuThi\`. Nên sao lưu thư mục này định kỳ.

## Cấu hình đầu đọc ICW VNG

Theo datasheet, đầu đọc có cổng Gigabit Ethernet, hỗ trợ **TCP/IP** ở chế độ **no protocol**
(gửi chuỗi mã dạng text thô). Datasheet không ghi IP, cổng mặc định hay lệnh trigger, nên các thông số
này cần xem và đặt trong phần mềm cấu hình đi kèm đầu đọc:

1. **Mạng**: đặt đầu đọc và PC cùng dải mạng, ví dụ đầu đọc `192.168.1.100`, PC `192.168.1.10`, subnet `255.255.255.0`.
2. **Giao thức truyền**: chọn TCP/IP, kiểu *no protocol* (không phải Modbus TCP, EtherNet/IP, Profinet hay Melsec).
3. **Vai trò TCP**, chọn một trong hai:
   - Đầu đọc là **TCP Server**, cổng ví dụ `2001`. Trong phần mềm chọn *TCP Client*, nhập IP đầu đọc và cổng đó.
   - Đầu đọc là **TCP Client**, trỏ tới IP PC và cổng ví dụ `2001`. Trong phần mềm chọn *TCP Server*, cổng `2001`.
     Cần mở cổng này trong Windows Firewall (Inbound, TCP).
4. **Ký tự kết thúc (suffix)**: nên đặt `CR+LF`. Phần mềm cũng hiểu `CR`, `LF`, `STX…ETX`.
   Nếu không có suffix, mã được chốt sau 100 ms không có dữ liệu mới.
5. **Kiểu kích đọc**:
   - Dùng cảm biến (input cứng) hoặc đọc liên tục: để trống ô *Lệnh trigger*.
   - Dùng trigger mềm qua TCP: nhập chuỗi lệnh đã cấu hình trong đầu đọc vào ô *Lệnh trigger*,
     dùng `\r`, `\n` cho ký tự xuống dòng (ví dụ `T\r\n`), rồi bấm **Gửi trigger**.

**Chống đọc lặp** (mặc định 300 ms): bỏ qua mã giống hệt đến liên tiếp trong khoảng này, vì đầu đọc
có thể gửi lặp một mã. Không nên đặt lớn hơn khoảng 500 ms, nếu không khi thu ngân quét nhanh 2 món
giống nhau, món thứ hai sẽ bị bỏ qua và đơn bị tính thiếu. Nếu đầu đọc để chế độ đọc liên tục, nên
đặt khoảng chống lặp ngay trong phần mềm cấu hình của đầu đọc.

## In hóa đơn

Hóa đơn được thiết kế cho máy in nhiệt khổ 80mm. Máy in A4 vẫn in được, bill nằm ở góc trái trang.
Để test khi chưa có máy in, chọn *Microsoft Print to PDF*.

## Test khi chưa có đầu đọc (simulator)

```powershell
# Giả lập đầu đọc chạy TCP Server trên cổng 2001
dotnet run --project tools/ReaderSimulator -- --mode server --port 2001
```

Trong phần mềm vào **Cài đặt**, chọn *TCP Client*, IP `127.0.0.1`, cổng `2001`, bấm **Kết nối**.
Gõ mã trong cửa sổ simulator rồi Enter để gửi. Enter trống sẽ gửi một mã mẫu ngẫu nhiên.

Các tùy chọn khác:

| Tham số | Giá trị |
|---|---|
| `--mode` | `server` (mặc định) hoặc `client` (giả lập đầu đọc chạy TCP Client, phần mềm chọn *TCP Server*) |
| `--host` | IP của PC khi `--mode client` (mặc định `127.0.0.1`) |
| `--port` | Cổng (mặc định `2001`) |
| `--suffix` | `crlf` (mặc định), `cr`, `lf`, `stx`, `none` |

## Cấu trúc code

```
Models/Product.cs, Invoice.cs      Sản phẩm, hóa đơn, dòng hàng, phương thức thanh toán
Data/Database.cs                   Kết nối SQLite, tạo bảng
Data/ProductRepository.cs          CRUD sản phẩm (barcode là UNIQUE)
Data/InvoiceRepository.cs          Lưu hóa đơn (transaction, cấp số HĐ), tra cứu, thống kê doanh thu
Services/BarcodeReaderService.cs   Kết nối TCP với đầu đọc, tách khung dữ liệu, tự kết nối lại, gửi trigger
Services/ReceiptBuilder.cs         Dựng bill 80mm (FlowDocument) để xem trước và in
Services/AppSettings.cs            Cấu hình (settings.json)
Helpers/Money.cs                   Đọc / định dạng tiền VNĐ
ViewModels/                        MainViewModel (khung + điều hướng), PosViewModel (bán hàng),
                                   InvoicesViewModel, ProductsViewModel, SettingsViewModel
Views/                             PosView, InvoicesView, ProductsView, SettingsView, ReceiptWindow
Themes/Styles.xaml                 Bảng màu và style dùng chung (nút, ô nhập, bảng, menu...)
tools/ReaderSimulator/             Chương trình giả lập đầu đọc
```
