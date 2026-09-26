using CheckBarcodeSieuThi.Models;
using Microsoft.Data.Sqlite;

namespace CheckBarcodeSieuThi.Data
{
    /// <summary>
    /// Thêm / sửa / xóa / tìm sản phẩm.
    /// </summary>
    public class ProductRepository(Database db)
    {
        public List<Product> GetAll()
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Barcode, Name, Price FROM Products ORDER BY Name COLLATE NOCASE";
            using var reader = cmd.ExecuteReader();

            var list = new List<Product>();
            while (reader.Read())
                list.Add(Map(reader));
            return list;
        }

        public Product? GetByBarcode(string barcode)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Barcode, Name, Price FROM Products WHERE Barcode = $barcode";
            cmd.Parameters.AddWithValue("$barcode", barcode);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }

        /// <summary>Thêm mới. Ném <see cref="DuplicateBarcodeException"/> nếu barcode đã tồn tại.</summary>
        public Product Add(Product p)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Products (Barcode, Name, Price) VALUES ($barcode, $name, $price);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$barcode", p.Barcode);
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$price", p.Price);

            try
            {
                p.Id = (long)cmd.ExecuteScalar()!;
                return p;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
            {
                throw new DuplicateBarcodeException(p.Barcode);
            }
        }

        /// <summary>Cập nhật theo Id. Trả về false nếu không còn bản ghi.</summary>
        public bool Update(Product p)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Products SET Barcode = $barcode, Name = $name, Price = $price WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", p.Id);
            cmd.Parameters.AddWithValue("$barcode", p.Barcode);
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$price", p.Price);

            try
            {
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                throw new DuplicateBarcodeException(p.Barcode);
            }
        }

        public bool Delete(long id)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Products WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }

        private static Product Map(SqliteDataReader r) => new()
        {
            Id = r.GetInt64(0),
            Barcode = r.GetString(1),
            Name = r.GetString(2),
            Price = r.GetDecimal(3),
        };
    }

    public class DuplicateBarcodeException(string barcode)
        : Exception($"Barcode '{barcode}' đã tồn tại.")
    {
        public string Barcode { get; } = barcode;
    }
}
