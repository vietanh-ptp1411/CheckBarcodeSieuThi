using Microsoft.Data.Sqlite;

namespace CheckBarcodeSieuThi.Data
{
    /// <summary>
    /// Kết nối SQLite và tạo bảng nếu chưa có (Products, Invoices, InvoiceItems).
    /// </summary>
    public class Database
    {
        private readonly string _connectionString;

        public Database(string dbPath)
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                ForeignKeys = true,
            }.ToString();
            EnsureSchema();
        }

        public SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        private void EnsureSchema()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Products (
                    Id      INTEGER PRIMARY KEY AUTOINCREMENT,
                    Barcode TEXT    NOT NULL UNIQUE,
                    Name    TEXT    NOT NULL,
                    Price   NUMERIC NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Invoices (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    Number        TEXT    NOT NULL UNIQUE,
                    CreatedAt     TEXT    NOT NULL,
                    TotalQuantity INTEGER NOT NULL,
                    Subtotal      NUMERIC NOT NULL,
                    Discount      NUMERIC NOT NULL DEFAULT 0,
                    Total         NUMERIC NOT NULL,
                    PaymentMethod TEXT    NOT NULL,
                    CustomerPaid  NUMERIC NOT NULL,
                    ChangeDue     NUMERIC NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Invoices_CreatedAt ON Invoices(CreatedAt);

                CREATE TABLE IF NOT EXISTS InvoiceItems (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceId INTEGER NOT NULL REFERENCES Invoices(Id) ON DELETE CASCADE,
                    ProductId INTEGER REFERENCES Products(Id) ON DELETE SET NULL,
                    Barcode   TEXT    NOT NULL,
                    Name      TEXT    NOT NULL,
                    UnitPrice NUMERIC NOT NULL,
                    Quantity  INTEGER NOT NULL,
                    LineTotal NUMERIC NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_InvoiceItems_InvoiceId ON InvoiceItems(InvoiceId);
                """;
            cmd.ExecuteNonQuery();
        }
    }
}
