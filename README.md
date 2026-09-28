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
- **Cấu hình bên trong đầu đọc** (thay cho phần mềm ShiJieConfig của hãng, xem mục riêng bên dưới):
  tìm đầu đọc trong mạng, chế độ quét, loại mã, ký tự đầu/cuối, đèn, âm báo, camera, khôi phục mặc định.
- Thông tin cửa hàng in trên hóa đơn: tên, địa chỉ, điện thoại, lời chào cuối hóa đơn.

## Chạy phần mềm

Yêu cầu Windows và .NET 10 SDK.

```powershell
dotnet run --project CheckBarcodeSieuThi.csproj
```

Hoặc mở `CheckBarcodeSieuThi.slnx` bằng Visual Studio.

Dữ liệu (`products.db`: sản phẩm và hóa đơn) và cấu hình (`settings.json`) lưu tại
`%LocalAppData%\CheckBarcodeSieuThi\`. Nên sao lưu thư mục này định kỳ.

## Kết nối đầu đọc ICW (Shijie / Superlead)

Đầu đọc mặc định chạy **TCP Server** tại IP tĩnh (ví dụ `192.168.1.100`), cổng **18204**, gửi mã dạng
text kèm `CR+LF` (no protocol). Đầu đọc chấp nhận nhiều kết nối cùng lúc nên phần mềm này và
ShiJieConfig có thể cùng mở.

1. **Mạng**: đặt PC cùng dải với đầu đọc (ví dụ PC `192.168.1.240`, subnet `255.255.255.0`).
2. Vào **Cài đặt → Cấu hình bên trong đầu đọc → Tìm đầu đọc trong mạng**: phần mềm broadcast UDP
   và liệt kê các đầu đọc kèm IP, cổng, model, số serial. Bấm vào một dòng để điền IP và cổng, rồi **Kết nối**.
3. Nếu đầu đọc được đặt làm **TCP Client** trỏ tới PC: chọn *TCP Server* và cùng cổng, mở cổng đó trong Windows Firewall.
4. **Ký tự kết thúc (suffix)**: để `[CR][LF]`. Phần mềm cũng hiểu `CR`, `LF`, `STX…ETX`;
   không có suffix thì mã được chốt sau 100 ms không có dữ liệu mới.

**Chống đọc lặp** (mặc định 300 ms, trong phần mềm): bỏ qua mã giống hệt đến liên tiếp trong khoảng này.
Không nên đặt lớn hơn khoảng 500 ms, nếu không khi thu ngân quét nhanh 2 món giống nhau, món thứ hai
sẽ bị bỏ qua. Đầu đọc còn có chống lặp riêng (*Chống đọc lặp trên đầu đọc*, mặc định 750 ms).

## Cấu hình bên trong đầu đọc (thay cho ShiJieConfig)

Trang **Cài đặt** có thẻ *Cấu hình bên trong đầu đọc*. Khi kết nối xong, phần mềm tự đọc model,
firmware, số serial và các tham số; sửa xong bấm **Lưu vào đầu đọc** (chỉ ghi tham số đã thay đổi,
lưu bền vào bộ nhớ đầu đọc). Các tham số gồm:

| Nhóm | Tham số |
|---|---|
| Chế độ quét | Kích đọc (trigger) / đọc liên tục, chống đọc lặp trên đầu đọc, thời gian giải mã tối đa, giữ đọc sau trigger, số mã mỗi lần đọc, chuỗi lệnh trigger/dừng, nút kích đọc thử |
| Loại mã | EAN-13/8, UPC-A/E, Code 128, Code 39, Code 93, Interleaved 2 of 5, QR, Data Matrix, PDF417 |
| Dữ liệu gửi về | Prefix, suffix (`[CR]` `[LF]` `[TAB]` `\xNN`), chuỗi báo lỗi khi không đọc được |
| Đèn, âm báo | Bật/tắt đèn chiếu, độ sáng, chọn đèn 1–4, đèn ngắm, âm báo |
| Camera | Chế độ phơi sáng, thời gian phơi sáng, gain cho từng chế độ đọc, lấy nét |

Khi kết nối xong phần mềm bật đọc theo chế độ đã lưu; khi thoát phần mềm (hoặc bấm **Dừng đọc**) đầu đọc
được tạm chuyển sang chế độ trigger bằng lệnh ghi tạm nên ngừng đọc, cấu hình đã lưu không đổi.

Nút **Cấu hình mẫu siêu thị** điền sẵn bộ giá trị cho quầy thu ngân (đọc liên tục, chỉ bật EAN/UPC/Code 128/QR
để tránh đọc nhầm, chống lặp 1500 ms, suffix CR+LF); **Khôi phục mặc định nhà sản xuất** đưa đầu đọc về cấu hình gốc (đầu đọc khởi động lại).
Địa chỉ IP của đầu đọc không đổi được bằng phần mềm này, dùng ShiJieConfig nếu cần đổi IP.

### Giao thức (dựng lại từ ShiJieConfig 1.0.71, đã thử trên ICW76Pro firmware 1.0.52)

Lệnh cấu hình đi trên cùng kết nối TCP nhận mã vạch (`Services/ShiYinProtocol.cs`):

```
Khung menu:        02 F0 03  <lệnh>[;<lệnh>...] "."        ghi tạm: kết thúc bằng "!."
Khung sản phẩm:    02 F1 03  <lệnh> "."                    (0F0100? model, 0F0800? số serial)
Lệnh:              <mã 6 hex> + "?" (đọc) | "*" (khoảng cho phép) | <giá trị> (ghi)
Trả lời:           <mã 6 hex><giá trị><06 ACK | 15 NAK | 05 ENQ>  cách nhau ";" kết thúc "." (hoặc "!")
Lưu bền:           thêm lệnh 050203 vào cuối khung ghi
Khôi phục gốc:     0D0100;050203
Trigger / dừng:    02 F4 03  /  02 F5 03
Tìm thiết bị:      UDP broadcast "ShiYinScannerr\0Json" tới cổng 6000, đầu đọc trả JSON (IP, PortNumber, TypeNames, SN...)
```

Một số mã tham số: `0E0100` chế độ quét (0 trigger, 8 liên tục), `080B06` chống lặp, `0E0105` trigger timeout,
`024D21`/`024E0D` decode timeout, `080400`/`080500` prefix/suffix (`99` + hex), `0F0140`/`025004` báo lỗi,
`0213xx` EAN, `0211xx` UPC, `020A01` Code128, `020301` Code39, `020D01` Code93, `020401` I25, `021F01` PDF417,
`023701` QR, `023601` Data Matrix, `040905` đèn, `0F0145` độ sáng, `0F0123` chọn đèn (bit 1–4), `040906` đèn ngắm,
`0F0141` âm báo, `040211`/`040306`/`040307` phơi sáng liên tục, `040210`/`040217`/`040216` phơi sáng trigger, `0F028B` lấy nét.
Phản hồi lệnh được `BarcodeReaderService` tách khỏi luồng mã vạch nhờ ký tự ACK/NAK/ENQ.

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
Simulator cũng trả lời các lệnh cấu hình (đọc/ghi tham số, trigger) nên thẻ *Cấu hình bên trong đầu đọc*
dùng được với nó (không hỗ trợ tìm thiết bị qua UDP).

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
Services/BarcodeReaderService.cs   Kết nối TCP với đầu đọc, tách khung dữ liệu, tự kết nối lại, gửi lệnh và nhận trả lời
Services/ShiYinProtocol.cs         Đóng gói / phân tích khung lệnh cấu hình của đầu đọc Shijie, đổi hex <-> [CR][LF]
Services/ReaderConfigService.cs    Đọc thông tin thiết bị, đọc / ghi / khôi phục tham số, trigger
Services/ReaderDiscovery.cs        Tìm đầu đọc trong LAN qua UDP broadcast
Services/ReceiptBuilder.cs         Dựng bill 80mm (FlowDocument) để xem trước và in
Services/AppSettings.cs            Cấu hình (settings.json)
Helpers/Money.cs                   Đọc / định dạng tiền VNĐ
ViewModels/                        MainViewModel (khung + điều hướng), PosViewModel (bán hàng),
                                   InvoicesViewModel, ProductsViewModel, SettingsViewModel,
                                   ReaderConfigViewModel (cấu hình bên trong đầu đọc)
Views/                             PosView, InvoicesView, ProductsView, SettingsView, ReceiptWindow
Themes/Styles.xaml                 Bảng màu và style dùng chung (nút, ô nhập, bảng, menu...)
tools/ReaderSimulator/             Chương trình giả lập đầu đọc
```
