using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using CheckBarcodeSieuThi.Helpers;
using CheckBarcodeSieuThi.Models;

namespace CheckBarcodeSieuThi.Services
{
    /// <summary>
    /// Dựng hóa đơn dạng bill máy in nhiệt 80mm (FlowDocument) để xem trước và in.
    /// </summary>
    public static class ReceiptBuilder
    {
        /// <summary>Khổ giấy 80mm ≈ 302 DIP (1 DIP = 1/96 inch).</summary>
        public const double PaperWidth = 302;

        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));

        public static FlowDocument Build(Invoice invoice, AppSettings store, double width = PaperWidth)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = Brushes.Black,
                Background = Brushes.White,
                PagePadding = new Thickness(10, 14, 10, 18),
                PageWidth = width,
                ColumnWidth = width,
                TextAlignment = TextAlignment.Left,
            };
            var contentWidth = width - doc.PagePadding.Left - doc.PagePadding.Right;

            // Thông tin cửa hàng
            doc.Blocks.Add(Text(store.StoreName.ToUpperInvariant(), 16, FontWeights.Bold, TextAlignment.Center));
            if (!string.IsNullOrWhiteSpace(store.StoreAddress))
                doc.Blocks.Add(Text(store.StoreAddress, 11, align: TextAlignment.Center, margin: new Thickness(0, 2, 0, 0)));
            if (!string.IsNullOrWhiteSpace(store.StorePhone))
                doc.Blocks.Add(Text($"ĐT: {store.StorePhone}", 11, align: TextAlignment.Center));

            doc.Blocks.Add(Separator(contentWidth));
            doc.Blocks.Add(Text("HÓA ĐƠN BÁN HÀNG", 15, FontWeights.Bold, TextAlignment.Center, new Thickness(0, 0, 0, 4)));

            var info = NewTable(1.0, 1.4);
            AddRow(info, "Số HĐ:", invoice.Number);
            AddRow(info, "Ngày:", invoice.CreatedAt.ToString("dd/MM/yyyy HH:mm"));
            doc.Blocks.Add(info);

            doc.Blocks.Add(Separator(contentWidth));

            // Dòng hàng: tên ở dòng trên, "SL x đơn giá ... thành tiền" ở dòng dưới
            var items = NewTable(2.2, 1.0);
            var header = new TableRow();
            header.Cells.Add(Cell("Mặt hàng", FontWeights.SemiBold));
            header.Cells.Add(Cell("T.Tiền", FontWeights.SemiBold, TextAlignment.Right));
            items.RowGroups[0].Rows.Add(header);

            foreach (var item in invoice.Items)
            {
                var nameRow = new TableRow();
                var nameCell = Cell(item.Name, margin: new Thickness(0, 5, 0, 0));
                nameCell.ColumnSpan = 2;
                nameRow.Cells.Add(nameCell);
                items.RowGroups[0].Rows.Add(nameRow);

                var qtyRow = new TableRow();
                var qtyCell = Cell($"   {item.Quantity} x {Money.Format(item.UnitPrice)}");
                qtyCell.Foreground = Muted;
                qtyRow.Cells.Add(qtyCell);
                qtyRow.Cells.Add(Cell(Money.Format(item.LineTotal), align: TextAlignment.Right));
                items.RowGroups[0].Rows.Add(qtyRow);
            }
            doc.Blocks.Add(items);

            doc.Blocks.Add(Separator(contentWidth));

            // Tổng tiền
            var totals = NewTable(1.3, 1.0);
            AddRow(totals, "Tổng số lượng:", invoice.TotalQuantity.ToString());
            AddRow(totals, "Tạm tính:", Money.Format(invoice.Subtotal));
            if (invoice.Discount > 0)
                AddRow(totals, "Giảm giá:", "-" + Money.Format(invoice.Discount));
            AddRow(totals, "TỔNG CỘNG:", Money.FormatVnd(invoice.Total), 15, FontWeights.Bold, new Thickness(0, 4, 0, 4));
            AddRow(totals, "Thanh toán:", invoice.PaymentMethodName);
            if (invoice.PaymentMethod == PaymentMethod.Cash)
            {
                AddRow(totals, "Khách đưa:", Money.Format(invoice.CustomerPaid));
                AddRow(totals, "Tiền thừa:", Money.Format(invoice.ChangeDue));
            }
            doc.Blocks.Add(totals);

            doc.Blocks.Add(Separator(contentWidth));
            if (!string.IsNullOrWhiteSpace(store.ReceiptFooter))
            {
                var footer = Text(store.ReceiptFooter, 12, align: TextAlignment.Center);
                footer.FontStyle = FontStyles.Italic;
                doc.Blocks.Add(footer);
            }

            return doc;
        }

        /// <summary>
        /// In hóa đơn. showDialog = false: in thẳng ra máy in mặc định.
        /// Trả về false nếu người dùng bấm hủy ở hộp thoại chọn máy in.
        /// </summary>
        public static bool Print(Invoice invoice, AppSettings store, bool showDialog)
        {
            var dialog = new PrintDialog();
            if (showDialog && dialog.ShowDialog() != true)
                return false;

            // Máy in nhiệt 80mm thường chỉ in được ~72mm; máy in A4 thì in bill ở góc trái
            var width = Math.Min(PaperWidth, dialog.PrintableAreaWidth);
            var doc = Build(invoice, store, width);
            doc.PageHeight = dialog.PrintableAreaHeight;

            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(width, dialog.PrintableAreaHeight);
            dialog.PrintDocument(paginator, $"Hóa đơn {invoice.Number}");
            return true;
        }

        // ------------------------------------------------------------------

        private static Paragraph Text(string text, double size, FontWeight? weight = null,
            TextAlignment align = TextAlignment.Left, Thickness? margin = null) =>
            new(new Run(text))
            {
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                TextAlignment = align,
                Margin = margin ?? new Thickness(0),
            };

        private static Block Separator(double width) =>
            new BlockUIContainer(new Line
            {
                X1 = 0,
                X2 = width,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                StrokeDashArray = [3, 2],
                SnapsToDevicePixels = true,
            })
            { Margin = new Thickness(0, 8, 0, 8) };

        private static Table NewTable(params double[] starWidths)
        {
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            foreach (var w in starWidths)
                table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            table.RowGroups.Add(new TableRowGroup());
            return table;
        }

        private static TableCell Cell(string text, FontWeight? weight = null,
            TextAlignment align = TextAlignment.Left, Thickness? margin = null) =>
            new(Text(text, 12, weight, align)) { Padding = margin ?? new Thickness(0, 1, 0, 1) };

        private static void AddRow(Table table, string label, string value,
            double size = 12, FontWeight? weight = null, Thickness? margin = null)
        {
            var row = new TableRow { FontSize = size, FontWeight = weight ?? FontWeights.Normal };
            var labelCell = Cell(label, weight, margin: margin);
            var valueCell = Cell(value, weight, TextAlignment.Right, margin);
            ((Paragraph)labelCell.Blocks.FirstBlock).FontSize = size;
            ((Paragraph)valueCell.Blocks.FirstBlock).FontSize = size;
            row.Cells.Add(labelCell);
            row.Cells.Add(valueCell);
            table.RowGroups[0].Rows.Add(row);
        }
    }
}
