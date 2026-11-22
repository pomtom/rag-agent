// Opens connections to the AgentRag database. The only class that knows the connection string.
//
// The connection string "AgentRag" comes from the Aspire AppHost (ConnectionStrings__AgentRag).
// ADO.NET pools connections, so "open, use, dispose" for every query is the normal, fast way.

using Microsoft.Data.SqlClient;

namespace AgentRag.Api.Database;

public sealed class SqlConnectionFactory
{
    public const string ConnectionStringName = "AgentRag";

    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string '" + ConnectionStringName + "' is missing. "
                + "Start the app through AgentRag.AppHost, or set ConnectionStrings:" + ConnectionStringName + ".");
        }

        _connectionString = connectionString;
    }

    public string ConnectionString
    {
        get { return _connectionString; }
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
