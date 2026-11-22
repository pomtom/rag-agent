// The background service: runs for as long as the API runs, and turns Pending documents into vectors.
//
//   loop:
//     take the oldest Pending document         (nothing? wait a few seconds and look again)
//     Status = Processing
//     DocumentIngestor.IngestAsync(...)        (read -> chunk -> embed -> save)
//     Status = Completed  (or Failed + the error message)
//
// WHY POLL THE TABLE (and not an in-memory queue)
//   The table IS the queue. Nothing is lost when the app restarts, and you can see the queue in SSMS.
//
// BackgroundService is the standard .NET base class for this: ASP.NET Core starts ExecuteAsync
// when the app starts and cancels the token when the app stops.

using AgentRag.Api.Database;

namespace AgentRag.Api.Ingestion;

public sealed class IngestionWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly DocumentRepository _documents;
    private readonly DocumentIngestor _ingestor;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(DocumentRepository documents, DocumentIngestor ingestor, ILogger<IngestionWorker> logger)
    {
        _documents = documents;
        _ingestor = ingestor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Documents left in "Processing" by a previous run (crash, debugger stopped) go back in the queue.
        int reset = await _documents.ResetInterruptedAsync(stoppingToken);
        if (reset > 0)
        {
            _logger.LogInformation("Put {Count} interrupted document(s) back to Pending.", reset);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool processedOne = await ProcessNextAsync(stoppingToken);
                if (!processedOne)
                {
                    await Task.Delay(PollInterval, stoppingToken);   // the queue is empty
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;   // the app is stopping
            }
            catch (Exception ex)
            {
                // e.g. SQL Server not reachable. Log it, wait, and keep the service alive.
                _logger.LogError(ex, "The ingestion loop failed; trying again in {Seconds} s.", PollInterval.TotalSeconds);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    // True when a document was taken from the queue (so look for the next one right away).
    private async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        PendingDocument? document = await _documents.GetNextPendingAsync(stoppingToken);
        if (document == null)
        {
            return false;
        }

        await _documents.MarkProcessingAsync(document.Id, stoppingToken);
        _logger.LogInformation("Ingesting document {Id} '{FileName}'.", document.Id, document.FileName);

        try
        {
            int chunkCount = await _ingestor.IngestAsync(document, stoppingToken);
            await _documents.MarkCompletedAsync(document.Id, chunkCount, stoppingToken);
            _logger.LogInformation("Document {Id} '{FileName}' completed with {Chunks} chunks.", document.Id, document.FileName, chunkCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad file must not stop the others: record why it failed and move on.
            _logger.LogWarning(ex, "Document {Id} '{FileName}' failed.", document.Id, document.FileName);
            await _documents.MarkFailedAsync(document.Id, ex.Message, stoppingToken);
        }

        return true;
    }
}
