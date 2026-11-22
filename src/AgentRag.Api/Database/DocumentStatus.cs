// The life of an uploaded file, stored in Documents.Status:
//
//   Pending  --(IngestionWorker picks it up)-->  Processing  --(all chunks saved)-->  Completed
//                                                     |
//                                                     +--(any error)-->  Failed   (see Documents.ErrorMessage)
//
// "Reprocess" puts a file back to Pending.

namespace AgentRag.Api.Database;

public static class DocumentStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
