using System.Data;

namespace SRM_by_Longtygu.Database
{
    public interface IDatabaseConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}