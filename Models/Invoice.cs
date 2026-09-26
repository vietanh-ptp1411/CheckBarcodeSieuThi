namespace CheckBarcodeSieuThi.Models
{
    public enum PaymentMethod
    {
        Cash,
        BankTransfer,
        Card,
    }

    public static class PaymentMethodExtensions
    {
        public static string ToDisplayName(this PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "Tiền mặt",
            PaymentMethod.BankTransfer => "Chuyển khoản",
            PaymentMethod.Card => "Thẻ",
            _ => method.ToString(),
        };
    }

    public class Invoice
    {
        public long Id { get; set; }

        /// <summary>Số hóa đơn dạng HD260925-0001 (đánh số lại mỗi ngày).</summary>
        public string Number { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        public int TotalQuantity { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public decimal CustomerPaid { get; set; }
        public decimal ChangeDue { get; set; }

        public List<InvoiceItem> Items { get; set; } = [];

        public string PaymentMethodName => PaymentMethod.ToDisplayName();
    }

    /// <summary>
    /// Dòng hàng trên hóa đơn. Lưu lại tên và giá tại thời điểm bán,
    /// nên sửa hoặc xóa sản phẩm sau này không làm thay đổi hóa đơn cũ.
    /// </summary>
    public class InvoiceItem
    {
        public long Id { get; set; }
        public long InvoiceId { get; set; }
        public long? ProductId { get; set; }
        public string Barcode { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal { get; set; }
    }
}
