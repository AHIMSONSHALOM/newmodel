using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ProductHub_MVC.Data
{
    public class SqlDbContext
    {
        private readonly string _connectionString;

        public SqlDbContext(IConfiguration configuration)
        {
            // Pulls the active 'ProductHubSqlConnection' connection string parameter from your appsettings.json file
            _connectionString = configuration.GetConnectionString("ProductHubSqlConnection") 
                ?? throw new InvalidOperationException("Critical Error: Connection string 'ProductHubSqlConnection' was not found.");
        }

        public IDbConnection CreateConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}