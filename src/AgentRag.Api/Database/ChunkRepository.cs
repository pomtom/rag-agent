// Every SQL statement for the dbo.DocumentChunks table: save chunks and search them.
//
// HOW VECTORS GO INTO SQL SERVER
//   A vector is sent as JSON text "[0.012, -0.034, ...]" and converted by SQL Server:
//   CAST(@Embedding AS VECTOR(1536)). Simple, and works with every SQL client.
//
// HOW SEARCH WORKS
//   VECTOR_DISTANCE('cosine', a, b) = 0 for the same meaning, up to 2 for the opposite.
//   ORDER BY that distance -> the closest chunks first. This is an exact search over all chunks:
//   fast enough for thousands of chunks, so no vector index is needed for this app.

using System.Data;
using System.Text.Json;
using AgentRag.Api.Search;
using Microsoft.Data.SqlClient;

namespace AgentRag.Api.Database;

public sealed class ChunkRepository
{
    /// <summary>Must match VECTOR(1536) in schema.sql and the embedding model (text-embedding-3-small).</summary>
    public const int VectorDimensions = 1536;

    private readonly SqlConnectionFactory _connections;

    public ChunkRepository(SqlConnectionFactory connections)
    {
        _connections = connections;
    }

    /// <summary>
    /// Replaces ALL chunks of a document in one transaction: either every new chunk is saved, or nothing changes.
    /// "Replace" (not "add") makes re-processing a file safe.
    /// </summary>
    public async Task ReplaceChunksAsync(int documentId, List<ChunkToSave> chunks, CancellationToken cancellationToken)
    {
        const string deleteSql = "DELETE FROM dbo.DocumentChunks WHERE DocumentId = @DocumentId;";
        const string insertSql = @"
INSERT INTO dbo.DocumentChunks (DocumentId, ChunkIndex, Content, Embedding)
VALUES (@DocumentId, @ChunkIndex, @Content, CAST(@Embedding AS VECTOR(1536)));";

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlTransaction transaction = connection.BeginTransaction())
        {
            using (SqlCommand delete = new SqlCommand(deleteSql, connection, transaction))
            {
                delete.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (ChunkToSave chunk in chunks)
            {
                using (SqlCommand insert = new SqlCommand(insertSql, connection, transaction))
                {
                    insert.Parameters.Add("@DocumentId", SqlDbType.Int).Value = documentId;
                    insert.Parameters.Add("@ChunkIndex", SqlDbType.Int).Value = chunk.ChunkIndex;
                    insert.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = chunk.Content;
                    insert.Parameters.Add("@Embedding", SqlDbType.NVarChar, -1).Value = ToJson(chunk.Embedding);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            transaction.Commit();
        }
    }

    /// <summary>The <paramref name="top"/> chunks closest to the question vector, from completed documents only.</summary>
    public async Task<List<SearchResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int top, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (@Top)
       c.DocumentId,
       d.FileName,
       c.ChunkIndex,
       c.Content,
       VECTOR_DISTANCE('cosine', c.Embedding, CAST(@QueryVector AS VECTOR(1536))) AS Distance
FROM dbo.DocumentChunks AS c
JOIN dbo.Documents AS d ON d.Id = c.DocumentId
WHERE d.Status = @Completed
ORDER BY Distance;";

        List<SearchResult> results = new List<SearchResult>();

        using (SqlConnection connection = await _connections.OpenAsync(cancellationToken))
        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Top", SqlDbType.Int).Value = top;
            command.Parameters.Add("@QueryVector", SqlDbType.NVarChar, -1).Value = ToJson(queryVector);
            command.Parameters.Add("@Completed", SqlDbType.NVarChar, 20).Value = DocumentStatus.Completed;

            using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new SearchResult
                    {
                        DocumentId = reader.GetInt32(0),
                        FileName = reader.GetString(1),
                        ChunkIndex = reader.GetInt32(2),
                        Content = reader.GetString(3),
                        Distance = reader.GetDouble(4)
                    });
                }
            }
        }

        return results;
    }

    // [0.1, 0.2, ...] - System.Text.Json always writes numbers with a '.' whatever the PC's language settings.
    private static string ToJson(ReadOnlyMemory<float> vector)
    {
        if (vector.Length != VectorDimensions)
        {
            throw new InvalidOperationException(
                "The embedding has " + vector.Length + " numbers but the database expects " + VectorDimensions
                + ". Use text-embedding-3-small, or change VECTOR(1536) in schema.sql.");
        }

        return JsonSerializer.Serialize(vector.ToArray());
    }
}
