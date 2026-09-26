using System.Globalization;

namespace CheckBarcodeSieuThi.Helpers
{
    /// <summary>
    /// Tiền tính theo đồng, không có phần lẻ.
    /// </summary>
    public static class Money
    {
        private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

        /// <summary>Chấp nhận "15000", "15.000", "15,000", "15 000 đ". Chuỗi rỗng không hợp lệ.</summary>
        public static bool TryParse(string? text, out decimal value)
        {
            var digits = new string((text ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('.' or ',')).ToArray())
                .Replace("₫", "").Replace("đ", "").Replace("VND", "", StringComparison.OrdinalIgnoreCase);
            return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>15000 -> "15.000"</summary>
        public static string Format(decimal value) => value.ToString("#,##0", Vi);

        /// <summary>15000 -> "15.000 ₫"</summary>
        public static string FormatVnd(decimal value) => Format(value) + " ₫";
    }
}
