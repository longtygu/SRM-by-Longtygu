using System.Data;
using System.Data.SQLite;

namespace SRM_by_Longtygu.Database
{
    public class SqliteConnectionFactory : IDatabaseConnectionFactory
    {
        // Bỏ _connectionString đi
        public SqliteConnectionFactory() { }

        public IDbConnection CreateConnection()
        {
            // Lấy trực tiếp từ Bootstrapper mỗi khi cần gọi
            return new SQLiteConnection(DatabaseBootstrapper.GetConnectionString());
        }
    }
}