// The ingestion pipeline for ONE document:
//
//   file bytes --TextExtractor--> text --TextChunker--> chunks --embedding model--> vectors --ChunkRepository--> SQL
//
// It does not know about queues, statuses or retries: IngestionWorker takes care of that.

using AgentRag.Api.Database;
using Microsoft.Extensions.AI;

namespace AgentRag.Api.Ingestion;

public sealed class DocumentIngestor
{
    // Chunks sent to the embedding model per call. Fewer calls = faster; the API allows up to 2048 inputs.
    private const int EmbeddingBatchSize = 50;

    private readonly TextExtractor _textExtractor;
    private readonly TextChunker _textChunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ChunkRepository _chunks;
    private readonly ILogger<DocumentIngestor> _logger;

    public DocumentIngestor(
        TextExtractor textExtractor,
        TextChunker textChunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ChunkRepository chunks,
        ILogger<DocumentIngestor> logger)
    {
        _textExtractor = textExtractor;
        _textChunker = textChunker;
        _embeddingGenerator = embeddingGenerator;
        _chunks = chunks;
        _logger = logger;
    }

    /// <summary>Processes the document and returns how many chunks were saved. Throws when something goes wrong.</summary>
    public async Task<int> IngestAsync(PendingDocument document, CancellationToken cancellationToken)
    {
        // 1. Read: file -> plain text.
        string text = _textExtractor.Extract(document.FileExtension, document.FileContent);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("No text found in the file (a scanned PDF has only images and needs OCR first).");
        }

        // 2. Chunk: text -> pieces of about 2,000 characters.
        List<string> chunkTexts = _textChunker.Split(text);
        _logger.LogInformation("{FileName}: {Characters} characters -> {Chunks} chunks.", document.FileName, text.Length, chunkTexts.Count);

        // 3. Embed: every chunk -> a vector of 1536 numbers, in batches.
        List<ChunkToSave> chunks = new List<ChunkToSave>();
        for (int start = 0; start < chunkTexts.Count; start += EmbeddingBatchSize)
        {
            List<string> batch = chunkTexts.Skip(start).Take(EmbeddingBatchSize).ToList();
            GeneratedEmbeddings<Embedding<float>> embeddings = await _embeddingGenerator.GenerateAsync(batch, cancellationToken: cancellationToken);

            for (int i = 0; i < batch.Count; i++)
            {
                chunks.Add(new ChunkToSave(start + i, batch[i], embeddings[i].Vector));
            }
        }

        // 4. Store: replace the document's chunks in SQL Server.
        await _chunks.ReplaceChunksAsync(document.Id, chunks, cancellationToken);

        return chunks.Count;
    }
}
