// Every SQL statement for the dbo.Documents table, and nothing else.
//
// Plain ADO.NET (no ORM) so you can see exactly which SQL runs.
// Always parameters (@name), never string concatenation: that is what prevents SQL injection.

using System.Data;
using Microsoft.Data.SqlClient;

namespace AgentRag.Api.Database;

public sealed class DocumentRepository
{
    private readonly SqlConnectionFactory _connections;

    public DocumentRepository(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    // ---------- used by the upload endpoint ----------

    /// <summary>The id of a document with exactly the same content, or null.</summary>
    public async Task<int?> FindByHashAsync(string fileHash, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id FROM dbo.Documents WHERE FileHash = @FileHash;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@FileHash", SqlDbType.Char, 64).Value = fileHash;

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (result == null)
            {
                return null;
            }

            return (int)result;
        }
    }

    /// <summary>Saves a new upload with Status = Pending and returns its id.</summary>
    public async Task<int> InsertAsync(
        string fileName, string fileExtension, string? contentType, string fileHash, byte[] fileContent,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO dbo.Documents (FileName, FileExtension, ContentType, FileSizeBytes, FileHash, FileContent, Status)
OUTPUT inserted.Id
VALUES (@FileName, @FileExtension, @ContentType, @FileSizeBytes, @FileHash, @FileContent, @Status);";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = fileName;
            command.Parameters.Add("@FileExtension", SqlDbType.NVarChar, 10).Value = fileExtension;
            command.Parameters.Add("@ContentType", SqlDbType.NVarChar, 100).Value = (object?)contentType ?? DBNull.Value;
            command.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = fileContent.LongLength;
            command.Parameters.Add("@FileHash", SqlDbType.Char, 64).Value = fileHash;
            command.Parameters.Add("@FileContent", SqlDbType.VarBinary, -1).Value = fileContent;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Pending;

            object? id = await command.ExecuteScalarAsync(cancellationToken);
            return (int)id!;
        }
    }

    /// <summary>All documents, newest first (without the file content).</summary>
    public async Task<List<DocumentRow>> ListAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT Id, FileName, FileExtension, FileSizeBytes, Status, ChunkCount, ErrorMessage, UploadedAt, ProcessedAt
FROM dbo.Documents
ORDER BY UploadedAt DESC, Id DESC;";

        List<DocumentRow> documents = new List<DocumentRow>();

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add(new DocumentRow
                {
                    Id = reader.GetInt32(0),
                    FileName = reader.GetString(1),
                    FileExtension = reader.GetString(2),
                    FileSizeBytes = reader.GetInt64(3),
                    Status = reader.GetString(4),
                    ChunkCount = reader.GetInt32(5),
                    ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                    UploadedAt = DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
                    ProcessedAt = reader.IsDBNull(8) ? null : DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)
                });
            }
        }

        return documents;
    }

    /// <summary>Puts a document back in the queue. False when the id does not exist.</summary>
    public async Task<bool> ResetToPendingAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE dbo.Documents
SET Status = @Status, ChunkCount = 0, ErrorMessage = NULL, ProcessedAt = NULL
WHERE Id = @Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Pending;

            int rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
    }

    /// <summary>Deletes a document; its chunks go with it (ON DELETE CASCADE). False when the id does not exist.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.Documents WHERE Id = @Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            int rows = await command.ExecuteNonQueryAsync(cancellationToken);
            return rows > 0;
        }
    }

    // ---------- used by the background ingestion service ----------

    /// <summary>The oldest Pending document (with its content), or null when there is nothing to do.</summary>
    public async Task<PendingDocument?> GetNextPendingAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1) Id, FileName, FileExtension, FileContent
FROM dbo.Documents
WHERE Status = @Status
ORDER BY Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Pending;

            using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                return new PendingDocument
                {
                    Id = reader.GetInt32(0),
                    FileName = reader.GetString(1),
                    FileExtension = reader.GetString(2),
                    FileContent = (byte[])reader[3]
                };
            }
        }
    }

    public async Task MarkProcessingAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Documents SET Status = @Status, ErrorMessage = NULL WHERE Id = @Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Processing;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task MarkCompletedAsync(int id, int chunkCount, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE dbo.Documents
SET Status = @Status, ChunkCount = @ChunkCount, ErrorMessage = NULL, ProcessedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Completed;
            command.Parameters.Add("@ChunkCount", SqlDbType.Int).Value = chunkCount;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(int id, string errorMessage, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE dbo.Documents
SET Status = @Status, ChunkCount = 0, ErrorMessage = @ErrorMessage, ProcessedAt = SYSUTCDATETIME()
WHERE Id = @Id;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = DocumentStatus.Failed;
            command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, -1).Value = errorMessage;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// After a crash or restart, documents can be stuck in Processing. Put them back to Pending
    /// so the background service picks them up again. Returns how many were reset.
    /// </summary>
    public async Task<int> ResetInterruptedAsync(CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.Documents SET Status = @Pending WHERE Status = @Processing;";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Pending", SqlDbType.NVarChar, 20).Value = DocumentStatus.Pending;
            command.Parameters.Add("@Processing", SqlDbType.NVarChar, 20).Value = DocumentStatus.Processing;
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
