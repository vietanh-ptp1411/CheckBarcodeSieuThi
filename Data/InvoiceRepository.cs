using System.Globalization;
using CheckBarcodeSieuThi.Models;
using Microsoft.Data.Sqlite;

namespace CheckBarcodeSieuThi.Data
{
    public record SalesSummary(int InvoiceCount, decimal Revenue, int ItemCount);

    /// <summary>
    /// Lưu và tra cứu hóa đơn bán hàng.
    /// </summary>
    public class InvoiceRepository(Database db)
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Lưu hóa đơn kèm các dòng hàng trong một transaction, cấp số hóa đơn HDyyMMdd-NNNN.
        /// </summary>
        public Invoice Create(Invoice invoice)
        {
            using var conn = db.Open();
            using var tx = conn.BeginTransaction();

            var prefix = $"HD{invoice.CreatedAt:yyMMdd}-";
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT COALESCE(MAX(CAST(substr(Number, $start) AS INTEGER)), 0)
                    FROM Invoices WHERE Number LIKE $prefix || '%'
                    """;
                cmd.Parameters.AddWithValue("$start", prefix.Length + 1);
                cmd.Parameters.AddWithValue("$prefix", prefix);
                var seq = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) + 1;
                invoice.Number = $"{prefix}{seq:0000}";
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Invoices (Number, CreatedAt, TotalQuantity, Subtotal, Discount, Total,
                                          PaymentMethod, CustomerPaid, ChangeDue)
                    VALUES ($number, $createdAt, $qty, $subtotal, $discount, $total, $method, $paid, $change);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$number", invoice.Number);
                cmd.Parameters.AddWithValue("$createdAt", invoice.CreatedAt.ToString(DateFormat, CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$qty", invoice.TotalQuantity);
                cmd.Parameters.AddWithValue("$subtotal", invoice.Subtotal);
                cmd.Parameters.AddWithValue("$discount", invoice.Discount);
                cmd.Parameters.AddWithValue("$total", invoice.Total);
                cmd.Parameters.AddWithValue("$method", invoice.PaymentMethod.ToString());
                cmd.Parameters.AddWithValue("$paid", invoice.CustomerPaid);
                cmd.Parameters.AddWithValue("$change", invoice.ChangeDue);
                invoice.Id = (long)cmd.ExecuteScalar()!;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO InvoiceItems (InvoiceId, ProductId, Barcode, Name, UnitPrice, Quantity, LineTotal)
                    VALUES ($invoiceId, $productId, $barcode, $name, $price, $qty, $lineTotal);
                    SELECT last_insert_rowid();
                    """;
                var pInvoice = cmd.Parameters.Add("$invoiceId", SqliteType.Integer);
                var pProduct = cmd.Parameters.Add("$productId", SqliteType.Integer);
                var pBarcode = cmd.Parameters.Add("$barcode", SqliteType.Text);
                var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                var pPrice = cmd.Parameters.Add("$price", SqliteType.Text);
                var pQty = cmd.Parameters.Add("$qty", SqliteType.Integer);
                var pLine = cmd.Parameters.Add("$lineTotal", SqliteType.Text);

                foreach (var item in invoice.Items)
                {
                    item.InvoiceId = invoice.Id;
                    pInvoice.Value = invoice.Id;
                    pProduct.Value = (object?)item.ProductId ?? DBNull.Value;
                    pBarcode.Value = item.Barcode;
                    pName.Value = item.Name;
                    pPrice.Value = item.UnitPrice;
                    pQty.Value = item.Quantity;
                    pLine.Value = item.LineTotal;
                    item.Id = (long)cmd.ExecuteScalar()!;
                }
            }

            tx.Commit();
            return invoice;
        }

        /// <summary>Danh sách hóa đơn (không kèm dòng hàng) trong khoảng ngày, mới nhất trước.</summary>
        public List<Invoice> Search(DateTime fromDate, DateTime toDate, string? keyword)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Number, CreatedAt, TotalQuantity, Subtotal, Discount, Total, PaymentMethod, CustomerPaid, ChangeDue
                FROM Invoices
                WHERE CreatedAt >= $from AND CreatedAt < $to
                  AND ($kw IS NULL OR Number LIKE '%' || $kw || '%')
                ORDER BY CreatedAt DESC, Id DESC
                """;
            AddRange(cmd, fromDate, toDate);
            cmd.Parameters.AddWithValue("$kw", string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : (object)keyword.Trim());

            using var r = cmd.ExecuteReader();
            var list = new List<Invoice>();
            while (r.Read())
                list.Add(MapInvoice(r));
            return list;
        }

        /// <summary>Hóa đơn đầy đủ kèm các dòng hàng.</summary>
        public Invoice? GetById(long id)
        {
            using var conn = db.Open();
            Invoice invoice;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, Number, CreatedAt, TotalQuantity, Subtotal, Discount, Total, PaymentMethod, CustomerPaid, ChangeDue
                    FROM Invoices WHERE Id = $id
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                invoice = MapInvoice(r);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, InvoiceId, ProductId, Barcode, Name, UnitPrice, Quantity, LineTotal
                    FROM InvoiceItems WHERE InvoiceId = $id ORDER BY Id
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    invoice.Items.Add(new InvoiceItem
                    {
                        Id = r.GetInt64(0),
                        InvoiceId = r.GetInt64(1),
                        ProductId = r.IsDBNull(2) ? null : r.GetInt64(2),
                        Barcode = r.GetString(3),
                        Name = r.GetString(4),
                        UnitPrice = r.GetDecimal(5),
                        Quantity = r.GetInt32(6),
                        LineTotal = r.GetDecimal(7),
                    });
                }
            }

            return invoice;
        }

        public SalesSummary GetSummary(DateTime fromDate, DateTime toDate)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(Total), 0), COALESCE(SUM(TotalQuantity), 0)
                FROM Invoices WHERE CreatedAt >= $from AND CreatedAt < $to
                """;
            AddRange(cmd, fromDate, toDate);
            using var r = cmd.ExecuteReader();
            r.Read();
            return new SalesSummary(r.GetInt32(0), r.GetDecimal(1), r.GetInt32(2));
        }

        /// <summary>Khoảng ngày tính trọn ngày: từ 00:00 của fromDate đến hết ngày toDate.</summary>
        private static void AddRange(SqliteCommand cmd, DateTime fromDate, DateTime toDate)
        {
            cmd.Parameters.AddWithValue("$from", fromDate.Date.ToString(DateFormat, CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$to", toDate.Date.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        private static Invoice MapInvoice(SqliteDataReader r) => new()
        {
            Id = r.GetInt64(0),
            Number = r.GetString(1),
            CreatedAt = DateTime.ParseExact(r.GetString(2), DateFormat, CultureInfo.InvariantCulture),
            TotalQuantity = r.GetInt32(3),
            Subtotal = r.GetDecimal(4),
            Discount = r.GetDecimal(5),
            Total = r.GetDecimal(6),
            PaymentMethod = Enum.TryParse<PaymentMethod>(r.GetString(7), out var m) ? m : PaymentMethod.Cash,
            CustomerPaid = r.GetDecimal(8),
            ChangeDue = r.GetDecimal(9),
        };
    }
}
