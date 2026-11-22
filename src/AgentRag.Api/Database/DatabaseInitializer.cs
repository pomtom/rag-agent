// Runs once at startup: creates the database (if missing) and then the tables (schema.sql).
//
// This keeps "clone the repo, press F5" working without any manual SQL steps.

using Microsoft.Data.SqlClient;

namespace AgentRag.Api.Database;

public sealed class DatabaseInitializer
{
    private readonly SqlConnectionFactory _connections;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(SqlConnectionFactory connections, ILogger<DatabaseInitializer> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await CreateDatabaseIfMissingAsync(cancellationToken);
        await CreateTablesAsync(cancellationToken);
    }

    private async Task CreateDatabaseIfMissingAsync(CancellationToken cancellationToken)
    {
        // Connect to "master", because our own database may not exist yet.
        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(_connections.ConnectionString);
        string databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        const string sql = @"
IF DB_ID(@name) IS NULL
BEGIN
    DECLARE @create NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@name);
    EXEC (@create);
END;";

        using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@name", databaseName);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        _logger.LogInformation("Database '{Database}' is ready.", databaseName);
    }

    private async Task CreateTablesAsync(CancellationToken cancellationToken)
    {
        string schemaFile = Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql");
        string sql = await File.ReadAllTextAsync(schemaFile, cancellationToken);

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Tables Documents and DocumentChunks are ready.");
    }
}
